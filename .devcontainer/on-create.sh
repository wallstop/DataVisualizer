#!/usr/bin/env bash
# On-create script for the Data Visualizer dev container.
# Runs once at container creation, BEFORE VS Code extensions are activated.
# The image already contains Node LTS + the four coding CLIs, so this stays fast.

set -euo pipefail

WORKSPACE_DIR="${1:-$(pwd)}"
if [ ! -d "${WORKSPACE_DIR}" ]; then
    echo "❌ Workspace not found: ${WORKSPACE_DIR}"
    exit 1
fi
cd "${WORKSPACE_DIR}"

echo ""
echo "╔════════════════════════════════════════════════════════════════╗"
echo "║        Data Visualizer On-Create Setup (Pre-Extension)         ║"
echo "╚════════════════════════════════════════════════════════════════╝"
echo ""

# ============================================================================
# STEP 1: Bootstrap the gitignored .env.local credential store
# ============================================================================
echo "🔐 Step 1/4: Bootstrapping .env.local..."
ENV_LOCAL="${WORKSPACE_DIR}/.env.local"
if [ ! -f "${ENV_LOCAL}" ]; then
    unity_mcp_token="$(node -e 'process.stdout.write(require("node:crypto").randomBytes(24).toString("hex"))')"
    cat > "${ENV_LOCAL}" <<EOF
# Machine-local credentials (gitignored). Prefer this file for all secrets.
# See .env.local.example for the full annotated template.

# Z.ai (GLM Coding Plan): https://docs.z.ai/devpack/overview
ZAI_API_KEY=

# OpenRouter (https://openrouter.ai/keys): claude-openrouter / codex-openrouter
OPENROUTER_API_KEY=

# GitHub PAT for the GitHub MCP server (repo scope is enough for public repos)
GITHUB_PERSONAL_ACCESS_TOKEN=

# Context7 API key (optional; anonymous access works, key lifts rate limits)
CONTEXT7_API_KEY=

# Shared Unity MCP bridge token (auto-generated; host and container must match)
UNITY_MCP_TOKEN=${unity_mcp_token}
EOF
    chmod 600 "${ENV_LOCAL}"
    echo "   Created ${ENV_LOCAL} (chmod 600) with an auto-generated UNITY_MCP_TOKEN."
    echo "   Add your ZAI_API_KEY and GITHUB_PERSONAL_ACCESS_TOKEN to enable those MCP tools."
else
    chmod 600 "${ENV_LOCAL}" 2>/dev/null || true
    echo "   Keeping existing ${ENV_LOCAL}."
fi

# ============================================================================
# STEP 2: Wire .env.local + tool homes into interactive shells
# ============================================================================
echo "🐚 Step 2/4: Configuring shell environment..."
bashrc="${HOME}/.bashrc"
temp_bashrc="$(mktemp)"
if [ -f "${bashrc}" ]; then
    awk '
        $0 == "# >>> DataVisualizer devcontainer env >>>" { skip = 1; next }
        $0 == "# <<< DataVisualizer devcontainer env <<<" { skip = 0; next }
        skip != 1 { print }
    ' "${bashrc}" > "${temp_bashrc}"
fi
{
    echo ""
    echo "# >>> DataVisualizer devcontainer env >>>"
    echo "# Sourced via .devcontainer/env-local.sh (BOM/CRLF-tolerant; fills empty vars)."
    echo "if [ -f \"${WORKSPACE_DIR}/.devcontainer/env-local.sh\" ]; then"
    echo "    . \"${WORKSPACE_DIR}/.devcontainer/env-local.sh\""
    echo "    load_env_local \"${WORKSPACE_DIR}/.env.local\""
    echo "fi"
    echo "export NANOCODER_MCPSERVERS_FILE=\"${WORKSPACE_DIR}/.nanocoder/mcp.json\""
    echo "export PATH=\"${HOME}/.local/bin:\${PATH}\""
    echo "# <<< DataVisualizer devcontainer env <<<"
} >> "${temp_bashrc}"
mv "${temp_bashrc}" "${bashrc}"
echo "   .env.local is sourced by every interactive shell; launchers on PATH."

# ============================================================================
# STEP 3: Refresh npm coding tools + install Z.ai backend launchers
# ============================================================================
echo ""
echo "🤖 Step 3/4: Refreshing npm coding tools and Z.ai backends..."
DATAVIZ_NPM_INSTALL_ATTEMPTS="${DATAVIZ_NPM_CREATE_ATTEMPTS:-3}" \
    bash .devcontainer/install-npm-tools.sh --offline-ok || {
        echo "⚠️  npm tool refresh failed at create time; image-baked versions remain installed." >&2
    }
bash .devcontainer/ai-backends.sh install

# ============================================================================
# STEP 4: Configure MCP servers for every supported agent frontend
# ============================================================================
echo ""
echo "🔌 Step 4/4: Configuring MCP servers (claude, codex, opencode, nanocoder, vscode)..."
if command -v node >/dev/null 2>&1; then
    bash .llm/mcp/sync-mcp.sh "${WORKSPACE_DIR}" || echo "⚠️  MCP configuration reported warnings; run 'npm run mcp:sync' to retry."
else
    echo "⚠️  node not found; MCP configuration skipped."
fi

echo ""
echo "✅ On-create complete."
