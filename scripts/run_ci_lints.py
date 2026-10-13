#!/usr/bin/env python3
"""Run the ordered CI lint ladder without a Python process per check."""
from __future__ import annotations

import os
import runpy
import subprocess
import sys
from pathlib import Path
from typing import Optional, Sequence, Tuple

REPO_ROOT = Path(__file__).resolve().parent.parent
COMMANDS: Tuple[Tuple[str, Tuple[str, ...]], ...] = (
    ('lint_llm_instructions.py', ('--verbose-output',)),
    ('lint_csharp_member_order.py', ('--verbose',)),
    ('lint_csharp_null_assertions.py', ('--verbose',)),
    ('lint_csharp_usings.py', ('--verbose',)),
    ('lint_release_secrets.py', ('--verbose',)),
    ('lint_line_endings.py', ('--verbose',)),
    ('lint_changelog_length.py', ('--verbose',)),
    ('lint_assembly_warnings.py', ()),
    ('lint_file_lengths.py', ('--verbose-output',)),
    ('generate_skills_index.py', ()),
)


def run_script(path: Path, arguments: Sequence[str]) -> int:
    previous_argv = sys.argv
    previous_path = list(sys.path)
    try:
        sys.argv = [str(path), *arguments]
        sys.path.insert(0, str(path.parent))
        try:
            runpy.run_path(str(path), run_name='__main__')
        except SystemExit as error:
            if error.code is None:
                return 0
            if isinstance(error.code, int):
                return error.code
            print(error.code, file=sys.stderr)
            return 1
        return 0
    finally:
        sys.argv = previous_argv
        sys.path[:] = previous_path


def run_ladder(root: Path) -> int:
    previous_directory = Path.cwd()
    try:
        os.chdir(root)
        for name, arguments in COMMANDS:
            code = run_script(root / 'scripts' / name, arguments)
            if code != 0:
                return code
        # Keep the same Git drift check, including staged and untracked changes.
        status = subprocess.check_output(
            ['git', 'status', '--porcelain', '--', '.llm/skills/index.md'],
            text=True, encoding='utf-8',
        ).strip()
        if status:
            print('::error::.llm/skills/index.md is stale; run uv run scripts/generate_skills_index.py and commit the result.')
            print(status)
            return 1
        return 0
    finally:
        os.chdir(previous_directory)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    # The workflow has no per-lint switches; reject extras instead of skipping checks.
    options = list(sys.argv[1:] if arguments is None else arguments)
    if options:
        print('Usage: uv run scripts/run_ci_lints.py', file=sys.stderr)
        return 2
    return run_ladder(REPO_ROOT)


if __name__ == '__main__':
    sys.exit(main())
