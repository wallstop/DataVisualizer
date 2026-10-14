#!/usr/bin/env python3
"""Sync frontend MCP configs from an optional workspace (issue #171).

Usage: uv run .llm/mcp/sync_mcp.py [workspace_dir] [configure.mjs flags...]
A leading-dash argument belongs to the configurator, not the workspace.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Sequence


def main(arguments: Sequence[str]) -> int:
    """Run the adjacent Node configurator with the wrapper's workspace contract."""
    args = list(arguments)
    # Trust entries use the workspace spelling opened by the agent, so retain
    # symlinks in both an explicit workspace and a script-relative default.
    script_dir = Path(os.path.abspath(__file__)).parent
    workspace = script_dir.parent.parent
    if args and args[0] and not args[0].startswith("-"):
        workspace = Path(os.path.abspath(args.pop(0)))
    node = shutil.which("node")
    if node is None:
        print("❌ node is required to sync MCP configuration.", file=sys.stderr)
        return 1
    env = dict(os.environ)
    env["DATAVIZ_MCP_WORKSPACE"] = str(workspace)
    try:
        return subprocess.run(
            [node, str(script_dir / "configure.mjs"), *args],
            cwd=workspace,
            env=env,
            check=False,
        ).returncode
    except OSError as error:
        print(f"❌ Cannot sync MCP configuration: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
