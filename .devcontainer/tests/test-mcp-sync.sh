#!/usr/bin/env bash
# Contract tests for .llm/mcp/configure.mjs (npm run mcp:sync).
#
# Run directly:
#   bash .devcontainer/tests/test-mcp-sync.sh
# or via the PowerShell wrapper (scripts/tests/test-mcp-sync.ps1), which the
# harness self-test runner and CI invoke.
#
# The suite syncs into a throwaway workspace with throwaway HOME and CODEX_HOME
# so the real machine-local configs (and any real credentials) are untouched,
# and asserts that every supported agent frontend receives the same server
# fleet with placeholder-only credentials (no secrets on disk).

set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SYNC="${REPO_ROOT}/.llm/mcp/sync-mcp.sh"
if [ ! -f "${SYNC}" ]; then
    echo "FAIL: sync script not found at ${SYNC}" >&2
    exit 1
fi

PASS=0
FAIL=0
SANDBOX=""

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

assert_file_contains() {
    if [ -f "$2" ] && grep -Fq -- "$3" "$2"; then
        pass "$1"
    else
        fail "$1 (missing [$3] in $2)"
    fi
}

make_sandbox() {
    SANDBOX="$(mktemp -d "${TMPDIR:-/tmp}/mcp-sync-test.XXXXXX")"
    mkdir -p "${SANDBOX}/home" "${SANDBOX}/codex-home"
}

destroy_sandbox() {
    rm -rf "${SANDBOX}"
    SANDBOX=""
}

run_sync() {
    (cd "${SANDBOX}" && \
        DATAVIZ_MCP_WORKSPACE="${SANDBOX}" \
        DATAVIZ_IN_CONTAINER=1 \
        CODEX_HOME="${SANDBOX}/codex-home" \
        HOME="${SANDBOX}/home" \
        bash "${SYNC}" "${SANDBOX}" \
        >"${SANDBOX}/stdout.txt" 2>"${SANDBOX}/stderr.txt")
    RUN_EXIT=$?
}

# The managed fleet; fetch/git only register when uvx exists (mirrors the
# configurator), so the expected set is derived from the same condition.
# Names are emitted sorted because jq `keys` (and the codex grep) sort too.
expected_fleet() {
    local fleet="unity github context7 zai-vision zai-web-search zai-image"
    if command -v uvx >/dev/null 2>&1; then
        fleet="unity github context7 fetch git zai-vision zai-web-search zai-image"
    fi
    printf '%s\n' "${fleet}" | tr ' ' '\n' | sort | tr '\n' ' ' | sed 's/ $//'
}

test_sync_generates_all_frontends() {
    make_sandbox
    run_sync
    assert_eq "sync exits zero" 0 "${RUN_EXIT}"
    for file in .mcp.json opencode.json .vscode/mcp.json .nanocoder/mcp.json .cursor/mcp.json; do
        if [ -f "${SANDBOX}/${file}" ]; then
            pass "sync generated ${file}"
        else
            fail "sync did not generate ${file}"
        fi
    done
    if [ -f "${SANDBOX}/codex-home/config.toml" ]; then
        pass "sync generated codex config.toml"
    else
        fail "sync did not generate codex config.toml"
    fi
    destroy_sandbox
}

test_server_fleet_is_identical_across_frontends() {
    make_sandbox
    run_sync
    local expected
    expected="$(expected_fleet)"
    assert_eq \
        "claude .mcp.json fleet matches catalog" \
        "${expected}" \
        "$(jq -r '.mcpServers | keys | join(" ")' "${SANDBOX}/.mcp.json" 2>/dev/null)"
    assert_eq \
        "opencode fleet matches catalog" \
        "${expected}" \
        "$(jq -r '.mcp | keys | join(" ")' "${SANDBOX}/opencode.json" 2>/dev/null)"
    assert_eq \
        "nanocoder fleet matches catalog" \
        "${expected}" \
        "$(jq -r '.mcpServers | keys | join(" ")' "${SANDBOX}/.nanocoder/mcp.json" 2>/dev/null)"
    assert_eq \
        "vscode fleet matches catalog" \
        "${expected}" \
        "$(jq -r '.servers | keys | join(" ")' "${SANDBOX}/.vscode/mcp.json" 2>/dev/null)"
    assert_eq \
        "codex fleet matches catalog" \
        "${expected}" \
        "$(grep -oE '^\[mcp_servers\.[a-z0-9-]+\]' \
            "${SANDBOX}/codex-home/config.toml" \
            | sed 's/^\[mcp_servers\.//; s/\]$//' | sort | tr '\n' ' ' | sed 's/ $//')"
    # Cursor runs on the host and only carries host-executable entries, so
    # zai-image (container-local script path) is intentionally absent.
    local cursor_expected
    cursor_expected="$(printf '%s\n' ${expected} | grep -vx 'zai-image' | sort | tr '\n' ' ' | sed 's/ $//')"
    assert_eq \
        "cursor fleet matches host-executable catalog" \
        "${cursor_expected}" \
        "$(jq -r '.mcpServers | keys | join(" ")' "${SANDBOX}/.cursor/mcp.json" 2>/dev/null)"
    destroy_sandbox
}

test_claude_project_servers_are_preapproved() {
    # Sync must record approval state the way the interactive dialogs do so
    # project-scope servers are usable without a first-run prompt.
    make_sandbox
    run_sync
    assert_file_contains \
        "claude settings auto-approve project servers" \
        "${SANDBOX}/.claude/settings.local.json" \
        '"enableAllProjectMcpServers": true'
    assert_file_contains \
        "claude home state records folder trust" \
        "${SANDBOX}/home/.claude.json" \
        '"hasTrustDialogAccepted": true'
    destroy_sandbox
}

test_generated_configs_contain_no_literal_secrets() {
    # Config files must carry env-var REFERENCES only; real credentials live in
    # .env.local. A literal key in any generated file fails the suite.
    make_sandbox
    run_sync
    local hits
    hits="$(grep -RIlE \
        'sk-(or-)?[A-Za-z0-9_-]{16,}|ghp_[A-Za-z0-9]{20,}|Bearer [A-Za-z0-9._-]{16,}' \
        "${SANDBOX}/.mcp.json" \
        "${SANDBOX}/opencode.json" \
        "${SANDBOX}/.vscode/mcp.json" \
        "${SANDBOX}/.nanocoder/mcp.json" \
        "${SANDBOX}/.cursor/mcp.json" \
        "${SANDBOX}/codex-home/config.toml" 2>/dev/null || true)"
    assert_eq "no literal bearer/secret tokens in generated configs" "" "${hits}"
    assert_file_contains \
        "claude github auth uses env placeholder" \
        "${SANDBOX}/.mcp.json" \
        'Bearer ${GITHUB_PERSONAL_ACCESS_TOKEN'
    assert_file_contains \
        "claude unity uses the official CLI HTTP bridge" \
        "${SANDBOX}/.mcp.json" \
        '"url": "http://host.docker.internal:9020/mcp"'
    assert_file_contains \
        "claude github enables all toolsets" \
        "${SANDBOX}/.mcp.json" \
        '"X-MCP-Toolsets": "all"'
    assert_file_contains \
        "codex github enables all toolsets" \
        "${SANDBOX}/codex-home/config.toml" \
        '"X-MCP-Toolsets" = "all"'
    assert_file_contains \
        "cursor unity launches the official CLI (host stdio)" \
        "${SANDBOX}/.cursor/mcp.json" \
        '"command": "unity"'
    assert_file_contains \
        "opencode zai auth uses env placeholder" \
        "${SANDBOX}/opencode.json" \
        'Bearer {env:ZAI_API_KEY}'
    assert_file_contains \
        "codex github auth uses env var name" \
        "${SANDBOX}/codex-home/config.toml" \
        'bearer_token_env_var = "GITHUB_PERSONAL_ACCESS_TOKEN"'
    assert_file_contains \
        "codex zai-vision passes Z_AI_MODE through (every other frontend sets it)" \
        "${SANDBOX}/codex-home/config.toml" \
        'env_vars = ["ZAI_API_KEY", "Z_AI_MODE"]'
    destroy_sandbox
}

# ---------------------------------------------------------------------------
# Runner
# ---------------------------------------------------------------------------

for test_fn in \
    test_sync_generates_all_frontends \
    test_server_fleet_is_identical_across_frontends \
    test_claude_project_servers_are_preapproved \
    test_generated_configs_contain_no_literal_secrets; do
    "${test_fn}"
done

printf '\nmcp-sync contract tests: %d passed, %d failed.\n' "${PASS}" "${FAIL}"
if [ "${FAIL}" -gt 0 ]; then
    exit 1
fi
exit 0
