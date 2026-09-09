#!/usr/bin/env bash
# Contract tests for .devcontainer/ai-backends.sh.
#
# Run directly:
#   bash .devcontainer/tests/test-ai-backends.sh
# or via the PowerShell wrapper (scripts/tests/test-ai-backends.ps1), which the
# harness self-test runner and CI invoke.
#
# The suite stubs `claude` and `codex` on PATH so no CLI and no network is
# touched, and pins AI_BACKENDS_ENV_FILE to a sandbox file so the real
# .env.local (and any real credentials) are never read.

set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LAUNCHER="${REPO_ROOT}/.devcontainer/ai-backends.sh"
if [ ! -f "${LAUNCHER}" ]; then
    echo "FAIL: launcher not found at ${LAUNCHER}" >&2
    exit 1
fi

PASS=0
FAIL=0
SANDBOX=""
RUN_EXIT=0

pass() {
    PASS=$((PASS + 1))
    printf 'PASS: %s\n' "$1"
}

fail() {
    FAIL=$((FAIL + 1))
    printf 'FAIL: %s\n' "$1"
}

assert_eq() {
    if [ "$2" = "$3" ]; then
        pass "$1"
    else
        fail "$1 (expected: [$2]; actual: [$3])"
    fi
}

assert_exit_zero() {
    assert_eq "$1 (exit 0)" 0 "${RUN_EXIT}"
}

assert_exit_nonzero() {
    if [ "${RUN_EXIT}" -ne 0 ]; then
        pass "$1"
    else
        fail "$1 (expected non-zero exit, got 0)"
    fi
}

assert_stderr_contains() {
    if [ -f "${SANDBOX}/stderr.txt" ] && grep -Fq -- "$2" "${SANDBOX}/stderr.txt"; then
        pass "$1"
    else
        fail "$1 (stderr missing [$2])"
        sed 's/^/    | /' "${SANDBOX}/stderr.txt" 2>/dev/null || true
    fi
}

assert_dump_line() {
    if [ -f "${SANDBOX}/dump.txt" ] && grep -Fxq -- "$2" "${SANDBOX}/dump.txt"; then
        pass "$1"
    else
        fail "$1 (expected exact stub line [$2]; dump:)"
        sed 's/^/    | /' "${SANDBOX}/dump.txt" 2>/dev/null || true
    fi
}

assert_dump_lacks() {
    if [ ! -f "${SANDBOX}/dump.txt" ] || ! grep -Fq -- "$2" "${SANDBOX}/dump.txt"; then
        pass "$1"
    else
        fail "$1 (dump unexpectedly contains [$2])"
    fi
}

assert_file_contains() {
    if [ -f "$2" ] && grep -Fq -- "$3" "$2"; then
        pass "$1"
    else
        fail "$1 (missing [$3] in $2)"
    fi
}

make_sandbox() {
    SANDBOX="$(mktemp -d "${TMPDIR:-/tmp}/ai-backends-test.XXXXXX")"
    mkdir -p "${SANDBOX}/bin" "${SANDBOX}/home" "${SANDBOX}/codex-home" "${SANDBOX}/workdir"
    cp "${REPO_ROOT}/.devcontainer/env-local.sh" "${SANDBOX}/bin/env-local.sh"
    for launcher in codex-zai claude-zai codex-openrouter claude-openrouter; do
        ln -sfn "${LAUNCHER}" "${SANDBOX}/bin/${launcher}"
    done
    cat >"${SANDBOX}/bin/claude" <<'EOF'
#!/usr/bin/env bash
{
    printf 'cmd: %s\n' "$(basename "$0")"
    for a in "$@"; do printf 'arg: %s\n' "$a"; done
    for key in ${AI_BACKENDS_TEST_ENV_KEYS}; do
        if [ -n "${!key+x}" ]; then
            printf 'env: %s=%s\n' "${key}" "${!key}"
        else
            printf 'env-unset: %s\n' "${key}"
        fi
    done
} >"${AI_BACKENDS_TEST_DUMP:?}"
exit 0
EOF
    cp "${SANDBOX}/bin/claude" "${SANDBOX}/bin/codex"
    chmod +x "${SANDBOX}/bin/claude" "${SANDBOX}/bin/codex"
    # Superset of environment keys each stub reports (with unset detection).
    TEST_ENV_KEYS="ANTHROPIC_AUTH_TOKEN ANTHROPIC_BASE_URL ANTHROPIC_API_KEY ANTHROPIC_DEFAULT_SONNET_MODEL ANTHROPIC_DEFAULT_OPUS_MODEL ANTHROPIC_DEFAULT_HAIKU_MODEL ANTHROPIC_DEFAULT_FABLE_MODEL CLAUDE_CONFIG_DIR CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY OPENROUTER_API_KEY ZAI_API_KEY Z_AI_API_KEY Z_AI_MODE"
}

destroy_sandbox() {
    rm -rf "${SANDBOX}"
    SANDBOX=""
}

# Runs the named launcher in a hermetic environment: empty env, stub PATH,
# temp HOME/CODEX_HOME, and AI_BACKENDS_ENV_FILE pinned to the sandbox file.
# Optional KEY=VALUE pairs before the launcher arguments seed process env.
run_launcher() {
    local launcher_name="$1"
    shift
    local extra_env=()
    while [ "$#" -gt 0 ]; do
        case "$1" in
            *=*) extra_env+=("$1"); shift ;;
            *) break ;;
        esac
    done
    (cd "${SANDBOX}/workdir" && \
        env -i \
            PATH="${SANDBOX}/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin" \
            HOME="${SANDBOX}/home" \
            CODEX_HOME="${SANDBOX}/codex-home" \
            AI_BACKENDS_TEST_DUMP="${SANDBOX}/dump.txt" \
            AI_BACKENDS_TEST_ENV_KEYS="${TEST_ENV_KEYS}" \
            AI_BACKENDS_ENV_FILE="${SANDBOX}/env.local" \
            AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB=0 \
            ${extra_env[@]+"${extra_env[@]}"} \
            "${SANDBOX}/bin/${launcher_name}" "$@" \
            >"${SANDBOX}/stdout.txt" 2>"${SANDBOX}/stderr.txt")
    RUN_EXIT=$?
}

write_env_file() {
    printf '%b' "$1" >"${SANDBOX}/env.local"
}

# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

test_claude_zai_uses_env_local_file_key() {
    make_sandbox
    write_env_file 'ZAI_API_KEY=zk-file-key\n'
    run_launcher claude-zai --version
    assert_exit_zero "claude-zai launches with key from .env.local"
    assert_dump_line "claude-zai auth token from .env.local" "env: ANTHROPIC_AUTH_TOKEN=zk-file-key"
    assert_dump_line "claude-zai base URL is Z.ai anthropic endpoint" "env: ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic"
    assert_dump_line "claude-zai keeps ZAI_API_KEY exported for .mcp.json \${ZAI_API_KEY} expansion" "env: ZAI_API_KEY=zk-file-key"
    assert_dump_line "claude-zai mirrors Z_AI_API_KEY alias (@z_ai reads it first)" "env: Z_AI_API_KEY=zk-file-key"
    destroy_sandbox
}

test_claude_zai_env_var_wins_over_file() {
    make_sandbox
    write_env_file 'ZAI_API_KEY=zk-file-key\n'
    run_launcher claude-zai "ZAI_API_KEY=zk-env-key" --version
    assert_dump_line "process env beats .env.local for ZAI key" "env: ANTHROPIC_AUTH_TOKEN=zk-env-key"
    destroy_sandbox
}

test_claude_zai_accepts_variant_file_key() {
    make_sandbox
    write_env_file 'Z_AI_API_KEY=zk-variant-key\n'
    run_launcher claude-zai --version
    assert_dump_line "Z_AI_API_KEY variant in .env.local is honored" "env: ANTHROPIC_AUTH_TOKEN=zk-variant-key"
    destroy_sandbox
}

test_claude_zai_missing_key_fails() {
    make_sandbox
    write_env_file '# nothing here\n'
    run_launcher claude-zai --version
    assert_exit_nonzero "missing ZAI key fails loudly"
    assert_stderr_contains "error mentions ZAI_API_KEY" "ZAI_API_KEY"
    destroy_sandbox
}

test_claude_zai_sandbox_settings_flag() {
    # The container sandbox override must reach claude via --settings; a bare
    # positional is parsed as the session PROMPT (it literally became the first
    # user message of every claude-zai session).
    make_sandbox
    write_env_file 'ZAI_API_KEY=zk-file-key\n'
    run_launcher claude-zai AI_BACKENDS_CONTAINER_MODE=yes --version
    assert_dump_line "sandbox override passed via --settings flag" "arg: --settings"
    assert_dump_line "sandbox settings JSON disables weaker nested sandbox" \
        'arg: {"sandbox":{"enabled":false,"enableWeakerNestedSandbox":true}}'
    destroy_sandbox
}

test_claude_openrouter_uses_env_local_file_key() {
    make_sandbox
    write_env_file 'OPENROUTER_API_KEY=sk-or-file-key\n'
    run_launcher claude-openrouter --version
    assert_exit_zero "claude-openrouter launches with key from .env.local"
    assert_dump_line "claude-openrouter auth token from .env.local" "env: ANTHROPIC_AUTH_TOKEN=sk-or-file-key"
    assert_dump_line "claude-openrouter base URL is OpenRouter anthropic skin" "env: ANTHROPIC_BASE_URL=https://openrouter.ai/api"
    assert_dump_line "claude-openrouter blanks ANTHROPIC_API_KEY per OpenRouter docs" "env: ANTHROPIC_API_KEY="
    assert_dump_line "claude-openrouter uses isolated config dir" "env: CLAUDE_CONFIG_DIR=${SANDBOX}/home/.claude-openrouter"
    assert_dump_line "claude-openrouter scrubs raw key from child env" "env-unset: OPENROUTER_API_KEY"
    assert_dump_line "claude-openrouter enables gateway model discovery" "env: CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY=1"
    assert_dump_line "claude-openrouter default sonnet model" "env: ANTHROPIC_DEFAULT_SONNET_MODEL=~anthropic/claude-sonnet-latest"
    assert_dump_line "claude-openrouter default opus model" "env: ANTHROPIC_DEFAULT_OPUS_MODEL=~anthropic/claude-opus-latest"
    assert_dump_line "claude-openrouter default haiku model" "env: ANTHROPIC_DEFAULT_HAIKU_MODEL=~anthropic/claude-haiku-latest"
    destroy_sandbox
}

test_claude_openrouter_accepts_variant_file_key() {
    make_sandbox
    write_env_file 'OPEN_ROUTER_API_KEY=sk-or-variant-key\n'
    run_launcher claude-openrouter --version
    assert_dump_line "OPEN_ROUTER_API_KEY variant in .env.local is honored" "env: ANTHROPIC_AUTH_TOKEN=sk-or-variant-key"
    destroy_sandbox
}

test_claude_openrouter_env_var_wins_over_file() {
    make_sandbox
    write_env_file 'OPENROUTER_API_KEY=sk-or-file-key\n'
    run_launcher claude-openrouter "OPENROUTER_API_KEY=sk-or-env-key" --version
    assert_dump_line "process env beats .env.local for OpenRouter key" "env: ANTHROPIC_AUTH_TOKEN=sk-or-env-key"
    destroy_sandbox
}

test_claude_openrouter_model_overrides() {
    make_sandbox
    write_env_file 'OPENROUTER_API_KEY=sk-or-file-key\n'
    run_launcher claude-openrouter \
        "CLAUDE_OPENROUTER_SONNET_MODEL=acme/sonnet-x" \
        "CLAUDE_OPENROUTER_OPUS_MODEL=acme/opus-x" \
        "CLAUDE_OPENROUTER_FABLE_MODEL=acme/fable-x"
    assert_dump_line "sonnet model override honored" "env: ANTHROPIC_DEFAULT_SONNET_MODEL=acme/sonnet-x"
    assert_dump_line "opus model override honored" "env: ANTHROPIC_DEFAULT_OPUS_MODEL=acme/opus-x"
    assert_dump_line "fable model override honored" "env: ANTHROPIC_DEFAULT_FABLE_MODEL=acme/fable-x"
    destroy_sandbox
}

test_claude_openrouter_missing_key_fails() {
    make_sandbox
    write_env_file '# nothing here\n'
    run_launcher claude-openrouter --version
    assert_exit_nonzero "missing OpenRouter key fails loudly"
    assert_stderr_contains "error mentions OPENROUTER_API_KEY" "OPENROUTER_API_KEY"
    destroy_sandbox
}

test_codex_openrouter_launch() {
    make_sandbox
    write_env_file 'OPENROUTER_API_KEY=sk-or-file-key\n'
    run_launcher codex-openrouter --version
    assert_exit_zero "codex-openrouter launches with key from .env.local"
    assert_dump_line "codex-openrouter selects devcontainer-openrouter profile" "arg: devcontainer-openrouter"
    assert_dump_line "codex-openrouter default model" "arg: ~openai/gpt-latest"
    assert_dump_line "codex-openrouter provider override" 'arg: model_provider="OPENROUTER"'
    assert_dump_line "codex-openrouter exports canonical OPENROUTER_API_KEY" "env: OPENROUTER_API_KEY=sk-or-file-key"
    local profile_file="${SANDBOX}/codex-home/devcontainer-openrouter.config.toml"
    assert_file_contains "codex-openrouter profile exists" "${profile_file}" 'base_url = "https://openrouter.ai/api/v1"'
    assert_file_contains "codex-openrouter profile uses command auth" "${profile_file}" 'command = "sh"'
    assert_file_contains "codex-openrouter auth echoes OPENROUTER_API_KEY" "${profile_file}" 'echo $OPENROUTER_API_KEY'
    destroy_sandbox
}

test_codex_openrouter_reasoning_effort_validation() {
    make_sandbox
    write_env_file 'OPENROUTER_API_KEY=sk-or-file-key\n'
    run_launcher codex-openrouter "CODEX_OPENROUTER_REASONING_EFFORT=bogus" --version
    assert_exit_nonzero "invalid reasoning effort rejected"
    run_launcher codex-openrouter "CODEX_OPENROUTER_REASONING_EFFORT=xhigh" --version
    assert_dump_line "valid reasoning effort forwarded" 'arg: model_reasoning_effort="xhigh"'
    destroy_sandbox
}

test_codex_openrouter_missing_key_fails() {
    make_sandbox
    write_env_file '# nothing here\n'
    run_launcher codex-openrouter --version
    assert_exit_nonzero "missing OpenRouter key fails for codex too"
    destroy_sandbox
}

test_codex_zai_regression() {
    make_sandbox
    write_env_file 'ZAI_API_KEY=zk-file-key\n'
    run_launcher codex-zai --version
    assert_exit_zero "codex-zai launches with key from .env.local"
    assert_dump_line "codex-zai still exports ZAI_API_KEY from .env.local" "env: ZAI_API_KEY=zk-file-key"
    assert_dump_line "codex-zai exports Z_AI_MODE=ZAI for MCP env passthrough" "env: Z_AI_MODE=ZAI"
    assert_dump_line "codex-zai still selects devcontainer-zai profile" "arg: devcontainer-zai"
    assert_file_contains "codex-zai profile uses env_key auth" \
        "${SANDBOX}/codex-home/devcontainer-zai.config.toml" 'env_key = "ZAI_API_KEY"'
    destroy_sandbox
}

test_env_file_does_not_clobber_existing_env() {
    make_sandbox
    write_env_file 'PATH=/evil\nZAI_API_KEY=zk-file-key\n'
    run_launcher claude-zai --version
    assert_dump_lacks ".env.local never overrides preexisting PATH" "/evil"
    assert_dump_line "other .env.local keys still load" "env: ANTHROPIC_AUTH_TOKEN=zk-file-key"
    destroy_sandbox
}

test_env_file_with_bom_and_crlf_is_parsed() {
    # Windows-edited .env.local files commonly carry a UTF-8 BOM and CRLF
    # endings; bash `source` chokes on the BOM, so the loader must not.
    make_sandbox
    printf '\xEF\xBB\xBF# Machine-local credentials (gitignored).\r\nZAI_API_KEY=zk-bom-key\r\n' \
        >"${SANDBOX}/env.local"
    run_launcher claude-zai --version
    assert_dump_line "BOM + CRLF comment-first file loads keys" "env: ANTHROPIC_AUTH_TOKEN=zk-bom-key"
    destroy_sandbox
    make_sandbox
    printf '\xEF\xBB\xBFZAI_API_KEY=zk-bom-first-key\r\n' >"${SANDBOX}/env.local"
    run_launcher claude-zai --version
    assert_dump_line "BOM-prefixed first variable line loads" "env: ANTHROPIC_AUTH_TOKEN=zk-bom-first-key"
    destroy_sandbox
}

test_config_dir_root_owned_is_self_healed_with_sudo() {
    # Root-owned config dirs (Docker named volumes initialized before the image
    # pre-created the mount target) must be repaired in place when passwordless
    # sudo is available, not left failing. Needs sudo to stage the state.
    if [ "$(id -u)" = "0" ] || ! sudo -n true >/dev/null 2>&1; then
        pass "config dir self-heal test skipped (sudo unavailable or running as root)"
        return 0
    fi
    make_sandbox
    write_env_file 'OPENROUTER_API_KEY=sk-or-file-key\n'
    sudo mkdir -p "${SANDBOX}/home/.claude-openrouter"
    sudo chown root:root "${SANDBOX}/home/.claude-openrouter"
    run_launcher claude-openrouter --version
    assert_exit_zero "root-owned config dir is repaired via passwordless sudo"
    if [ "$(uname -s)" = "Linux" ]; then
        assert_eq \
            "repaired config dir is owned by the invoking user" \
            "$(id -u)" \
            "$(stat -c %u "${SANDBOX}/home/.claude-openrouter")"
    fi
    sudo rm -rf "${SANDBOX}/home/.claude-openrouter"
    destroy_sandbox
}

test_config_dir_failure_is_actionable() {
    # With neither a writable dir nor a working sudo/chmod, the launcher must
    # still fail fast with the chown remediation. Stub both on the sandbox PATH
    # (which precedes /usr/bin) to force the unhealable path deterministically.
    make_sandbox
    write_env_file 'OPENROUTER_API_KEY=sk-or-file-key\n'
    printf '#!/usr/bin/env sh\nexit 1\n' >"${SANDBOX}/bin/chmod"
    printf '#!/usr/bin/env sh\nexit 1\n' >"${SANDBOX}/bin/sudo"
    chmod +x "${SANDBOX}/bin/chmod" "${SANDBOX}/bin/sudo"
    mkdir -p "${SANDBOX}/home/.claude-openrouter"
    run_launcher claude-openrouter --version
    assert_exit_nonzero "unhealable config dir fails fast"
    assert_stderr_contains "error includes chown remediation" "chown"
    destroy_sandbox
}

test_install_creates_all_launchers() {
    make_sandbox
    mkdir -p "${SANDBOX}/install-bin"
    (cd "${SANDBOX}/workdir" && \
        env -i \
            PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin" \
            HOME="${SANDBOX}/home" \
            CODEX_HOME="${SANDBOX}/codex-home" \
            AI_BACKENDS_BIN_DIR="${SANDBOX}/install-bin" \
            AI_BACKENDS_ENV_FILE="${SANDBOX}/env.local" \
            bash "${LAUNCHER}" install \
            >"${SANDBOX}/stdout.txt" 2>"${SANDBOX}/stderr.txt")
    RUN_EXIT=$?
    assert_eq "install exits zero" 0 "${RUN_EXIT}"
    for launcher in codex-zai claude-zai codex-openrouter claude-openrouter; do
        if [ -e "${SANDBOX}/install-bin/${launcher}" ]; then
            if [ "$(uname -s)" != "Linux" ] || [ -L "${SANDBOX}/install-bin/${launcher}" ]; then
                pass "install created ${launcher} launcher"
            else
                fail "install created ${launcher} but not as a symlink"
            fi
        else
            fail "install did not create ${launcher} launcher"
        fi
    done
    destroy_sandbox
}

test_install_seeds_isolated_claude_approvals() {
    # Isolated Claude config dirs (claude-zai / claude-openrouter) read MCP
    # approvals and folder trust from their own .claude.json. `install` must
    # seed them, or every project MCP server shows "Pending approval" there.
    make_sandbox
    mkdir -p "${SANDBOX}/install-bin"
    printf '{"mcpServers":{"unity":{}}}\n' >"${SANDBOX}/.mcp.json"
    local node_path node_dir test_path
    node_path="$(command -v node || true)"
    node_dir=""
    if [ -n "${node_path}" ]; then
        node_dir="$(dirname "${node_path}")"
    fi
    test_path="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
    if [ -n "${node_dir}" ]; then
        test_path="${test_path}:${node_dir}"
    fi
    (cd "${SANDBOX}/workdir" && \
        env -i \
            PATH="${test_path}" \
            HOME="${SANDBOX}/home" \
            CODEX_HOME="${SANDBOX}/codex-home" \
            AI_BACKENDS_BIN_DIR="${SANDBOX}/install-bin" \
            AI_BACKENDS_ENV_FILE="${SANDBOX}/env.local" \
            AI_BACKENDS_MCP_CONFIG="${SANDBOX}/.mcp.json" \
            bash "${LAUNCHER}" install \
            >"${SANDBOX}/stdout.txt" 2>"${SANDBOX}/stderr.txt")
    RUN_EXIT=$?
    assert_eq "install exits zero with approval seeding" 0 "${RUN_EXIT}"
    for config_dir_name in .claude-zai .claude-openrouter; do
        local state_file="${SANDBOX}/home/${config_dir_name}/.claude.json"
        assert_file_contains \
            "install seeds trust for ${config_dir_name}" \
            "${state_file}" '"hasTrustDialogAccepted": true'
        assert_file_contains \
            "install seeds managed server approvals for ${config_dir_name}" \
            "${state_file}" '"unity"'
    done
    destroy_sandbox
}

# ---------------------------------------------------------------------------
# Runner
# ---------------------------------------------------------------------------

for test_fn in \
    test_claude_zai_uses_env_local_file_key \
    test_claude_zai_env_var_wins_over_file \
    test_claude_zai_accepts_variant_file_key \
    test_claude_zai_missing_key_fails \
    test_claude_zai_sandbox_settings_flag \
    test_claude_openrouter_uses_env_local_file_key \
    test_claude_openrouter_accepts_variant_file_key \
    test_claude_openrouter_env_var_wins_over_file \
    test_claude_openrouter_model_overrides \
    test_claude_openrouter_missing_key_fails \
    test_codex_openrouter_launch \
    test_codex_openrouter_reasoning_effort_validation \
    test_codex_openrouter_missing_key_fails \
    test_codex_zai_regression \
    test_env_file_does_not_clobber_existing_env \
    test_env_file_with_bom_and_crlf_is_parsed \
    test_config_dir_root_owned_is_self_healed_with_sudo \
    test_config_dir_failure_is_actionable \
    test_install_creates_all_launchers \
    test_install_seeds_isolated_claude_approvals; do
    "${test_fn}"
done

printf '\nai-backends contract tests: %d passed, %d failed.\n' "${PASS}" "${FAIL}"
if [ "${FAIL}" -gt 0 ]; then
    exit 1
fi
exit 0
