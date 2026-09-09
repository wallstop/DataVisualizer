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
# Usage: source this file, then: load_env_local [path-to-.env.local]
load_env_local() {
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
        [ -n "${!key:-}" ] && continue
        export "${key}=${value}"
    done <"${env_file}"
}
