#!/usr/bin/env python3
"""Post-start script: refresh moving npm CLIs and regenerate MCP configs on every
container start. This is what makes the setup durable across full rebuilds and
fresh clones: everything machine-local is derived from committed scripts plus
the gitignored .env.local.

Typed Python port of ``post-start.sh`` (issue #171).
"""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

sys.dont_write_bytecode = True

from env_local import load_environment

from _lifecycle_common import (
    ENV_BLOCK_BEGIN,
    ENV_BLOCK_END,
    append_bashrc_block,
    awk_strip_env_block,
    bashrc_has_block_end,
    read_bashrc_text,
    repair_ownership,
    resolve_workspace,
    run_child,
    run_child_strict,
    write_bashrc_text,
)

DOCTOR_FILTER_PATTERN = re.compile(r"❌|divergent|Repairing|authenticates")

AGENT_CONFIG_DIRS: Tuple[str, ...] = (
    ".codex",
    ".claude",
    ".claude-zai",
    ".claude-openrouter",
    ".local/share/opencode",
    ".config/nanocoder",
)


def refresh_npm_tools(workspace: Path) -> None:
    """Refresh the npm coding tools with the start-time retry budgets."""
    env: Dict[str, str] = dict(os.environ)
    env["DATAVIZ_NPM_INSTALL_ATTEMPTS"] = os.environ.get("DATAVIZ_NPM_START_ATTEMPTS") or "2"
    env["NPM_CONFIG_FETCH_RETRIES"] = os.environ.get("DATAVIZ_NPM_START_FETCH_RETRIES") or "1"
    env["NPM_CONFIG_FETCH_TIMEOUT"] = os.environ.get("DATAVIZ_NPM_START_FETCH_TIMEOUT_MS") or "10000"
    run_child_strict([sys.executable, ".devcontainer/install_npm_tools.py", "--offline-ok"], workspace, env)


def repair_agent_config_ownership(home: Path) -> None:
    """Repair root-owned agent config homes in place (every later chmod fails otherwise)."""
    for relative in AGENT_CONFIG_DIRS:
        agent_dir = home / relative
        if agent_dir.is_dir() and not os.access(agent_dir, os.W_OK):
            repair_ownership(agent_dir)


def replace_bashrc_block(home: Path, workspace: Path) -> None:
    """Ensure the shell env block is present and current.

    Older images wrote a naive ``set -a; . .env.local`` which dies on a UTF-8
    BOM (leaving agent CLIs with empty credentials); replace any previous block
    with the tolerant env-local.sh loader. A block that is present is stripped
    and re-appended at the end; a bashrc without the block gains it.
    """
    if bashrc_has_block_end(home):
        write_bashrc_text(home, awk_strip_env_block(read_bashrc_text(home)))
    if not bashrc_has_block_end(home):
        workspace_arg = str(workspace)
        home_arg = str(home)
        block: List[str] = [
            ENV_BLOCK_BEGIN,
            "# Sourced via .devcontainer/env-local.sh (BOM/CRLF-tolerant; fills empty vars).",
            "# precedence=file lets MCP credential names in .env.local override a stale",
            "# copy already exported into this session, so a rotated PAT applies without",
            "# rebuilding the container. PATH and other vars keep the default rule.",
            f'if [ -f "{workspace_arg}/.devcontainer/env-local.sh" ]; then',
            f'    . "{workspace_arg}/.devcontainer/env-local.sh"',
            "    DATAVIZ_ENV_LOCAL_PRECEDENCE=file",
            f'    load_env_local "{workspace_arg}/.env.local"',
            "fi",
            f'export NANOCODER_MCPSERVERS_FILE="{workspace_arg}/.nanocoder/mcp.json"',
            f'export PATH="{home_arg}/.local/bin:${{PATH}}"',
            ENV_BLOCK_END,
        ]
        append_bashrc_block(home, block)


def repair_cache_ownership(home: Path) -> None:
    """Repair root-owned uv and npm caches so uvx-backed servers and npm work."""
    uv_cache = home / ".cache" / "uv"
    if uv_cache.is_dir() and not os.access(uv_cache, os.W_OK):
        repair_ownership(uv_cache)
    npm_cache = home / ".npm"
    if npm_cache.is_dir() and not os.access(npm_cache, os.W_OK):
        repair_ownership(npm_cache)


def child_env_with_env_local(repo_root: Path) -> Dict[str, str]:
    """Load credentials for child tools without spawning a shell.

    File precedence applies only to managed credentials; PATH and unrelated
    non-empty variables retain their inherited values.
    """
    environment = dict(os.environ)
    if 'DATAVIZ_ENV_LOCAL_PRECEDENCE' in environment:
        environment['DATAVIZ_ENV_LOCAL_PRECEDENCE'] = 'file'
    try:
        return load_environment(repo_root / '.env.local', environment, prefer_file=True)
    except OSError:
        print('Cannot read .env.local; aborting startup.', file=sys.stderr)
        raise SystemExit(11)


def repair_credential_aliases(workspace: Path, env: Optional[Dict[str, str]]) -> None:
    """Run the MCP credential doctor when .env.local exists, showing only its findings.

    Reconciles credential aliases BEFORE the config sync: when .env.local carries
    both names for one credential with different values, every generated config
    references the stale one and the hosted GitHub MCP answers 401. ``--repair``
    probes which value actually authenticates and rewrites the loser in place; it
    never writes on an unproven guess and never prints a credential.
    """
    if not (workspace / ".env.local").is_file():
        return
    node = shutil.which("node")
    if node is None:
        print("node: command not found; skipping the credential repair.", file=sys.stderr)
        return
    completed = subprocess.run(
        [node, ".llm/mcp/mcp-doctor.mjs", "--repair"],
        cwd=str(workspace),
        env=env,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    output = (completed.stdout or "") + (completed.stderr or "")
    for line in output.splitlines():
        if DOCTOR_FILTER_PATTERN.search(line):
            print(line)


def recycle_stale_service(workspace: Path) -> None:
    """Recycle a stale shared opencode service so the next session respawns healthy.

    Every opencode session attaches to ONE shared background server
    (``serve --service``) that inherits the environment of whichever client
    started it and keeps it for life. Repairing .env.local therefore leaves NEW
    sessions still broken - they are fine, the singleton behind them is not. A
    matching service is never touched, and a missing .env.local is a no-op.
    """
    run_child([sys.executable, str(workspace / ".devcontainer" / "recycle_stale_opencode_service.py")], workspace)


def warm_opencode_service() -> None:
    """Touch the shared service so the first attach never reports no MCP servers.

    OpenCode V2 runs a shared background server (``serve --service``) that
    lazy-loads project config on first attach: the very first ``opencode mcp
    list`` after a service restart can briefly report "No MCP servers
    configured". Warm the service at startup so that race never surfaces in a
    user's first command.
    """
    subprocess.run(["opencode", "mcp", "list"], capture_output=True, check=False)
    time.sleep(1)
    subprocess.run(["opencode", "mcp", "list"], capture_output=True, check=False)


def main(argv: Sequence[str]) -> int:
    workspace = resolve_workspace(argv)
    os.chdir(workspace)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(line_buffering=True)  # type: ignore[union-attr]
    home = Path.home()

    refresh_npm_tools(workspace)
    repair_agent_config_ownership(home)
    run_child_strict([sys.executable, ".devcontainer/ai_backends.py", "install"], workspace)
    replace_bashrc_block(home, workspace)
    repair_cache_ownership(home)

    env = child_env_with_env_local(workspace)
    repair_credential_aliases(workspace, env)
    run_child_strict([sys.executable, ".llm/mcp/sync_mcp.py", str(workspace)], workspace, env)

    if shutil.which("opencode") is not None:
        recycle_stale_service(workspace)
        warm_opencode_service()

    print("✅ Data Visualizer dev container ready.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
