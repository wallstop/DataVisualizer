#!/usr/bin/env bash
# Recycle the OpenCode background service when its MCP credentials disagree with
# .env.local.
#
# The problem this solves: OpenCode runs a shared background server
# (`opencode serve --service`) that EVERY CLI session attaches to, discovered
# through ~/.local/state/opencode/service.json. That service inherits the
# environment of whichever client happened to start it and keeps it for its
# entire life. So when a credential is repaired in .env.local, new shells and new
# sessions still fail: they are fine, the singleton behind them is not. Killing
# it lets the next client respawn it from the corrected environment, which is
# the only fix that does not require rebuilding the container.
#
# Safety properties, each covered by a contract test:
#   * A service whose credential MATCHES .env.local is never touched.
#   * A record whose pid is gone is removed (clients must not attach to it).
#   * No .env.local means nothing to compare against, so nothing to do.
#   * Credential VALUES are never printed — only variable NAMES.
#
# Usage:
#   bash recycle-stale-opencode-service.sh [--dry-run] [--state-dir DIR]
#                                            [--env-file FILE]
#
# Exit codes: 0 = healthy or recycled, 1 = unexpected error.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

STATE_DIR="${XDG_STATE_HOME:-${HOME}/.local/state}/opencode"
ENV_FILE="${WORKSPACE_ROOT:-$(pwd)}/.env.local"
DRY_RUN=false

while [ "$#" -gt 0 ]; do
    case "$1" in
        --dry-run) DRY_RUN=true; shift ;;
        --state-dir) STATE_DIR="${2:-}"; shift 2 ;;
        --env-file) ENV_FILE="${2:-}"; shift 2 ;;
        -h|--help)
            sed -n '2,25p' "$0"
            exit 0
            ;;
        *) printf 'recycle: unknown argument %s\n' "$1" >&2; exit 1 ;;
    esac
done

RECORD="${STATE_DIR}/service.json"

# Credential names worth comparing. The github pair and the z.ai pair are the
# ones that circulate under two names; the rest are single-name secrets whose
# rotation has the same staleness problem.
CREDENTIAL_KEYS="GITHUB_PERSONAL_ACCESS_TOKEN GITHUB_MCP_PAT GITHUB_TOKEN ZAI_API_KEY Z_AI_API_KEY"

# Reads one KEY=VALUE from an env-style file without executing it.
#
# Delegates to env-local.sh when available so the parsing rules (BOM, CRLF,
# `export ` prefix, symmetric quotes) have exactly one implementation. This
# matters: a hand-rolled parser that disagrees with the loader would compare the
# service against a string the loader never produces and recycle a healthy
# service on every start.
read_env_file_value() {
    local file="$1" key="$2"
    [ -f "${file}" ] || return 1
    local loader="${SCRIPT_DIR}/env-local.sh"
    if [ -f "${loader}" ]; then
        # Runs in a subshell (command substitution) so the unset/load cannot
        # leak into this script's own environment.
        #
        # Two details are load-bearing:
        #  * Each candidate is unset first, so the value returned is the FILE's,
        #     never an inherited ambient value for a key the file omits. Without
        #     this, a credential present only in the process environment reads as
        #     present in the file and a mismatch is invented.
        #  * precedence=file, because the loader's default is "non-empty
        #     preexisting wins" — which would hand back this shell's own
        #     inherited value instead of the file's.
        (
            unset "${key}"
            # shellcheck source=./env-local.sh
            . "${loader}"
            DATAVIZ_ENV_LOCAL_PRECEDENCE=file
            load_env_local "${file}"
            [ -n "${!key-}" ] || exit 1
            printf '%s' "${!key}"
        )
        return $?
    fi
    # Standalone fallback (script copied without its sibling loader).
    local bom=$'\xEF\xBB\xBF' line keyname value
    while IFS= read -r line || [ -n "${line}" ]; do
        line="${line#"${bom}"}"
        line="${line%$'\r'}"
        line="${line#"${line%%[![:space:]]*}"}"
        case "${line}" in
            '' | '#'*) continue ;;
            *=*) ;;
            *) continue ;;
        esac
        keyname="${line%%=*}"
        case "${keyname}" in
            export\ *) keyname="${keyname#export }" ;;
        esac
        keyname="${keyname#"${keyname%%[![:space:]]*}"}"
        keyname="${keyname%"${keyname##*[![:space:]]}"}"
        [ "${keyname}" = "${key}" ] || continue
        value="${line#*=}"
        value="${value#"${value%%[![:space:]]*}"}"
        value="${value%"${value##*[![:space:]]}"}"
        case "${value}" in
            '"'*'"') value="${value#\"}"; value="${value%\"}" ;;
            "'"*"'") value="${value#\'}"; value="${value%\'}" ;;
        esac
        printf '%s' "${value}"
        return 0
    done <"${file}"
    return 1
}

# The service's own environment, or "" when unreadable.
service_env_value() {
    local pid="$1" key="$2"
    [ -r "/proc/${pid}/environ" ] || return 1
    tr '\0' '\n' <"/proc/${pid}/environ" 2>/dev/null |
        grep -m1 -E "^${key}=" |
        cut -d= -f2-
}

pid_alive() {
    kill -0 "$1" 2>/dev/null
}

# Minimal field extraction from the discovery record. Deliberately not a JSON
# parser: the record is written by opencode itself, and jq may be absent on a
# bare container.
json_field() {
    local file="$1" field="$2"
    grep -o "\"${field}\"[[:space:]]*:[[:space:]]*[^,}]*" "${file}" 2>/dev/null |
        head -1 |
        grep -oE '[0-9]+' |
        head -1
}

if [ ! -f "${RECORD}" ]; then
    # No service recorded: nothing to recycle (normal before first use).
    exit 0
fi

SERVICE_PID="$(json_field "${RECORD}" pid)"
if [ -z "${SERVICE_PID}" ]; then
    printf 'recycle: discovery record has no pid; removing it\n'
    [ "${DRY_RUN}" = true ] || rm -f "${RECORD}"
    exit 0
fi

if ! pid_alive "${SERVICE_PID}"; then
    printf 'recycle: service pid %s not running; removing stale discovery record\n' "${SERVICE_PID}"
    [ "${DRY_RUN}" = true ] || rm -f "${RECORD}"
    exit 0
fi

if [ ! -f "${ENV_FILE}" ]; then
    # Nothing to compare against. Never kill on no evidence.
    exit 0
fi

# Compare each credential the service holds against .env.local. Report NAMES
# only; the values themselves are compared, never displayed.
STALE_KEYS=""
for key in ${CREDENTIAL_KEYS}; do
    svc="$(service_env_value "${SERVICE_PID}" "${key}")"
    [ -n "${svc}" ] || continue
    file_val="$(read_env_file_value "${ENV_FILE}" "${key}" || true)"
    [ -n "${file_val}" ] || continue
    if [ "${svc}" != "${file_val}" ]; then
        STALE_KEYS="${STALE_KEYS} ${key}"
    fi
done

if [ -z "${STALE_KEYS}" ]; then
    exit 0
fi

printf 'recycle: opencode service pid %s holds a stale credential for:%s\n' "${SERVICE_PID}" "${STALE_KEYS}"
printf 'recycle: .env.local has newer values; the service will respawn from the current environment\n'

if [ "${DRY_RUN}" = true ]; then
    printf 'recycle: dry run — service left running\n'
    exit 0
fi

kill "${SERVICE_PID}" 2>/dev/null || true
# Give it a moment to exit so a client does not race the record removal.
for _ in 1 2 3 4 5 6 7 8 9 10; do
    pid_alive "${SERVICE_PID}" || break
    sleep 0.2
done
if pid_alive "${SERVICE_PID}"; then
    printf 'recycle: service pid %s did not exit on SIGTERM; sending SIGKILL\n' "${SERVICE_PID}"
    kill -9 "${SERVICE_PID}" 2>/dev/null || true
    sleep 0.3
fi

# The record must go: a client that read it would otherwise attach to a dead pid.
rm -f "${RECORD}"
printf 'recycle: recycled pid %s; next agent session starts a fresh service\n' "${SERVICE_PID}"
exit 0