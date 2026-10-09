#!/usr/bin/env bash
# Contract tests for .devcontainer/recycle-stale-opencode-service.sh
#
# Run directly:
#   bash .devcontainer/tests/test-opencode-service-recycle.sh
#
# Background: OpenCode runs a shared background server (`opencode.exe serve
# --service`) that every CLI session attaches to via a discovery record in
# ~/.local/state/opencode/service.json. The service inherits the environment of
# whichever client started it and holds that environment for its whole life, so
# a credential repaired in .env.local left every NEW session still broken: the
# session itself was fine, the singleton behind it was not. Recycle-and-restart
# is the only fix short of a container rebuild.
#
# Every test uses synthetic placeholder values in throwaway state dirs and never
# reads the real .env.local or service.json.

set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT="${REPO_ROOT}/.devcontainer/recycle-stale-opencode-service.sh"

PASS=0
FAIL=0
SANDBOX=""

# Synthetic, obviously-fake placeholders. NOT credentials.
STALE="ghp_0000000000000000000000000000000000"
LIVE="ghp_1111111111111111111111111111111111"

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

assert_contains() {
    case "$3" in
        *"$2"*) pass "$1" ;;
        *) fail "$1 (expected to contain: [$2]; actual: [$3])" ;;
    esac
}

assert_not_contains() {
    case "$3" in
        *"$2"*) fail "$1 (expected NOT to contain: [$2]; actual: [$3])" ;;
        *) pass "$1" ;;
    esac
}

make_sandbox() {
    SANDBOX="$(mktemp -d "${TMPDIR:-/tmp}/oc-recycle-test.XXXXXX")"
    mkdir -p "${SANDBOX}/state/opencode" "${SANDBOX}/home"
}

destroy_sandbox() {
    rm -rf "${SANDBOX}"
    SANDBOX=""
}

# Stands in for the long-lived service: a real background process whose
# /proc/<pid>/environ carries a chosen credential value. This is what the
# recycle script must compare against .env.local.
SERVICE_PID=""
start_fake_service() {
    local service_env_value="$1"
    # Every other credential name is explicitly cleared: the test must measure
    # the fixture, not whatever the developer happens to have exported.
    env -u ZAI_API_KEY -u Z_AI_API_KEY -u GITHUB_TOKEN \
        GITHUB_PERSONAL_ACCESS_TOKEN="${service_env_value}" \
        GITHUB_MCP_PAT="${service_env_value}" \
        sleep 300 &
    SERVICE_PID=$!
}

stop_fake_service() {
    [ -n "${SERVICE_PID}" ] && kill "${SERVICE_PID}" 2>/dev/null
    wait "${SERVICE_PID}" 2>/dev/null
    SERVICE_PID=""
}

# Writes a discovery record pointing at SERVICE_PID.
write_service_record() {
    cat >"${SANDBOX}/state/opencode/service.json" <<JSON
{
  "id": "00000000-0000-0000-0000-000000000000",
  "version": "2.0.26",
  "url": "http://127.0.0.1:49374",
  "pid": ${SERVICE_PID}
}
JSON
}

# --state-dir is the opencode state directory itself (the parent of
# service.json), matching the script's default of $XDG_STATE_HOME/opencode.
run_recycle() {
    bash "${SCRIPT}" \
        --state-dir "${SANDBOX}/state/opencode" \
        --env-file "${SANDBOX}/.env.local" \
        "$@" 2>&1
}

# ---------------------------------------------------------------------------
# Detection: a service holding a credential that disagrees with .env.local
# ---------------------------------------------------------------------------

test_recycles_service_with_stale_credential() {
    # The exact incident: .env.local holds the repaired value, the running
    # service still holds the old one. New sessions keep attaching to the stale
    # singleton, so the service must be replaced.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\n' "${LIVE}" >"${SANDBOX}/.env.local"
    start_fake_service "${STALE}"
    write_service_record
    OUT="$(run_recycle)"
    assert_contains "reports the stale credential" "stale credential" "${OUT}"
    assert_contains "names the service pid it recycled" "${SERVICE_PID}" "${OUT}"
    if kill -0 "${SERVICE_PID}" 2>/dev/null; then
        fail "stale service process was not terminated"
    else
        pass "stale service process is terminated"
    fi
    stop_fake_service
    destroy_sandbox
}

test_removes_stale_discovery_record() {
    # The record is how clients find the singleton. Leaving it behind would let
    # a client attach to a pid that is gone.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\n' "${LIVE}" >"${SANDBOX}/.env.local"
    start_fake_service "${STALE}"
    write_service_record
    run_recycle >/dev/null
    if [ -f "${SANDBOX}/state/opencode/service.json" ]; then
        fail "stale discovery record was not removed"
    else
        pass "stale discovery record is removed"
    fi
    stop_fake_service
    destroy_sandbox
}

# ---------------------------------------------------------------------------
# Safety: never recycle a healthy service
# ---------------------------------------------------------------------------

test_leaves_matching_service_alone() {
    # The common case must cost nothing — no kill, no record churn.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\n' "${LIVE}" >"${SANDBOX}/.env.local"
    start_fake_service "${LIVE}"
    write_service_record
    OUT="$(run_recycle)"
    if kill -0 "${SERVICE_PID}" 2>/dev/null; then
        pass "matching service is left running"
    else
        fail "matching service was killed"
    fi
    if [ -f "${SANDBOX}/state/opencode/service.json" ]; then
        pass "matching discovery record is preserved"
    else
        fail "matching discovery record was removed"
    fi
    assert_not_contains "no recycle is reported when healthy" "stale credential" "${OUT}"
    stop_fake_service
    destroy_sandbox
}

test_dry_run_reports_without_killing() {
    # Reports must be verifiable before acting on them.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\n' "${LIVE}" >"${SANDBOX}/.env.local"
    start_fake_service "${STALE}"
    write_service_record
    OUT="$(run_recycle --dry-run)"
    if kill -0 "${SERVICE_PID}" 2>/dev/null; then
        pass "dry-run leaves the service running"
    else
        fail "dry-run killed the service"
    fi
    if [ -f "${SANDBOX}/state/opencode/service.json" ]; then
        pass "dry-run preserves the discovery record"
    else
        fail "dry-run removed the discovery record"
    fi
    assert_contains "dry-run still reports the finding" "stale credential" "${OUT}"
    stop_fake_service
    destroy_sandbox
}

test_missing_service_record_is_a_no_op() {
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\n' "${LIVE}" >"${SANDBOX}/.env.local"
    run_recycle
    assert_eq "missing discovery record exits zero" 0 "$?"
    destroy_sandbox
}

test_dead_pid_is_cleaned_up() {
    # A record whose pid is gone cannot be recycled, but it must not block
    # startup or be treated as a healthy service.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\n' "${LIVE}" >"${SANDBOX}/.env.local"
    cat >"${SANDBOX}/state/opencode/service.json" <<'JSON'
{ "id": "dead", "version": "2.0.26", "url": "http://127.0.0.1:1", "pid": 999999 }
JSON
    OUT="$(run_recycle)"
    assert_eq "record for a dead pid exits zero" 0 "$?"
    assert_contains "record for a dead pid is reported as stale" "not running" "${OUT}"
    if [ -f "${SANDBOX}/state/opencode/service.json" ]; then
        fail "record for a dead pid was not removed"
    else
        pass "record for a dead pid is removed"
    fi
    destroy_sandbox
}

test_no_env_file_is_a_no_op() {
    # With no .env.local there is nothing to compare against, so nothing to do.
    make_sandbox
    start_fake_service "${LIVE}"
    write_service_record
    OUT="$(run_recycle)"
    if kill -0 "${SERVICE_PID}" 2>/dev/null; then
        pass "missing .env.local leaves the service alone"
    else
        fail "missing .env.local caused a recycle"
    fi
    stop_fake_service
    destroy_sandbox
}

test_quoted_and_crlf_env_file_is_parsed_like_the_loader() {
    # Regression guard. The recycle script originally hand-rolled its own
    # .env.local parser and disagreed with env-local.sh, so it read a different
    # string than the loader produced and reported a HEALTHY service as stale —
    # recycling it on every container start. The script now delegates parsing to
    # the loader; this test pins that equivalence for the formats real files use.
    #
    # Symptom of the old bug: reading the value returned a 49-char string whose
    # tail was the SHA of the expected token, proving the key match leaked.
    make_sandbox
    printf '\xEF\xBB\xBFGITHUB_PERSONAL_ACCESS_TOKEN="%s"\r\n' "${LIVE}" >"${SANDBOX}/.env.local"
    start_fake_service "${LIVE}"
    write_service_record
    OUT="$(run_recycle)"
    assert_not_contains \
        "quoted+CRLF+BOM env file is not misread as a mismatch" \
        "stale credential" "${OUT}"
    if kill -0 "${SERVICE_PID}" 2>/dev/null; then
        pass "healthy service survives a quoted/CRLF/BOM env file"
    else
        fail "healthy service was recycled because of a parsing mismatch"
    fi
    stop_fake_service
    destroy_sandbox
}

test_inherited_env_is_not_mistaken_for_a_file_value() {
    # Regression guard for the subshell/unset fix. When the caller runs the
    # script with a credential exported in its own environment, a key the
    # .env.local does NOT define must not read as "present in the file" —
    # otherwise a mismatch is invented against the caller's own value and a
    # healthy service is recycled on every start.
    make_sandbox
    # File defines GITHUB_PERSONAL_ACCESS_TOKEN only; GITHUB_MCP_PAT is absent.
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\n' "${LIVE}" >"${SANDBOX}/.env.local"
    start_fake_service "${LIVE}"
    write_service_record
    OUT="$(GITHUB_MCP_PAT="${STALE}" run_recycle)"
    assert_not_contains \
        "an inherited value for a key absent from .env.local is ignored" \
        "stale credential" "${OUT}"
    if kill -0 "${SERVICE_PID}" 2>/dev/null; then
        pass "healthy service survives an inherited out-of-file credential"
    else
        fail "healthy service was recycled by an inherited out-of-file credential"
    fi
    stop_fake_service
    destroy_sandbox
}

test_never_prints_credential_values() {
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${LIVE}" "${LIVE}" \
        >"${SANDBOX}/.env.local"
    start_fake_service "${STALE}"
    write_service_record
    OUT="$(run_recycle)"
    assert_not_contains "output omits the file credential" "${LIVE}" "${OUT}"
    assert_not_contains "output omits the service credential" "${STALE}" "${OUT}"
    stop_fake_service
    destroy_sandbox
}

# ---------------------------------------------------------------------------
# Runner
# ---------------------------------------------------------------------------

if [ ! -f "${SCRIPT}" ]; then
    echo "FAIL: recycle script not found at ${SCRIPT}" >&2
    exit 1
fi

for test_fn in \
    test_recycles_service_with_stale_credential \
    test_removes_stale_discovery_record \
    test_leaves_matching_service_alone \
    test_dry_run_reports_without_killing \
    test_missing_service_record_is_a_no_op \
    test_dead_pid_is_cleaned_up \
    test_no_env_file_is_a_no_op \
    test_quoted_and_crlf_env_file_is_parsed_like_the_loader \
    test_inherited_env_is_not_mistaken_for_a_file_value \
    test_never_prints_credential_values; do
    "${test_fn}"
done

printf '\nopencode-service-recycle tests: %d passed, %d failed.\n' "${PASS}" "${FAIL}"
if [ "${FAIL}" -gt 0 ]; then
    exit 1
fi
exit 0