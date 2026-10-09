#!/usr/bin/env bash
# Contract tests for load_env_local in .devcontainer/env-local.sh.
#
# Run directly:
#   bash .devcontainer/tests/test-env-local-precedence.sh
#
# Background: a repaired GitHub PAT in .env.local had no effect because the
# stale value was already exported into the container's long-lived process
# environment, and the loader's "non-empty preexisting always wins" rule pinned
# it there until a full rebuild. DATAVIZ_ENV_LOCAL_PRECEDENCE=file inverts that
# rule for MCP credential names only.
#
# These tests use synthetic placeholder values and never read the real
# .env.local.

set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LOADER="${REPO_ROOT}/.devcontainer/env-local.sh"

PASS=0
FAIL=0

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

# Runs a fresh bash that sources the loader and applies it to a fixture file,
# printing one variable's resolved value. Each run gets its own environment.
# BASH_BIN is absolute so a test that overrides PATH cannot break the lookup of
# the interpreter itself.
BASH_BIN="$(command -v bash)"
TEST_BASE_PATH="$PATH"

resolve() {
    local env_file="$1"
    local var="$2"
    shift 2
    env -i \
        PATH="${TEST_BASE_PATH}" \
        HOME="${HOME:-/home/vscode}" \
        "$@" \
        "${BASH_BIN}" -c '. "$1"; load_env_local "$2"; printf %s "${!3}"' _ \
        "${LOADER}" "${env_file}" "${var}" 2>/dev/null
}

make_fixture() {
    mktemp "${TMPDIR:-/tmp}/env-local-test.XXXXXX"
}

# ---------------------------------------------------------------------------
# Baseline semantics (unchanged)
# ---------------------------------------------------------------------------

test_fills_empty_preexisting_variable() {
    local fixture
    fixture="$(make_fixture)"
    printf 'TEST_TOKEN=from-file\n' >"${fixture}"
    assert_eq \
        "an empty preexisting variable is filled from the file" \
        "from-file" \
        "$(resolve "${fixture}" TEST_TOKEN)"
    rm -f "${fixture}"
}

test_nonempty_preexisting_variable_wins_by_default() {
    local fixture
    fixture="$(make_fixture)"
    printf 'TEST_TOKEN=from-file\n' >"${fixture}"
    assert_eq \
        "a non-empty preexisting variable wins by default" \
        "from-env" \
        "$(resolve "${fixture}" TEST_TOKEN TEST_TOKEN=from-env)"
    rm -f "${fixture}"
}

test_path_is_never_overridden_under_file_precedence() {
    # The inverted mode must stay scoped to MCP credentials. Overriding PATH
    # would break every agent CLI in the session.
    local fixture
    fixture="$(make_fixture)"
    printf 'PATH=/evil/path\n' >"${fixture}"
    assert_eq \
        "PATH is never overridden even when precedence=file" \
        "from-env" \
        "$(resolve "${fixture}" PATH PATH=from-env DATAVIZ_ENV_LOCAL_PRECEDENCE=file)"
    rm -f "${fixture}"
}

# ---------------------------------------------------------------------------
# Inverted precedence for MCP credentials
# ---------------------------------------------------------------------------

test_github_pat_overridden_under_file_precedence() {
    local fixture
    fixture="$(make_fixture)"
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=repaired-in-file\n' >"${fixture}"
    assert_eq \
        "GITHUB_PERSONAL_ACCESS_TOKEN takes the file value under precedence=file" \
        "repaired-in-file" \
        "$(resolve "${fixture}" GITHUB_PERSONAL_ACCESS_TOKEN \
            GITHUB_PERSONAL_ACCESS_TOKEN=stale-in-env DATAVIZ_ENV_LOCAL_PRECEDENCE=file)"
    rm -f "${fixture}"
}

test_github_mcp_pat_overridden_under_file_precedence() {
    local fixture
    fixture="$(make_fixture)"
    printf 'GITHUB_MCP_PAT=repaired-in-file\n' >"${fixture}"
    assert_eq \
        "GITHUB_MCP_PAT takes the file value under precedence=file" \
        "repaired-in-file" \
        "$(resolve "${fixture}" GITHUB_MCP_PAT \
            GITHUB_MCP_PAT=stale-in-env DATAVIZ_ENV_LOCAL_PRECEDENCE=file)"
    rm -f "${fixture}"
}

test_zai_key_overridden_under_file_precedence() {
    local fixture
    fixture="$(make_fixture)"
    printf 'ZAI_API_KEY=repaired-in-file\n' >"${fixture}"
    assert_eq \
        "ZAI_API_KEY takes the file value under precedence=file" \
        "repaired-in-file" \
        "$(resolve "${fixture}" ZAI_API_KEY \
            ZAI_API_KEY=stale-in-env DATAVIZ_ENV_LOCAL_PRECEDENCE=file)"
    rm -f "${fixture}"
}

test_unmanaged_variable_not_overridden_under_file_precedence() {
    # Scope guard: precedence=file must not turn the loader into a general
    # "the file wins" mode for arbitrary variables.
    local fixture
    fixture="$(make_fixture)"
    printf 'SOME_OTHER_SETTING=from-file\n' >"${fixture}"
    assert_eq \
        "an unmanaged variable keeps the preexisting value under precedence=file" \
        "from-env" \
        "$(resolve "${fixture}" SOME_OTHER_SETTING \
            SOME_OTHER_SETTING=from-env DATAVIZ_ENV_LOCAL_PRECEDENCE=file)"
    rm -f "${fixture}"
}

test_repeated_load_is_idempotent() {
    local fixture
    fixture="$(make_fixture)"
    printf 'GITHUB_PERSONAL_ACCESS_TOKEN=value-in-file\n' >"${fixture}"
    local once twice
    once="$(resolve "${fixture}" GITHUB_PERSONAL_ACCESS_TOKEN \
        GITHUB_PERSONAL_ACCESS_TOKEN=stale DATAVIZ_ENV_LOCAL_PRECEDENCE=file)"
    twice="$(env -i PATH="${TEST_BASE_PATH}" HOME="${HOME:-/home/vscode}" \
        GITHUB_PERSONAL_ACCESS_TOKEN="${once}" DATAVIZ_ENV_LOCAL_PRECEDENCE=file \
        "${BASH_BIN}" -c '. "$1"; load_env_local "$2"; load_env_local "$2"; printf %s "${!3}"' \
        _ "${LOADER}" "${fixture}" GITHUB_PERSONAL_ACCESS_TOKEN 2>/dev/null)"
    assert_eq "loading twice yields the same value as loading once" "${once}" "${twice}"
    rm -f "${fixture}"
}

test_missing_file_is_not_an_error() {
    assert_eq \
        "a missing .env.local leaves the environment untouched" \
        "from-env" \
        "$(resolve "/nonexistent/.env.local" TEST_TOKEN TEST_TOKEN=from-env)"
}

# ---------------------------------------------------------------------------
# Runner
# ---------------------------------------------------------------------------

if [ ! -f "${LOADER}" ]; then
    echo "FAIL: loader not found at ${LOADER}" >&2
    exit 1
fi

for test_fn in \
    test_fills_empty_preexisting_variable \
    test_nonempty_preexisting_variable_wins_by_default \
    test_path_is_never_overridden_under_file_precedence \
    test_github_pat_overridden_under_file_precedence \
    test_github_mcp_pat_overridden_under_file_precedence \
    test_zai_key_overridden_under_file_precedence \
    test_unmanaged_variable_not_overridden_under_file_precedence \
    test_repeated_load_is_idempotent \
    test_missing_file_is_not_an_error; do
    "${test_fn}"
done

printf '\nenv-local precedence tests: %d passed, %d failed.\n' "${PASS}" "${FAIL}"
if [ "${FAIL}" -gt 0 ]; then
    exit 1
fi
exit 0