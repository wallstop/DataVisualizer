#!/usr/bin/env bash
# Install and launch isolated Z.ai and OpenRouter backends without changing
# native Codex or Claude defaults.
#
# Launchers installed as: codex-zai, claude-zai, codex-openrouter,
# claude-openrouter (symlinks to this script). The ordinary `codex` and
# `claude` commands keep their native backends.
#
# Credentials: launchers self-load a gitignored .env.local (workspace root
# beside this script, or the current directory, or AI_BACKENDS_ENV_FILE) for
# any credential that is not already exported. Preexisting environment
# variables always win, so non-interactive shells (agents, tasks, CI) resolve
# keys without relying on interactive shell rc sourcing.

set -euo pipefail

readonly PROFILE_NAME="devcontainer-zai"
readonly OPENROUTER_PROFILE_NAME="devcontainer-openrouter"
readonly ZAI_ANTHROPIC_URL="https://api.z.ai/api/anthropic"
readonly OPENROUTER_ANTHROPIC_URL="https://openrouter.ai/api"

LAUNCHER_SCRIPT_PATH="$(realpath "${BASH_SOURCE[0]}")"
readonly WORKSPACE_ROOT="$(cd "$(dirname "${LAUNCHER_SCRIPT_PATH}")/.." && pwd)"

die() {
    printf '[ai-backends] ERROR: %s\n' "$*" >&2
    exit 1
}

# Loads KEY=VALUE lines from .env.local without overriding the environment:
# a non-empty preexisting variable always wins. The parser lives in
# env-local.sh so interactive shells (via ~/.bashrc) and these launchers share
# one implementation (UTF-8 BOM, CRLF, and `export ` tolerance included).
if ! declare -F load_env_local >/dev/null 2>&1; then
    env_local_sh="$(dirname "${BASH_SOURCE[0]}")/env-local.sh"
    [ -f "${env_local_sh}" ] || env_local_sh="${WORKSPACE_ROOT}/.devcontainer/env-local.sh"
    # shellcheck source=./env-local.sh
    source "${env_local_sh}"
fi

resolve_action() {
    local invoked_as
    invoked_as="$(basename "$0")"
    case "${invoked_as}" in
        codex-zai|claude-zai|codex-openrouter|claude-openrouter)
            printf '%s\n' "${invoked_as}"
            ;;
        *)
            printf '%s\n' "${1:-help}"
            ;;
    esac
}

resolve_zai_key() {
    if [ -n "${ZAI_API_KEY:-}" ]; then
        printf '%s' "${ZAI_API_KEY}"
        return
    fi
    if [ -n "${Z_AI_API_KEY:-}" ]; then
        printf '%s' "${Z_AI_API_KEY}"
        return
    fi
    die "Set ZAI_API_KEY (or Z_AI_API_KEY) before launching a Z.ai backend. Add it to a gitignored .env.local at the workspace root (see .env.local.example); launchers load it automatically."
}

resolve_openrouter_key() {
    if [ -n "${OPENROUTER_API_KEY:-}" ]; then
        printf '%s' "${OPENROUTER_API_KEY}"
        return
    fi
    if [ -n "${OPEN_ROUTER_API_KEY:-}" ]; then
        printf '%s' "${OPEN_ROUTER_API_KEY}"
        return
    fi
    die "Set OPENROUTER_API_KEY (or OPEN_ROUTER_API_KEY) before launching an OpenRouter backend. Add it to a gitignored .env.local at the workspace root (see .env.local.example); launchers load it automatically."
}

# Validates a "<VAR>_TIMEOUT_MS" style override and prints the effective value.
resolve_timeout_ms() {
    local var_name="$1" default_ms="$2" raw
    raw="${!var_name:-${default_ms}}"
    case "${raw}" in
        ''|*[!0-9]*) die "${var_name} must be a positive integer." ;;
        0) die "${var_name} must be greater than zero." ;;
    esac
    printf '%s' "${raw}"
}

# Creates and secures a private config directory. Root-owned Docker named
# volumes (created before the image pre-built the mount target) are repaired in
# place via passwordless sudo, mirroring post_start.py's uv cache repair; the
# script only fails with remediation guidance when neither path works.
prepare_config_dir() {
    local dir="$1"
    mkdir -p "${dir}" 2>/dev/null || true
    if ! chmod 700 "${dir}" 2>/dev/null; then
        if sudo -n chown -R "$(id -u):$(id -g)" "${dir}" 2>/dev/null \
            && chmod 700 "${dir}" 2>/dev/null; then
            return 0
        fi
        die "Cannot secure config directory ${dir} (chmod 700 failed). If it is a Docker volume with root ownership, repair it with: sudo chown -R \"\$(id -u):\$(id -g)\" ${dir}"
    fi
}

detect_container_mode() {
    local container_mode="${AI_BACKENDS_CONTAINER_MODE:-auto}"
    case "${container_mode}" in
        auto)
            if [ -f /.dockerenv ] || [ -f /run/.containerenv ]; then
                printf 'yes\n'
            else
                printf 'no\n'
            fi
            ;;
        yes|no) printf '%s\n' "${container_mode}" ;;
        *) die "AI_BACKENDS_CONTAINER_MODE must be auto, yes, or no." ;;
    esac
}

# Effective Claude Code subprocess env-scrub preference. Legacy
# CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB stays honored for the Z.ai launcher.
claude_scrub_value() {
    local scrub="${AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB:-${CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB:-auto}}"
    case "${scrub}" in
        auto)
            if [ "$(detect_container_mode)" = "yes" ]; then
                # The devcontainer is already the isolation boundary. Claude's
                # additional Linux scrub sandbox still requires CLONE_NEWUSER
                # and mount operations that Docker's default profiles deny.
                printf '0\n'
            else
                printf '1\n'
            fi
            ;;
        0|1) printf '%s\n' "${scrub}" ;;
        *) die "AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB must be auto, 0, or 1." ;;
    esac
}

# Settings JSON disabling Claude's weaker nested sandbox inside containers,
# where the outer devcontainer is the primary isolation boundary. Passed to
# claude via --settings: a bare positional would be parsed as the session
# prompt (and historically became the first user message of every session).
claude_sandbox_arg() {
    if [ "$(detect_container_mode)" = "yes" ]; then
        printf '%s' '{"sandbox":{"enabled":false,"enableWeakerNestedSandbox":true}}'
    fi
}

preflight_claude_subprocess_scrub() {
    local scrub="$1"
    if [ "$(uname -s)" = "Linux" ] && [ "${scrub}" = "1" ]; then
        command -v bwrap >/dev/null 2>&1 \
            || die "bubblewrap is required for Claude subprocess isolation; install bubblewrap and socat."
        command -v socat >/dev/null 2>&1 \
            || die "socat is required for Claude sandbox networking; install bubblewrap and socat."
        bwrap --unshare-user --ro-bind / / --bind /proc /proc --dev /dev true \
            || die "Claude subprocess isolation cannot create its nested sandbox. Set AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB=0 (or CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB=0) to rely on the outer devcontainer boundary."
    fi
}

# The image has python3; host shells may only have python, or a Windows
# python3 alias that cannot execute. Resolve once for this launch process.
backend_state_python=""
run_backend_state() {
    local candidate
    if [ -z "${backend_state_python}" ]; then
        for candidate in "${PYTHON:-}" python3 python; do
            if [ -n "${candidate}" ] && command -v "${candidate}" >/dev/null 2>&1 \
                && "${candidate}" -c "" >/dev/null 2>&1; then
                backend_state_python="${candidate}"
                break
            fi
        done
        [ -n "${backend_state_python}" ] || die "A working Python 3 interpreter is required for backend state writes."
    fi
    "${backend_state_python}" "${WORKSPACE_ROOT}/.devcontainer/backend_state.py" "$@"
}

install_codex_profile() {
    local codex_home="${CODEX_HOME:-${HOME}/.codex}"
    prepare_config_dir "${codex_home}"
    run_backend_state profile zai --config-dir "${codex_home}"
}

install_codex_openrouter_profile() {
    local codex_home="${CODEX_HOME:-${HOME}/.codex}"
    prepare_config_dir "${codex_home}"
    run_backend_state profile openrouter --config-dir "${codex_home}"
}

install_launchers() {
    local bin_dir codex_command launcher script_path target
    if [ -n "${AI_BACKENDS_BIN_DIR:-}" ]; then
        bin_dir="${AI_BACKENDS_BIN_DIR}"
    elif case ":${PATH}:" in *":${HOME}/.local/bin:"*) true ;; *) false ;; esac; then
        bin_dir="${HOME}/.local/bin"
    else
        codex_command="$(command -v codex || true)"
        if [ -n "${codex_command}" ] && [ -w "$(dirname "${codex_command}")" ]; then
            bin_dir="$(dirname "${codex_command}")"
        else
            bin_dir="${HOME}/.local/bin"
        fi
    fi
    script_path="$(realpath "${BASH_SOURCE[0]}")"
    mkdir -p "${bin_dir}"

    for launcher in codex-zai claude-zai codex-openrouter claude-openrouter; do
        target="${bin_dir}/${launcher}"
        if [ -e "${target}" ] && [ ! -L "${target}" ]; then
            die "Refusing to replace non-symlink launcher: ${target}"
        fi
        ln -sfn "${script_path}" "${target}"
    done
}

# Seeds folder trust + MCP server approvals into an isolated Claude config
# dir (${config_dir}/.claude.json), mirroring what configure.mjs records for
# ~/.claude.json and what the interactive dialogs write. Without this, every
# project .mcp.json server shows "Pending approval" in claude-zai /
# claude-openrouter because they never read ~/.claude.json.
seed_claude_approvals() {
    local config_dir="$1" mcp_config
    mcp_config="${AI_BACKENDS_MCP_CONFIG:-${WORKSPACE_ROOT}/.mcp.json}"
    [ -f "${mcp_config}" ] || return 0
    prepare_config_dir "${config_dir}" || return 1
    run_backend_state approve \
        --config-dir "${config_dir}" --project "${WORKSPACE_ROOT}" --mcp-config "${mcp_config}"
}

install_backend_support() {
    install_codex_profile
    install_codex_openrouter_profile
    install_launchers
    seed_claude_approvals "${CLAUDE_ZAI_CONFIG_DIR:-${HOME}/.claude-zai}" || true
    seed_claude_approvals "${CLAUDE_OPENROUTER_CONFIG_DIR:-${HOME}/.claude-openrouter}" || true
    printf '[ai-backends] Installed codex-zai, claude-zai, codex-openrouter, and claude-openrouter launchers.\n'
}

launch_codex_zai() {
    local catalog_file codex_home model reasoning_effort zai_key
    command -v codex >/dev/null 2>&1 || die "codex is not installed."
    zai_key="$(resolve_zai_key)"
    export ZAI_API_KEY="${zai_key}"
    # Codex cannot inject static env values into MCP servers — env_vars entries
    # pass through from this process — so set the mode the @z_ai vision server
    # expects, matching the claude/opencode/vscode configs.
    export Z_AI_MODE=ZAI

    codex_home="${CODEX_HOME:-${HOME}/.codex}"
    catalog_file="${codex_home}/${PROFILE_NAME}-models.json"
    if [ ! -f "${codex_home}/${PROFILE_NAME}.config.toml" ] || [ ! -f "${catalog_file}" ]; then
        install_codex_profile
    fi

    model="${CODEX_ZAI_MODEL:-glm-5.3}"
    reasoning_effort="${CODEX_ZAI_REASONING_EFFORT:-max}"
    case "${reasoning_effort}" in
        low|high|max) ;;
        *) die "CODEX_ZAI_REASONING_EFFORT must be low, high, or max." ;;
    esac
    exec codex \
        --profile "${PROFILE_NAME}" \
        --model "${model}" \
        --config 'model_provider="ZAI"' \
        --config "model_reasoning_effort=\"${reasoning_effort}\"" \
        --config "model_catalog_json=\"${catalog_file}\"" \
        "$@"
}

launch_codex_openrouter() {
    local codex_home model or_key reasoning_effort
    command -v codex >/dev/null 2>&1 || die "codex is not installed."
    or_key="$(resolve_openrouter_key)"
    export OPENROUTER_API_KEY="${or_key}"
    unset OPEN_ROUTER_API_KEY

    codex_home="${CODEX_HOME:-${HOME}/.codex}"
    if [ ! -f "${codex_home}/${OPENROUTER_PROFILE_NAME}.config.toml" ]; then
        install_codex_openrouter_profile
    fi

    model="${CODEX_OPENROUTER_MODEL:-~openai/gpt-latest}"
    reasoning_effort="${CODEX_OPENROUTER_REASONING_EFFORT:-high}"
    case "${reasoning_effort}" in
        low|medium|high|xhigh|max) ;;
        *) die "CODEX_OPENROUTER_REASONING_EFFORT must be low, medium, high, xhigh, or max." ;;
    esac
    exec codex \
        --profile "${OPENROUTER_PROFILE_NAME}" \
        --model "${model}" \
        --config 'model_provider="OPENROUTER"' \
        --config "model_reasoning_effort=\"${reasoning_effort}\"" \
        "$@"
}

launch_claude_zai() {
    local config_dir scrub timeout_ms zai_key
    local -a claude_args=()
    command -v claude >/dev/null 2>&1 || die "claude is not installed."
    zai_key="$(resolve_zai_key)"
    config_dir="${CLAUDE_ZAI_CONFIG_DIR:-${HOME}/.claude-zai}"
    timeout_ms="$(resolve_timeout_ms ZAI_API_TIMEOUT_MS 300000)"
    prepare_config_dir "${config_dir}"
    seed_claude_approvals "${config_dir}" || true

    scrub="$(claude_scrub_value)"
    preflight_claude_subprocess_scrub "${scrub}"
    if [ -n "$(claude_sandbox_arg)" ]; then
        claude_args+=("--settings" "$(claude_sandbox_arg)")
    fi

    unset ANTHROPIC_API_KEY \
        ANTHROPIC_MODEL \
        ANTHROPIC_DEFAULT_MODEL \
        ANTHROPIC_DEFAULT_HAIKU_MODEL \
        ANTHROPIC_DEFAULT_SONNET_MODEL \
        ANTHROPIC_DEFAULT_OPUS_MODEL \
        ANTHROPIC_SMALL_FAST_MODEL \
        CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST \
        CLAUDE_CODE_USE_ANTHROPIC_AWS \
        CLAUDE_CODE_USE_BEDROCK \
        CLAUDE_CODE_USE_VERTEX \
        CLAUDE_CODE_USE_FOUNDRY \
        CLAUDE_CODE_USE_MANTLE \
        CLAUDE_CODE_USE_GATEWAY
    export ANTHROPIC_AUTH_TOKEN="${zai_key}"
    # Keep the raw key exported under both spellings: project .mcp.json expands
    # ${ZAI_API_KEY} inside MCP servers (zai-web-search Authorization,
    # zai-vision startup — the @z_ai server exits when it is empty), and
    # @z_ai/mcp-server reads Z_AI_API_KEY first. The same secret already rides
    # along as ANTHROPIC_AUTH_TOKEN, so this adds variable names, not exposure.
    export ZAI_API_KEY="${zai_key}" Z_AI_API_KEY="${zai_key}"
    export ANTHROPIC_BASE_URL="${ZAI_ANTHROPIC_URL}"
    export ANTHROPIC_DEFAULT_HAIKU_MODEL="${CLAUDE_ZAI_HAIKU_MODEL:-glm-5.3-flash[1m]}"
    export ANTHROPIC_DEFAULT_SONNET_MODEL="${CLAUDE_ZAI_SONNET_MODEL:-glm-5.3[1m]}"
    export ANTHROPIC_DEFAULT_OPUS_MODEL="${CLAUDE_ZAI_OPUS_MODEL:-glm-5.3[1m]}"
    export API_TIMEOUT_MS="${timeout_ms}"
    export CLAUDE_CODE_AUTO_COMPACT_WINDOW=1000000
    export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
    export CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST=1
    export CLAUDE_CODE_SUBPROCESS_ENV_SCRUB="${scrub}"
    export CLAUDE_CONFIG_DIR="${config_dir}"
    exec claude ${claude_args[@]+"${claude_args[@]}"} "$@"
}

launch_claude_openrouter() {
    local config_dir or_key scrub timeout_ms
    local -a claude_args=()
    command -v claude >/dev/null 2>&1 || die "claude is not installed."
    or_key="$(resolve_openrouter_key)"
    config_dir="${CLAUDE_OPENROUTER_CONFIG_DIR:-${HOME}/.claude-openrouter}"
    timeout_ms="$(resolve_timeout_ms OPENROUTER_API_TIMEOUT_MS 300000)"
    prepare_config_dir "${config_dir}"
    seed_claude_approvals "${config_dir}" || true

    scrub="$(claude_scrub_value)"
    preflight_claude_subprocess_scrub "${scrub}"
    if [ -n "$(claude_sandbox_arg)" ]; then
        claude_args+=("--settings" "$(claude_sandbox_arg)")
    fi

    unset ANTHROPIC_API_KEY \
        ANTHROPIC_MODEL \
        ANTHROPIC_DEFAULT_MODEL \
        ANTHROPIC_DEFAULT_FABLE_MODEL \
        ANTHROPIC_DEFAULT_HAIKU_MODEL \
        ANTHROPIC_DEFAULT_SONNET_MODEL \
        ANTHROPIC_DEFAULT_OPUS_MODEL \
        ANTHROPIC_SMALL_FAST_MODEL \
        CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST \
        CLAUDE_CODE_USE_ANTHROPIC_AWS \
        CLAUDE_CODE_USE_BEDROCK \
        CLAUDE_CODE_USE_VERTEX \
        CLAUDE_CODE_USE_FOUNDRY \
        CLAUDE_CODE_USE_MANTLE \
        CLAUDE_CODE_USE_GATEWAY
    export ANTHROPIC_BASE_URL="${OPENROUTER_ANTHROPIC_URL}"
    export ANTHROPIC_AUTH_TOKEN="${or_key}"
    # OpenRouter contract: ANTHROPIC_API_KEY must be explicitly empty so Claude
    # Code never falls back to x-api-key auth against Anthropic directly.
    export ANTHROPIC_API_KEY=""
    export ANTHROPIC_DEFAULT_FABLE_MODEL="${CLAUDE_OPENROUTER_FABLE_MODEL:-~anthropic/claude-fable-latest}"
    export ANTHROPIC_DEFAULT_HAIKU_MODEL="${CLAUDE_OPENROUTER_HAIKU_MODEL:-~anthropic/claude-haiku-latest}"
    export ANTHROPIC_DEFAULT_SONNET_MODEL="${CLAUDE_OPENROUTER_SONNET_MODEL:-~anthropic/claude-sonnet-latest}"
    export ANTHROPIC_DEFAULT_OPUS_MODEL="${CLAUDE_OPENROUTER_OPUS_MODEL:-~anthropic/claude-opus-latest}"
    export CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY="${CLAUDE_OPENROUTER_GATEWAY_DISCOVERY:-1}"
    if [ -n "${CLAUDE_OPENROUTER_SUBAGENT_MODEL:-}" ]; then
        export CLAUDE_CODE_SUBAGENT_MODEL="${CLAUDE_OPENROUTER_SUBAGENT_MODEL}"
    fi
    export API_TIMEOUT_MS="${timeout_ms}"
    export CLAUDE_CODE_AUTO_COMPACT_WINDOW=1000000
    export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
    export CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST=1
    export CLAUDE_CODE_SUBPROCESS_ENV_SCRUB="${scrub}"
    export CLAUDE_CONFIG_DIR="${config_dir}"
    unset OPENROUTER_API_KEY OPEN_ROUTER_API_KEY
    exec claude ${claude_args[@]+"${claude_args[@]}"} "$@"
}

print_help() {
    cat <<'HELP'
Usage:
  bash .devcontainer/ai-backends.sh install
  codex-zai [codex arguments...]
  claude-zai [claude arguments...]
  codex-openrouter [codex arguments...]
  claude-openrouter [claude arguments...]

The ordinary `codex` and `claude` commands retain their native backends.

Credentials: every launcher loads a gitignored .env.local for keys that are
not already exported (workspace root beside this script, then the current
directory, or AI_BACKENDS_ENV_FILE when set). Preexisting environment
variables always win, so non-interactive shells work without rc sourcing.
Z.ai keys: ZAI_API_KEY (or Z_AI_API_KEY). OpenRouter keys: OPENROUTER_API_KEY
(or OPEN_ROUTER_API_KEY, sk-or-...). Set them only in your private shell or
secret store; see .env.local.example.

Optional overrides:
  Z.ai:       CODEX_ZAI_MODEL, CODEX_ZAI_REASONING_EFFORT (low|high|max),
              ZAI_API_TIMEOUT_MS, CLAUDE_ZAI_CONFIG_DIR,
              CLAUDE_ZAI_{SONNET,OPUS,HAIKU}_MODEL
  OpenRouter: CODEX_OPENROUTER_MODEL, CODEX_OPENROUTER_REASONING_EFFORT
              (low|medium|high|xhigh|max), OPENROUTER_API_TIMEOUT_MS,
              CLAUDE_OPENROUTER_CONFIG_DIR,
              CLAUDE_OPENROUTER_{FABLE,SONNET,OPUS,HAIKU}_MODEL,
              CLAUDE_OPENROUTER_SUBAGENT_MODEL,
              CLAUDE_OPENROUTER_GATEWAY_DISCOVERY (0|1)
  Shared:     AI_BACKENDS_CONTAINER_MODE (auto|yes|no),
              AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB (auto|0|1; legacy
              CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB stays honored),
              AI_BACKENDS_ENV_FILE (explicit .env.local path)
HELP
}

load_env_local
action="$(resolve_action "${1:-}")"
if [ "$(basename "$0")" != "codex-zai" ] && [ "$(basename "$0")" != "claude-zai" ] \
    && [ "$(basename "$0")" != "codex-openrouter" ] && [ "$(basename "$0")" != "claude-openrouter" ] \
    && [ "$#" -gt 0 ]; then
    shift
fi

case "${action}" in
    install) install_backend_support ;;
    codex-zai) launch_codex_zai "$@" ;;
    claude-zai) launch_claude_zai "$@" ;;
    codex-openrouter) launch_codex_openrouter "$@" ;;
    claude-openrouter) launch_claude_openrouter "$@" ;;
    help|-h|--help) print_help ;;
    *) die "Unknown action: ${action}" ;;
esac
