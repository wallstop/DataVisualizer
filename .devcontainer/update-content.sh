#!/usr/bin/env bash
# Update-content script: runs after git pull/clone operations inside the
# devcontainer. Restores repo tooling and re-derives machine-local MCP configs
# so switched branches or fresh checkouts are immediately agent-ready.

set -euo pipefail

WORKSPACE_DIR="${1:-$(pwd)}"
if [ ! -d "${WORKSPACE_DIR}" ]; then
    echo "❌ Workspace not found: ${WORKSPACE_DIR}"
    exit 1
fi
cd "${WORKSPACE_DIR}"

if [ -f ".config/dotnet-tools.json" ] && command -v dotnet >/dev/null 2>&1; then
    dotnet tool restore --verbosity minimal || echo "⚠️  dotnet tool restore failed; run manually."
fi

if command -v node >/dev/null 2>&1; then
    bash .llm/mcp/sync-mcp.sh "${WORKSPACE_DIR}"
fi
