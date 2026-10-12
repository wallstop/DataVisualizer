#!/usr/bin/env python3
"""Prints whether the harness self-test suite must run for a change.

The output is ``run`` or ``skip``: whether the harness self-test suite must
execute for the changes between ``--base-sha`` and HEAD. CI's llm-lint
workflow consults this before spending its slowest step
(``scripts/tests/run_all.py``) on a change that cannot affect any self-test
outcome. With ``--selection`` the script instead prints ``all`` or the
comma-joined self-test file names that cover the changed paths, so CI can
run the same scoped selection the local fast check runs. Every verdict
fails safe: whenever the base cannot be resolved or a path has no subject
mapping, the full suite runs.

With ``--ci-verdicts`` the script prints all three ``key=value`` verdicts
the llm-lint scope step consumes in one invocation:

- ``self-tests=run|skip`` — same verdict as the default mode.
- ``selection=all|name1,name2`` — same verdict as ``--selection``.
- ``markdown=run|skip`` — whether the change can affect the checked
  Markdown formatting: any changed ``.md`` file, or the Prettier
  configuration, ignore list, or package manifest. The npm install and
  Prettier steps skip when this is ``skip``. The verdict reuses the same
  committed and working-tree scans the other two verdicts use, so a
  working-tree Markdown edit also selects it; CI's fresh checkout is
  clean at HEAD, where the behavior matches the committed-only diff CI
  used to inline.

Ported from ``scripts/should-run-self-tests.ps1`` (issue #171); the verdict
semantics are identical, only the interpreter changed.

Launch with ``uv run scripts/should_run_self_tests.py``; any Python 3.9+
interpreter works.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path
from typing import Optional, Sequence

# Importing the local scope module must not leave scripts/__pycache__ behind:
# the bytecode cache is an untracked surface path the next scan would see,
# fail safe on, and turn every subsequent run into the full suite.
sys.dont_write_bytecode = True

from harness_scope import (
    REPO_ROOT,
    available_test_files,
    committed_changed_files,
    is_surface_path,
    markdown_paths_changed,
    self_test_selection,
    working_tree_changed_files,
)

_NULL_SHA = "0000000000000000000000000000000000000000"


def resolve_base_sha(repo_root: Path, base_sha: str) -> Optional[str]:
    """Verify the base resolves to a commit; None means fail safe to the full suite."""
    if not base_sha.strip() or base_sha.strip() == _NULL_SHA:
        return None
    try:
        completed = subprocess.run(
            ["git", "-C", str(repo_root), "rev-parse", "--verify", "--quiet", f"{base_sha.strip()}^{{commit}}"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except OSError:
        return None
    resolved = (completed.stdout or "").strip() if completed.returncode == 0 else ""
    return resolved or None


def changed_paths(repo_root: Path, base_sha: str) -> Sequence[str]:
    return committed_changed_files(repo_root, base_sha) + working_tree_changed_files(repo_root)


def parse_arguments(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="should-run-self-tests",
        description="Print 'run' or 'skip' (or the scoped selection with --selection) for a change.",
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=REPO_ROOT,
        help="repository root (default: the checkout holding this script)",
    )
    parser.add_argument("--base-sha", default="", help="the base commit; empty or the null SHA fails safe to 'run'/'all'")
    parser.add_argument(
        "--selection",
        action="store_true",
        help="print 'all' or the comma-joined self-test file names instead of 'run'/'skip'",
    )
    parser.add_argument(
        "--ci-verdicts",
        action="store_true",
        help="print all three key=value CI verdicts (self-tests, selection, markdown) in one invocation",
    )
    options = parser.parse_args(argv)
    if options.ci_verdicts and options.selection:
        parser.error("--selection is implied by --ci-verdicts; pass only --ci-verdicts")
    return options


def print_ci_verdicts(repo_root: Path, base_sha: Optional[str]) -> None:
    """Print the three key=value verdicts CI's llm-lint scope step consumes.

    One invocation replaces the two script calls plus the inline diff the
    workflow used to run: the Markdown verdict reuses the same changed-path
    scans the self-tests verdict needs, so the step spawns one interpreter
    instead of two plus an extra git diff.
    """
    if base_sha is None:
        # Every verdict fails safe: an unusable base runs everything it guards.
        print("self-tests=run")
        print("selection=all")
        print("markdown=run")
        return

    paths = changed_paths(repo_root, base_sha)
    selection = self_test_selection(paths, available_test_files())
    print("self-tests=run" if any(is_surface_path(path) for path in paths) else "self-tests=skip")
    print("selection=all" if selection.all else "selection=" + ",".join(selection.names))
    print("markdown=run" if markdown_paths_changed(paths) else "markdown=skip")


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = parse_arguments(argv)
    repo_root = options.repo_root.resolve()
    base_sha = resolve_base_sha(repo_root, options.base_sha)

    if options.ci_verdicts:
        print_ci_verdicts(repo_root, base_sha)
        return 0

    # Every verdict fails safe: an unusable base always runs the full suite.
    if base_sha is None:
        print("all" if options.selection else "run")
        return 0

    paths = changed_paths(repo_root, base_sha)
    if not options.selection:
        print("run" if any(is_surface_path(path) for path in paths) else "skip")
        return 0

    selection = self_test_selection(paths, available_test_files())
    print("all" if selection.all else ",".join(selection.names))
    return 0


if __name__ == "__main__":
    sys.exit(main())
