#!/usr/bin/env bash
# Post-start script: refresh moving npm CLIs and regenerate MCP configs on every
# container start. This is what makes the setup durable across full rebuilds and
# fresh clones: everything machine-local is derived from committed scripts plus
# the gitignored .env.local.

set -euo pipefail

workspace_dir="${1:-$(pwd)}"
cd "${workspace_dir}"

DATAVIZ_NPM_INSTALL_ATTEMPTS="${DATAVIZ_NPM_START_ATTEMPTS:-2}" \
    NPM_CONFIG_FETCH_RETRIES="${DATAVIZ_NPM_START_FETCH_RETRIES:-1}" \
    NPM_CONFIG_FETCH_TIMEOUT="${DATAVIZ_NPM_START_FETCH_TIMEOUT_MS:-10000}" \
    bash .devcontainer/install-npm-tools.sh --offline-ok

# Agent config homes backed by Docker named volumes can end up root-owned when
# the image did not pre-create the mount target (every later chmod then fails
# for the agent CLIs). Repair in place, mirroring the uv cache fix below.
for agent_dir in \
    "${HOME}/.codex" \
    "${HOME}/.claude" \
    "${HOME}/.claude-zai" \
    "${HOME}/.claude-openrouter" \
    "${HOME}/.local/share/opencode" \
    "${HOME}/.config/nanocoder"; do
    if [ -d "${agent_dir}" ] && [ ! -w "${agent_dir}" ]; then
        sudo -n chown -R "$(id -u):$(id -g)" "${agent_dir}" 2>/dev/null || true
    fi
done

bash .devcontainer/ai-backends.sh install

# Ensure the shell env block is present and current. Older images wrote a
# naive `set -a; . .env.local` which dies on a UTF-8 BOM (leaving agent CLIs
# with empty credentials); replace any previous block with the tolerant
# env-local.sh loader.
bashrc="${HOME}/.bashrc"
env_block_begin="# >>> DataVisualizer devcontainer env >>>"
env_block_end="# <<< DataVisualizer devcontainer env <<<"
if [ -f "${bashrc}" ] && grep -qF "${env_block_end}" "${bashrc}"; then
    temp_bashrc="$(mktemp)"
    awk -v begin="${env_block_begin}" -v end="${env_block_end}" '
        $0 == begin { skip = 1; next }
        $0 == end { skip = 0; next }
        skip != 1 { print }
    ' "${bashrc}" > "${temp_bashrc}" && mv "${temp_bashrc}" "${bashrc}"
fi
if ! grep -qF "${env_block_end}" "${bashrc}" 2>/dev/null; then
    {
        echo ""
        echo "${env_block_begin}"
        echo "# Sourced via .devcontainer/env-local.sh (BOM/CRLF-tolerant; fills empty vars)."
        echo "if [ -f \"${workspace_dir}/.devcontainer/env-local.sh\" ]; then"
        echo "    . \"${workspace_dir}/.devcontainer/env-local.sh\""
        echo "    load_env_local \"${workspace_dir}/.env.local\""
        echo "fi"
        echo "export NANOCODER_MCPSERVERS_FILE=\"${workspace_dir}/.nanocoder/mcp.json\""
        echo "export PATH=\"${HOME}/.local/bin:\${PATH}\""
        echo "${env_block_end}"
    } >> "${bashrc}"
fi

# uvx-backed MCP servers (fetch/git) abort at startup when the uv cache is
# not writable (root-owned cache dirs can leak out of root build steps).
uv_cache="${HOME}/.cache/uv"
if [ -d "${uv_cache}" ] && [ ! -w "${uv_cache}" ]; then
    sudo -n chown -R "$(id -u):$(id -g)" "${uv_cache}" 2>/dev/null || true
fi

bash .llm/mcp/sync-mcp.sh "${workspace_dir}"

echo "✅ Data Visualizer dev container ready."
