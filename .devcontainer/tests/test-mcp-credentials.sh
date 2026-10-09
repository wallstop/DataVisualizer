#!/usr/bin/env bash
# Contract tests for the GitHub/Z.ai credential aliasing in
# .llm/mcp/configure.mjs and .llm/mcp/mcp-doctor.mjs.
#
# Run directly:
#   bash .devcontainer/tests/test-mcp-credentials.sh
#
# These tests exist because of a real 401 incident: .env.local ended up with
# BOTH GITHUB_PERSONAL_ACCESS_TOKEN (stale/revoked) and GITHUB_MCP_PAT (valid),
# every generated config referenced the stale name, and both configure.mjs and
# mcp-doctor.mjs reported success because they only ever checked *presence*.
#
# Everything runs in a throwaway workspace with throwaway HOME and synthetic
# placeholder credentials. No real secret is read, written, or printed.

set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SYNC="${REPO_ROOT}/.llm/mcp/sync-mcp.sh"
DOCTOR="${REPO_ROOT}/.llm/mcp/mcp-doctor.mjs"

PASS=0
FAIL=0
SANDBOX=""

# Synthetic, obviously-fake placeholders. These are NOT credentials.
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

# Case-insensitive: configure.mjs prints uppercase status lines ("DIVERGENT")
# while the doctor emits prose ("divergent"), and neither casing is contractual.
assert_contains() {
    case "$(printf '%s' "$3" | tr '[:upper:]' '[:lower:]')" in
        *"$(printf '%s' "$2" | tr '[:upper:]' '[:lower:]')"*) pass "$1" ;;
        *) fail "$1 (expected to contain: [$2]; actual: [$3])" ;;
    esac
}

assert_not_contains() {
    case "$3" in
        *"$2"*) fail "$1 (expected NOT to contain: [$2]; actual: [$3])" ;;
        *) pass "$1" ;;
    esac
}

# ---------------------------------------------------------------------------
# --repair writes only after a probe proves which name is live
# ---------------------------------------------------------------------------

# A stub endpoint that accepts exactly one bearer token and 401s everything else.
# This lets the repair path be proven deterministically: which name is live, and
# therefore which value must be written, is decided by the stub, not by luck.
STUB_PID=""
start_stub() {
    local accept_token="$1"
    node -e '
        const accept = process.argv[1];
        const http = require("http");
        http.createServer((req, res) => {
            const auth = req.headers.authorization || "";
            if (auth === "Bearer " + accept) {
                res.writeHead(200, { "content-type": "application/json" });
                res.end(JSON.stringify({ jsonrpc: "2.0", id: 1, result: { serverInfo: { name: "stub" } } }));
            } else {
                res.writeHead(401, { "content-type": "application/json" });
                res.end(JSON.stringify({ error: "unauthorized" }));
            }
        }).listen(0, "127.0.0.1", function () {
            process.stdout.write(String(this.address().port) + "\n");
        });
    ' "${accept_token}" >"${SANDBOX}/stub.port" 2>/dev/null &
    STUB_PID=$!
    local i=0
    while [ ! -s "${SANDBOX}/stub.port" ] && [ "${i}" -lt 50 ]; do
        i=$((i + 1))
        sleep 0.1
    done
    STUB_PORT="$(cat "${SANDBOX}/stub.port" 2>/dev/null)"
}

stop_stub() {
    [ -n "${STUB_PID}" ] && kill "${STUB_PID}" 2>/dev/null
    STUB_PID=""
    STUB_PORT=""
}

test_repair_reconciles_divergent_github_pair() {
    # GITHUB_MCP_PAT holds a token the endpoint accepts; the canonical name holds
    # a rejected one. --repair must copy the live value over the stale key.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${STALE}" "${LIVE}" \
        >"${SANDBOX}/.env.local"
    start_stub "${LIVE}"
    if [ -z "${STUB_PORT}" ]; then
        fail "stub endpoint failed to start"
        destroy_sandbox
        return
    fi
    OUT="$(DATAVIZ_MCP_GITHUB_PROBE_URL="http://127.0.0.1:${STUB_PORT}/mcp/" \
        DATAVIZ_MCP_ZAI_PROBE_URL="http://127.0.0.1:${STUB_PORT}/mcp/" \
        DATAVIZ_MCP_PROBE_TIMEOUT_MS=5000 \
        run_isolated "${DOCTOR}" --repair 2>&1)"
    stop_stub
    assert_eq \
        "repair copies the probe-proven live value over the stale canonical key" \
        "${LIVE}" \
        "$(grep -E '^GITHUB_PERSONAL_ACCESS_TOKEN=' "${SANDBOX}/.env.local" | cut -d= -f2-)"
    assert_contains "repair reports which name was live" "GITHUB_MCP_PAT authenticates" "${OUT}"
    assert_contains "repair names the key it rewrote" "Repairing .env.local" "${OUT}"
    destroy_sandbox
}

test_repair_refuses_when_neither_credential_authenticates() {
    # Neither name works: the correct action is to change nothing and say so.
    # Guessing here is how a stale value gets blessed as "fixed".
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${STALE}" "${LIVE}" \
        >"${SANDBOX}/.env.local"
    start_stub "ghp_this-token-is-neither-of-them"
    if [ -z "${STUB_PORT}" ]; then
        fail "stub endpoint failed to start"
        destroy_sandbox
        return
    fi
    OUT="$(DATAVIZ_MCP_GITHUB_PROBE_URL="http://127.0.0.1:${STUB_PORT}/mcp/" \
        DATAVIZ_MCP_ZAI_PROBE_URL="http://127.0.0.1:${STUB_PORT}/mcp/" \
        DATAVIZ_MCP_PROBE_TIMEOUT_MS=5000 \
        run_isolated "${DOCTOR}" --repair 2>&1)"
    stop_stub
    assert_eq \
        "unprovable repair leaves the canonical key untouched" \
        "${STALE}" \
        "$(grep -E '^GITHUB_PERSONAL_ACCESS_TOKEN=' "${SANDBOX}/.env.local" | cut -d= -f2-)"
    assert_contains "unprovable repair says neither name authenticates" "neither name authenticates" "${OUT}"
    destroy_sandbox
}

test_repair_respects_reverse_divergence() {
    # The live credential may be the CANONICAL name. Repair must then repair the
    # alternate, i.e. it must not be hardcoded to always bless GITHUB_MCP_PAT.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${LIVE}" "${STALE}" \
        >"${SANDBOX}/.env.local"
    start_stub "${LIVE}"
    if [ -z "${STUB_PORT}" ]; then
        fail "stub endpoint failed to start"
        destroy_sandbox
        return
    fi
    OUT="$(DATAVIZ_MCP_GITHUB_PROBE_URL="http://127.0.0.1:${STUB_PORT}/mcp/" \
        DATAVIZ_MCP_ZAI_PROBE_URL="http://127.0.0.1:${STUB_PORT}/mcp/" \
        DATAVIZ_MCP_PROBE_TIMEOUT_MS=5000 \
        run_isolated "${DOCTOR}" --repair 2>&1)"
    stop_stub
    assert_eq \
        "repair copies the canonical value over a stale GITHUB_MCP_PAT" \
        "${LIVE}" \
        "$(grep -E '^GITHUB_MCP_PAT=' "${SANDBOX}/.env.local" | cut -d= -f2-)"
    destroy_sandbox
}

test_repair_leaves_matching_pair_untouched() {
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${LIVE}" "${LIVE}" \
        >"${SANDBOX}/.env.local"
    BEFORE="$(cat "${SANDBOX}/.env.local")"
    run_isolated "${DOCTOR}" --repair >/dev/null 2>&1
    assert_eq "repair is a no-op when names already agree" "${BEFORE}" "$(cat "${SANDBOX}/.env.local")"
    destroy_sandbox
}

test_repair_never_prints_secret_values() {
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\nZAI_API_KEY=%s\nZ_AI_API_KEY=%s\n' \
        "${STALE}" "${LIVE}" "zai-a-placeholder" "zai-b-placeholder" >"${SANDBOX}/.env.local"
    OUT="$(run_isolated "${DOCTOR}" --repair 2>&1)"
    assert_not_contains "repair output omits stale token" "${STALE}" "${OUT}"
    assert_not_contains "repair output omits live token" "${LIVE}" "${OUT}"
    assert_not_contains "repair output omits zai primary" "zai-a-placeholder" "${OUT}"
    assert_not_contains "repair output omits zai alternate" "zai-b-placeholder" "${OUT}"
    destroy_sandbox
}

test_repair_preserves_bom_and_line_endings() {
    # A repair rewrites the file, so it must be byte-faithful apart from the one
    # value: a dropped UTF-8 BOM or a mangled trailing newline is an invisible
    # change to a gitignored credential store. The loaders are BOM-tolerant, but
    # other tools reading .env.local may not be.
    make_sandbox
    printf '\xEF\xBB\xBFGITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${STALE}" "${LIVE}" \
        >"${SANDBOX}/.env.local"
    start_stub "${LIVE}"
    if [ -z "${STUB_PORT}" ]; then
        fail "stub endpoint failed to start"
        destroy_sandbox
        return
    fi
    DATAVIZ_MCP_GITHUB_PROBE_URL="http://127.0.0.1:${STUB_PORT}/mcp/" \
        DATAVIZ_MCP_ZAI_PROBE_URL="http://127.0.0.1:${STUB_PORT}/mcp/" \
        DATAVIZ_MCP_PROBE_TIMEOUT_MS=5000 \
        run_isolated "${DOCTOR}" --repair >/dev/null 2>&1
    stop_stub
    local bom
    bom="$(head -c 3 "${SANDBOX}/.env.local" | od -An -tx1 | tr -d ' \n')"
    assert_eq "repair preserves the UTF-8 BOM" "efbbbf" "${bom}"
    assert_eq \
        "repair preserves the trailing newline" \
        "0a" \
        "$(tail -c 1 "${SANDBOX}/.env.local" | od -An -tx1 | tr -d ' \n')"
    destroy_sandbox
}

test_repair_does_not_duplicate_keys() {
    # In-place rewrite only: appending a second copy of a key is last-wins for
    # the parser but grows the file on every run.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${STALE}" "${LIVE}" \
        >"${SANDBOX}/.env.local"
    run_isolated "${DOCTOR}" --repair >/dev/null 2>&1
    assert_eq \
        "GITHUB_PERSONAL_ACCESS_TOKEN appears exactly once after repair" \
        "1" \
        "$(grep -cE '^[[:space:]]*(export[[:space:]]+)?GITHUB_PERSONAL_ACCESS_TOKEN=' "${SANDBOX}/.env.local")"
    assert_eq \
        "GITHUB_MCP_PAT appears exactly once after repair" \
        "1" \
        "$(grep -cE '^[[:space:]]*(export[[:space:]]+)?GITHUB_MCP_PAT=' "${SANDBOX}/.env.local")"
    destroy_sandbox
}

make_sandbox() {
    SANDBOX="$(mktemp -d "${TMPDIR:-/tmp}/mcp-cred-test.XXXXXX")"
    mkdir -p "${SANDBOX}/home" "${SANDBOX}/codex-home"
}

destroy_sandbox() {
    rm -rf "${SANDBOX}"
    SANDBOX=""
}

# The credential vars are read from process.env FIRST (env() in both scripts),
# so the sandbox must not inherit the developer's real tokens or the test would
# measure the host environment instead of the fixture.
run_isolated() {
    (
        cd "${SANDBOX}" || exit 1
        env -u GITHUB_PERSONAL_ACCESS_TOKEN \
            -u GITHUB_MCP_PAT \
            -u GITHUB_TOKEN \
            -u ZAI_API_KEY \
            -u Z_AI_API_KEY \
            DATAVIZ_MCP_WORKSPACE="${SANDBOX}" \
            DATAVIZ_IN_CONTAINER=1 \
            CODEX_HOME="${SANDBOX}/codex-home" \
            HOME="${SANDBOX}/home" \
            UNITY_MCP_HTTP_PORT="${UNITY_MCP_HTTP_PORT:-9020}" \
            DATAVIZ_MCP_STUB_SKIP_PROBES=1 \
            node "$@"
    )
}

# ---------------------------------------------------------------------------
# configure.mjs: alias divergence must be detected, not treated as success
# ---------------------------------------------------------------------------

test_sync_reports_divergent_github_alias() {
    # RED for the original bug: both names set to different values used to
    # short-circuit to "configured". That is the exact state that produced the
    # 401, so it must be surfaced distinctly.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${STALE}" "${LIVE}" \
        >"${SANDBOX}/.env.local"
    OUT="$(run_isolated "${REPO_ROOT}/.llm/mcp/configure.mjs" --check 2>&1)"
    assert_contains "sync --check flags divergent github alias" "divergent" "${OUT}"
    assert_contains "sync --check names the stale var in use" "GITHUB_PERSONAL_ACCESS_TOKEN" "${OUT}"
    destroy_sandbox
}

test_sync_reports_divergent_zai_alias() {
    # Same defect shape for the Z.ai key pair.
    make_sandbox
    printf 'ZAI_API_KEY=zai-live-placeholder\nZ_AI_API_KEY=zai-stale-placeholder\n' \
        >"${SANDBOX}/.env.local"
    OUT="$(run_isolated "${REPO_ROOT}/.llm/mcp/configure.mjs" --check 2>&1)"
    assert_contains "sync --check flags divergent zai alias" "divergent" "${OUT}"
    destroy_sandbox
}

test_sync_reports_matching_alias_as_configured() {
    # Guard against over-eager divergence reporting: identical values are fine.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${LIVE}" "${LIVE}" \
        >"${SANDBOX}/.env.local"
    OUT="$(run_isolated "${REPO_ROOT}/.llm/mcp/configure.mjs" --check 2>&1)"
    assert_not_contains "sync --check does not flag matching alias" "divergent" "${OUT}"
    assert_contains "sync --check confirms configured" "configured" "${OUT}"
    destroy_sandbox
}

test_sync_still_aliases_single_name() {
    # The original feature must keep working: exactly one name present means it
    # is mirrored into the other.
    make_sandbox
    printf 'GITHUB_MCP_PAT=%s\n' "${LIVE}" >"${SANDBOX}/.env.local"
    run_isolated "${REPO_ROOT}/.llm/mcp/configure.mjs" >/dev/null 2>&1
    RUN_EXIT=$?
    assert_eq "sync exits zero" 0 "${RUN_EXIT}"
    assert_eq \
        "single GITHUB_MCP_PAT is mirrored into GITHUB_PERSONAL_ACCESS_TOKEN" \
        "${LIVE}" \
        "$(grep -E '^GITHUB_PERSONAL_ACCESS_TOKEN=' "${SANDBOX}/.env.local" | cut -d= -f2-)"
    destroy_sandbox
}

test_sync_check_mode_never_writes() {
    # --check must stay read-only even when a repair is pending.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${STALE}" "${LIVE}" \
        >"${SANDBOX}/.env.local"
    BEFORE="$(cat "${SANDBOX}/.env.local")"
    run_isolated "${REPO_ROOT}/.llm/mcp/configure.mjs" --check >/dev/null 2>&1
    assert_eq "--check leaves .env.local untouched" "${BEFORE}" "$(cat "${SANDBOX}/.env.local")"
    destroy_sandbox
}

# ---------------------------------------------------------------------------
# mcp-doctor.mjs: name the variable, and reconcile presence vs. probe
# ---------------------------------------------------------------------------

test_doctor_names_the_credential_source_it_used() {
    # The original doctor printed only "GitHub PAT resolved", leaving the
    # operator unable to tell WHICH var fed the endpoint probe.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\n' "${STALE}" >"${SANDBOX}/.env.local"
    OUT="$(run_isolated "${DOCTOR}" 2>&1)"
    assert_contains "doctor names the github variable it resolved" "GITHUB_PERSONAL_ACCESS_TOKEN" "${OUT}"
    destroy_sandbox
}

test_doctor_flags_divergence_before_probing() {
    # Divergence must be reported as its own actionable finding, and the doctor
    # must point at the repair command rather than implying the file is fine.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\n' "${STALE}" "${LIVE}" \
        >"${SANDBOX}/.env.local"
    OUT="$(run_isolated "${DOCTOR}" 2>&1)"
    assert_contains "doctor flags divergent github credentials" "divergent" "${OUT}"
    assert_contains "doctor points at the repair command" "mcp:doctor --repair" "${OUT}"
    destroy_sandbox
}

test_doctor_never_prints_secret_values() {
    # Safety contract: diagnostic output must never echo a credential.
    make_sandbox
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=%s\nGITHUB_MCP_PAT=%s\nZAI_API_KEY=zai-secret-placeholder\n' \
        "${STALE}" "${LIVE}" >"${SANDBOX}/.env.local"
    OUT="$(run_isolated "${DOCTOR}" 2>&1)"
    assert_not_contains "doctor output omits the stale token" "${STALE}" "${OUT}"
    assert_not_contains "doctor output omits the live token" "${LIVE}" "${OUT}"
    assert_not_contains "doctor output omits the zai key" "zai-secret-placeholder" "${OUT}"
    destroy_sandbox
}

# ---------------------------------------------------------------------------
# Runner
# ---------------------------------------------------------------------------

if [ ! -f "${SYNC}" ]; then
    echo "FAIL: sync script not found at ${SYNC}" >&2
    exit 1
fi
if [ ! -f "${DOCTOR}" ]; then
    echo "FAIL: doctor not found at ${DOCTOR}" >&2
    exit 1
fi

for test_fn in \
    test_sync_reports_divergent_github_alias \
    test_sync_reports_divergent_zai_alias \
    test_sync_reports_matching_alias_as_configured \
    test_sync_still_aliases_single_name \
    test_sync_check_mode_never_writes \
    test_doctor_names_the_credential_source_it_used \
    test_doctor_flags_divergence_before_probing \
    test_doctor_never_prints_secret_values \
    test_repair_reconciles_divergent_github_pair \
    test_repair_refuses_when_neither_credential_authenticates \
    test_repair_respects_reverse_divergence \
    test_repair_leaves_matching_pair_untouched \
    test_repair_never_prints_secret_values \
    test_repair_preserves_bom_and_line_endings \
    test_repair_does_not_duplicate_keys; do
    "${test_fn}"
done

printf '\nmcp-credentials contract tests: %d passed, %d failed.\n' "${PASS}" "${FAIL}"
if [ "${FAIL}" -gt 0 ]; then
    exit 1
fi
exit 0