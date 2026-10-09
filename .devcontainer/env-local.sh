#!/usr/bin/env bash
# BOM/CRLF-tolerant loader for the gitignored .env.local credential store.
#
# Sourced from ~/.bashrc (see on-create.sh / post-start.sh) so every
# interactive shell — and any agent CLI launched from one (opencode, claude,
# codex, nanocoder, vscode) — inherits the MCP credentials without requiring
# the gateway launchers in ai-backends.sh.
#
# Parsing mirrors load_env_local in ai-backends.sh and readEnvLocal in
# .llm/mcp/*.mjs: KEY=VALUE, `export ` prefix, symmetric quotes, blanks and
# comments skipped, UTF-8 BOM and CRLF tolerated.
#
# Semantics: a NON-EMPTY preexisting variable always wins; an EMPTY
# preexisting variable (e.g. a forwarded-but-unset ${localEnv:VAR} from
# devcontainer remoteEnv) is filled in from the file.
#
# Set DATAVIZ_ENV_LOCAL_PRECEDENCE=file to invert that for the MCP credential
# names listed in MCP_CREDENTIAL_KEYS below, so a value rotated in .env.local
# wins over the stale copy already exported into a long-lived session. This is
# how a devcontainer picks up a repaired credential WITHOUT a rebuild: the
# forwarded value lives in process.env for the container's whole lifetime, and
# "non-empty always wins" otherwise pins it forever. See
# .devcontainer/tests/test-env-local-precedence.sh for the contract.
#
# Usage: source this file, then: load_env_local [path-to-.env.local]
load_env_local() {
    # Credential names whose .env.local value may override an already-exported
    # stale copy when DATAVIZ_ENV_LOCAL_PRECEDENCE=file. Deliberately narrow:
    # PATH and every other variable keep "non-empty preexisting wins".
    MCP_CREDENTIAL_KEYS="GITHUB_PERSONAL_ACCESS_TOKEN GITHUB_MCP_PAT GITHUB_TOKEN ZAI_API_KEY Z_AI_API_KEY UNITY_MCP_TOKEN"
    local env_file="${1:-${AI_BACKENDS_ENV_FILE:-}}"
    if [ -n "${env_file}" ]; then
        [ -f "${env_file}" ] || return 0
    elif [ -f "${WORKSPACE_ROOT:-}/.env.local" ]; then
        env_file="${WORKSPACE_ROOT}/.env.local"
    elif [ -f ".env.local" ]; then
        env_file=".env.local"
    else
        return 0
    fi

    local bom=$'\xEF\xBB\xBF'
    local line key value
    while IFS= read -r line || [ -n "${line}" ]; do
        line="${line#"${bom}"}"
        line="${line%$'\r'}"
        line="${line#"${line%%[![:space:]]*}"}"
        line="${line%"${line##*[![:space:]]}"}"
        case "${line}" in
            '' | '#'*) continue ;;
            *=*) ;;
            *) continue ;;
        esac
        key="${line%%=*}"
        value="${line#*=}"
        case "${key}" in
            export\ *) key="${key#export }" ;;
        esac
        key="${key#"${key%%[![:space:]]*}"}"
        key="${key%"${key##*[![:space:]]}"}"
        value="${value#"${value%%[![:space:]]*}"}"
        value="${value%"${value##*[![:space:]]}"}"
        case "${value}" in
            '"'*'"') value="${value#\"}" ; value="${value%\"}" ;;
            "'"*"'") value="${value#\'}" ; value="${value%\'}" ;;
        esac
        case "${key}" in
            '' | *[!A-Za-z0-9_]*) continue ;;
        esac
        # A file value always replaces an EMPTY preexisting variable. It also
        # replaces a NON-EMPTY one when precedence is inverted AND the key is a
        # managed MCP credential — see the header note.
        if [ -n "${!key:-}" ]; then
            overwrite=false
            if [ "${DATAVIZ_ENV_LOCAL_PRECEDENCE:-}" = "file" ]; then
                case " ${MCP_CREDENTIAL_KEYS:-} " in
                    *" ${key} "*) overwrite=true ;;
                esac
            fi
            [ "${overwrite}" = true ] || continue
        fi
        export "${key}=${value}"
    done <"${env_file}"
}
