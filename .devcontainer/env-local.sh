#!/usr/bin/env bash
# Keep parent-shell exports in Bash; parse literal assignments with Python via uv.
# DATAVIZ_ENV_UV can pin uv when the caller uses a restricted PATH.
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

    local loader="${BASH_SOURCE[0]%/*}/env_local.py"
    local key value overwrite complete=false
    local -a assignments=()
    while IFS= read -r -d '' key && IFS= read -r -d '' value && IFS= read -r -d '' overwrite; do
        if [ -z "${key}" ]; then
            complete=true
            break
        fi
        # Preserve non-exported shell values, which the child cannot inherit.
        if [ -n "${!key:-}" ] && [ "${overwrite}" != 1 ]; then
            continue
        fi
        assignments+=("${key}=${value}")
    done < <(DATAVIZ_ENV_LOCAL_PRECEDENCE="${DATAVIZ_ENV_LOCAL_PRECEDENCE:-}" "${DATAVIZ_ENV_UV:-uv}" run --no-project "${loader}" -- "${env_file}")
    if [ "${complete}" != true ]; then
        printf '%s\n' 'Cannot load environment assignments; no exports applied.' >&2
        return 1
    fi
    if [ "${#assignments[@]}" -ne 0 ]; then
        export "${assignments[@]}"
    fi
}
