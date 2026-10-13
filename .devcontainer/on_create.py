#!/usr/bin/env python3
"""On-create script for the Data Visualizer dev container.

Typed Python port of ``on-create.sh`` (issue #171). Runs once at container
creation, BEFORE VS Code extensions are activated. The image already contains
Node LTS, python3, and uv, so this stays fast.

Steps: bootstrap the gitignored ``.env.local`` credential store, wire it and
the tool homes into interactive shells, refresh the npm coding tools and
install the Z.ai backend launchers, and configure MCP servers for every
supported agent frontend.
"""

from __future__ import annotations

import os
import secrets
import shutil
import sys
from pathlib import Path
from typing import Dict, List, Sequence

sys.dont_write_bytecode = True

from _lifecycle_common import (
    ENV_BLOCK_BEGIN,
    ENV_BLOCK_END,
    awk_strip_env_block,
    read_bashrc_text,
    resolve_workspace,
    run_child,
    run_child_strict,
    write_bashrc_text,
)

ENV_LOCAL_TEMPLATE = """# Machine-local credentials (gitignored). Prefer this file for all secrets.
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
UNITY_MCP_TOKEN={token}
"""


def bootstrap_env_local(workspace: Path) -> None:
    """Create ``.env.local`` with an auto-generated bridge token, or keep the existing one.

    The token comes from ``secrets.token_hex(24)``: the same 48 hex characters
    the bash original produced with ``node -e crypto.randomBytes(24)``, without
    needing node this early in the boot path.
    """
    env_local = workspace / ".env.local"
    if env_local.is_file():
        try:
            env_local.chmod(0o600)
        except OSError:
            pass
        print(f"   Keeping existing {env_local}.")
        return
    token = secrets.token_hex(24)
    with open(env_local, "w", encoding="utf-8", newline="") as stream:
        stream.write(ENV_LOCAL_TEMPLATE.format(token=token))
    env_local.chmod(0o600)
    print(f"   Created {env_local} (chmod 600) with an auto-generated UNITY_MCP_TOKEN.")
    print("   Add your ZAI_API_KEY and GITHUB_PERSONAL_ACCESS_TOKEN to enable those MCP tools.")


def bashrc_block_lines(workspace: Path) -> List[str]:
    """Build the bashrc block the original wrote, line for line."""
    workspace_arg = str(workspace)
    home = str(Path.home())
    return [
        ENV_BLOCK_BEGIN,
        "# Sourced via .devcontainer/env-local.sh (BOM/CRLF-tolerant; fills empty vars).",
        f'if [ -f "{workspace_arg}/.devcontainer/env-local.sh" ]; then',
        f'    . "{workspace_arg}/.devcontainer/env-local.sh"',
        f'    load_env_local "{workspace_arg}/.env.local"',
        "fi",
        f'export NANOCODER_MCPSERVERS_FILE="{workspace_arg}/.nanocoder/mcp.json"',
        f'export PATH="{home}/.local/bin:${{PATH}}"',
        ENV_BLOCK_END,
    ]


def wire_shell_environment(workspace: Path) -> None:
    """Replace any previous bashrc block so every shell sources .env.local.

    One atomic write composes the stripped content and the new block, the way
    the original piped awk output into its temp file before the single ``mv``.
    """
    block = "".join(f"{line}\n" for line in bashrc_block_lines(workspace))
    updated = awk_strip_env_block(read_bashrc_text(Path.home())) + "\n" + block
    write_bashrc_text(Path.home(), updated)
    print("   .env.local is sourced by every interactive shell; launchers on PATH.")


def refresh_npm_tools(workspace: Path) -> None:
    """Refresh the npm coding tools; image-baked versions remain on failure."""
    attempts = os.environ.get("DATAVIZ_NPM_CREATE_ATTEMPTS") or "3"
    env: Dict[str, str] = dict(os.environ)
    env["DATAVIZ_NPM_INSTALL_ATTEMPTS"] = attempts
    code = run_child(["python3", ".devcontainer/install_npm_tools.py", "--offline-ok"], workspace, env)
    if code != 0:
        print("⚠️  npm tool refresh failed at create time; image-baked versions remain installed.", file=sys.stderr)


def configure_mcp(workspace: Path) -> None:
    """Regenerate the machine-local MCP configs when node is available."""
    if shutil.which("node") is not None:
        code = run_child(["bash", ".llm/mcp/sync-mcp.sh", str(workspace)], workspace)
        if code != 0:
            print("⚠️  MCP configuration reported warnings; run 'npm run mcp:sync' to retry.")
        return
    print("⚠️  node not found; MCP configuration skipped.")


def main(argv: Sequence[str]) -> int:
    workspace = resolve_workspace(argv)
    os.chdir(workspace)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(line_buffering=True)  # type: ignore[union-attr]

    print()
    print("╔════════════════════════════════════════════════════════════════╗")
    print("║        Data Visualizer On-Create Setup (Pre-Extension)         ║")
    print("╚════════════════════════════════════════════════════════════════╝")
    print()

    print("🔐 Step 1/4: Bootstrapping .env.local...")
    bootstrap_env_local(workspace)

    print("🐚 Step 2/4: Configuring shell environment...")
    wire_shell_environment(workspace)

    print()
    print("🤖 Step 3/4: Refreshing npm coding tools and Z.ai backends...")
    refresh_npm_tools(workspace)
    run_child_strict(["bash", ".devcontainer/ai-backends.sh", "install"], workspace)

    print()
    print("🔌 Step 4/4: Configuring MCP servers (claude, codex, opencode, nanocoder, vscode)...")
    configure_mcp(workspace)

    print()
    print("✅ On-create complete.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
