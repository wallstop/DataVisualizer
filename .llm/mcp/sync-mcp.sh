#!/usr/bin/env bash
# Sync MCP server configuration into every supported agent frontend.
# Idempotent; safe to run at any time (container hooks and `npm run mcp:sync`).
#
# Usage:
#   bash sync-mcp.sh [workspace_dir] [configure.mjs flags...]
#
# A leading-dash first argument (e.g. `--check`) is forwarded to
# configure.mjs instead of being treated as the workspace directory.

set -euo pipefail

WORKSPACE_DIR=""
if [ "$#" -gt 0 ] && [ -n "$1" ] && [ "${1#-}" = "$1" ]; then
    WORKSPACE_DIR="$1"
    shift
fi
# Resolve the configurator path before cd-ing: the workspace argument may be
# any directory, so a script-relative invocation would break after the cd.
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -z "${WORKSPACE_DIR}" ]; then
    WORKSPACE_DIR="$(cd "${script_dir}/../.." && pwd)"
fi
cd "${WORKSPACE_DIR}"
export DATAVIZ_MCP_WORKSPACE="${WORKSPACE_DIR}"

if ! command -v node >/dev/null 2>&1; then
    echo "❌ node is required to sync MCP configuration." >&2
    exit 1
fi

exec node "${script_dir}/configure.mjs" "$@"
