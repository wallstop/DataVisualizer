#!/usr/bin/env bash
# Post-create script for the Data Visualizer dev container.
# Runs after the container is created; handles verification, repo tooling, hooks.

set -euo pipefail

WORKSPACE_DIR="${1:-$(pwd)}"
if [ ! -d "${WORKSPACE_DIR}" ]; then
    echo "❌ Workspace not found: ${WORKSPACE_DIR}"
    exit 1
fi
cd "${WORKSPACE_DIR}"

echo ""
echo "╔════════════════════════════════════════════════════════════════╗"
echo "║           Data Visualizer Post-Create Setup                    ║"
echo "╚════════════════════════════════════════════════════════════════╝"
echo ""

run_with_retries() {
    local max_attempts="$1"
    shift

    local attempt=1
    local delay_seconds=2
    while true; do
        if "$@"; then
            return 0
        else
            local exit_code=$?
        fi
        if [ "${attempt}" -ge "${max_attempts}" ]; then
            return "${exit_code}"
        fi

        local jitter=$((RANDOM % 3))
        local sleep_seconds=$((delay_seconds + jitter))
        local next_attempt=$((attempt + 1))
        echo "   Retry ${next_attempt}/${max_attempts} in ${sleep_seconds}s: $*"
        sleep "${sleep_seconds}"
        attempt="${next_attempt}"
        if [ "${delay_seconds}" -lt 30 ]; then
            delay_seconds=$((delay_seconds * 2))
            if [ "${delay_seconds}" -gt 30 ]; then
                delay_seconds=30
            fi
        fi
    done
}

# ============================================================================
# STEP 1: Restore .NET local tools (CSharpier formatting gate)
# ============================================================================
echo "🔧 Step 1/3: Restoring .NET local tools (CSharpier)..."
if [ -f ".config/dotnet-tools.json" ]; then
    if command -v dotnet >/dev/null 2>&1; then
        run_with_retries 3 dotnet tool restore --verbosity minimal || \
            echo "   Warning: dotnet tool restore failed; run it manually before formatting."
    else
        echo "   Warning: dotnet not found; CSharpier formatting gate unavailable."
    fi
else
    echo "   Skipped (no .config/dotnet-tools.json)"
fi

# ============================================================================
# STEP 2: Install pre-commit hooks (best-effort)
# ============================================================================
echo ""
echo "🪝 Step 2/3: Installing pre-commit hooks..."
if [ -f ".pre-commit-config.yaml" ]; then
    if command -v pre-commit >/dev/null 2>&1; then
        pre-commit install || echo "   Warning: pre-commit install failed"
    elif command -v python3 >/dev/null 2>&1; then
        echo "   Installing pre-commit via pip (user scope)..."
        python3 -m pip install --user --break-system-packages --quiet pre-commit 2>/dev/null \
            && "${HOME}/.local/bin/pre-commit" install \
            || echo "   Warning: could not install pre-commit; hooks not activated."
    else
        echo "   Skipped (python3 unavailable)"
    fi
else
    echo "   Skipped (no .pre-commit-config.yaml)"
fi

# ============================================================================
# STEP 3: Environment verification banner
# ============================================================================
echo ""
echo "🔎 Step 3/3: Verifying toolchain..."
echo ""

echo "📌 Node.js Tooling:"
printf "   %-18s %s\n" "Node.js:" "$(node --version 2>&1 || echo NOT-FOUND)"
printf "   %-18s %s\n" "npm:" "$(npm --version 2>&1 || echo NOT-FOUND)"
printf "   %-18s %s\n" "Nanocoder:" "$(nanocoder --version 2>&1 || echo NOT-FOUND)"
printf "   %-18s %s\n" "OpenCode:" "$(opencode --version 2>&1 || echo NOT-FOUND)"
printf "   %-18s %s\n" "Codex CLI:" "$(codex --version 2>&1 || echo NOT-FOUND)"
printf "   %-18s %s\n" "Claude Code:" "$(claude --version 2>&1 || echo NOT-FOUND)"
printf "   %-18s %s\n" "codex-zai:" "$(command -v codex-zai >/dev/null 2>&1 && echo ready || echo missing)"
printf "   %-18s %s\n" "claude-zai:" "$(command -v claude-zai >/dev/null 2>&1 && echo ready || echo missing)"

echo ""
echo "📌 Repo Tooling:"
printf "   %-18s %s\n" "PowerShell:" "$(pwsh --version 2>&1 || echo NOT-FOUND)"
printf "   %-18s %s\n" "dotnet SDKs:" "$(dotnet --list-sdks 2>/dev/null | tr '\n' ' ' || echo NOT-FOUND)"
printf "   %-18s %s\n" "CSharpier:" "$(dotnet tool run csharpier --version 2>&1 | tail -1 || echo NOT-FOUND)"
printf "   %-18s %s\n" "uvx (fetch/git MCP):" "$(uvx --version 2>&1 || echo NOT-FOUND)"

echo ""
echo "📌 Unity MCP (official Unity CLI, ~140 tools):"
UNITY_MCP_HEALTH="$(curl -s -m 3 http://host.docker.internal:9020/healthz 2>/dev/null || true)"
if printf '%s' "${UNITY_MCP_HEALTH}" | grep -q '"ok":true'; then
    echo "   Host bridge ready on host.docker.internal:9020."
else
    echo "   Host bridge not reachable yet."
    echo "   On the host: npm run unity:mcp:host (and unity pipeline install once)."
fi

echo ""
echo "╔════════════════════════════════════════════════════════════════╗"
echo "║           ✅ Setup Complete!                                    ║"
echo "╠════════════════════════════════════════════════════════════════╣"
echo "║  Quick commands:                                               ║"
echo "║    npm run mcp:sync              Reconfigure agent MCP servers ║"
echo "║    npm run unity:mcp:host        (Host) Unity MCP HTTP bridge  ║"
echo "║    npm run tools:install         Refresh coding CLIs           ║"
echo "║    codex-zai / claude-zai        Z.ai subscription backends    ║"
echo "║                                                                ║"
echo "║  Secrets live in .env.local (gitignored). IntelliSense loads   ║"
echo "║  automatically via the Roslyn LSP.                             ║"
echo "╚════════════════════════════════════════════════════════════════╝"
echo ""
