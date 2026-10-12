#!/usr/bin/env python3
"""Update-content script: runs after git pull/clone operations inside the
devcontainer. Restores repo tooling and re-derives machine-local MCP configs
so switched branches or fresh checkouts are immediately agent-ready.

Typed Python port of ``update-content.sh`` (issue #171).
"""

from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path
from typing import Sequence

sys.dont_write_bytecode = True

from _lifecycle_common import resolve_workspace, run_child, run_child_strict


def restore_dotnet_tools(workspace: Path) -> None:
    """Restore the .NET local tools when the manifest and the SDK are present."""
    if not (workspace / ".config" / "dotnet-tools.json").is_file() or shutil.which("dotnet") is None:
        return
    code = run_child(["dotnet", "tool", "restore", "--verbosity", "minimal"], workspace)
    if code != 0:
        print("⚠️  dotnet tool restore failed; run manually.")


def sync_mcp_configs(workspace: Path) -> None:
    """Re-derive the machine-local MCP configs when node is available."""
    if shutil.which("node") is None:
        return
    run_child_strict(["bash", ".llm/mcp/sync-mcp.sh", str(workspace)], workspace)


def main(argv: Sequence[str]) -> int:
    workspace = resolve_workspace(argv)
    os.chdir(workspace)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(line_buffering=True)  # type: ignore[union-attr]

    restore_dotnet_tools(workspace)
    sync_mcp_configs(workspace)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
