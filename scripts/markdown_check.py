#!/usr/bin/env python3
"""Check the Markdown scope CI's llm-lint workflow resolves (issue #171).

The scope step (``scripts/should_run_self_tests.py``) emits
``markdown-files=all`` or a comma-joined path list that already excludes
``.prettierignore`` trees. This script runs Prettier on exactly that scope
through the local npm install, so pull requests stop re-scanning every
tracked Markdown file while push events keep the full sweep.

The path list arrives as a single argument so neither default runner shell
(bash on Linux, PowerShell on Windows) has to split anything. ``all`` - or
no list at all - fails safe to the full ``npm run format:md:check`` sweep,
the same direction as every scope verdict here.

Launch with ``uv run scripts/markdown_check.py <scope>``; any Python 3.9+
interpreter works.
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from typing import List, Optional, Sequence

FULL_SWEEP_TARGET = "format:md:check"


def resolve_executable(name: str) -> Optional[str]:
    """Resolve npm's platform launcher, or None when it is unavailable.

    ``shutil.which`` finds ``npm.cmd``/``npx.cmd`` on Windows; a bare name
    would otherwise resolve through the process search order to whatever
    the PATH happens to hold (issue #229's CreateProcess lesson).
    """
    return shutil.which(name)


def build_command(argv: Sequence[str]) -> List[str]:
    """Return the npm command for the requested scope.

    Any ``all`` scope runs the full sweep; a missing or empty scope fails
    safe to it too. Otherwise exactly one comma-joined token is expected.
    """
    tokens = [token for token in argv if token.strip()]
    if not tokens or "all" in tokens:
        return ["npm", "run", FULL_SWEEP_TARGET]
    if len(tokens) != 1:
        raise SystemExit(f"markdown scope must be 'all' or one comma-joined token, got: {tokens}")
    paths = [path for path in tokens[0].split(",") if path]
    if not paths:
        return ["npm", "run", FULL_SWEEP_TARGET]
    return ["npx", "--no-install", "prettier", "--check", *paths]


def main(argv: Sequence[str]) -> int:
    try:
        command = build_command(argv)
    except SystemExit as error:
        print(error, file=sys.stderr)
        return 2
    executable = resolve_executable(command[0])
    if executable is None:
        print(f"markdown check cannot run: {command[0]} is not on PATH", file=sys.stderr)
        return 2
    completed = subprocess.run([executable, *command[1:]], check=False)
    return completed.returncode


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
