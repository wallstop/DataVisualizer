#!/usr/bin/env python3
"""Fail when a tracked non-dot path has no tracked .meta companion.

Unity generates a ``.meta`` for every non-dot path it imports, so a file or
directory committed without its companion makes the host Unity create one on
the next import: a fresh GUID lands in the tree, leaving a dirty checkout and
GUID drift across machines (the class documented as host churn in the agent
instructions and #193). Dot folders and dot files are never imported, so they
never need companions; ``.meta`` files and ``__pycache__`` content are
likewise skipped.

The scan is the repository's own tracked paths plus untracked non-ignored
paths, so the per-edit loop catches the mistake before the commit that CI
would see. Launch with ``uv run scripts/lint_meta_companions.py``; any
Python 3.9+ interpreter works.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path
from typing import List, Optional, Sequence, Set, Tuple

REPO_ROOT = Path(__file__).resolve().parent.parent


def parse_arguments(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="lint-meta-companions",
        description="Fail when a tracked non-dot path has no tracked .meta companion.",
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=REPO_ROOT,
        help="repository root to scan (default: the checkout holding this script)",
    )
    return parser.parse_args(argv)


def repository_paths(repo_root: Path) -> Tuple[List[str], List[str]]:
    """Return (tracked, untracked-non-ignored) paths as slash-relative strings."""
    tracked = _git_lines(repo_root, ["ls-files", "-z"])
    untracked = _git_lines(repo_root, ["ls-files", "--others", "--exclude-standard", "-z"])
    return tracked, untracked


def _git_lines(repo_root: Path, arguments: Sequence[str]) -> List[str]:
    try:
        completed = subprocess.run(
            ["git", "-C", str(repo_root), *arguments],
            capture_output=True,
            check=False,
        )
    except OSError:
        return []
    if completed.returncode != 0:
        return []
    return [entry.decode("utf-8", errors="replace") for entry in completed.stdout.split(b"\0") if entry]


def is_meta_relevant(path: str) -> bool:
    """Return True when the path is one Unity imports and therefore needs a companion for."""
    if path.startswith(".") or "/." in path:
        return False  # dot files and dot folders are never imported
    if path.endswith(".meta"):
        return False  # companions themselves carry none
    if any(part == "__pycache__" for part in path.split("/")):
        return False  # bytecode caches are regenerated, never imported assets
    return True


def missing_companions(paths: Sequence[str]) -> List[str]:
    """Return every non-dot path whose .meta companion is absent from the set.

    Directory companions are checked from every file's ancestors, so a
    meta-carrying file inside a new folder still reports the folder's missing
    companion.
    """
    known: Set[str] = set(paths)
    missing: Set[str] = set()
    for path in known:
        if not is_meta_relevant(path):
            continue
        if path + ".meta" not in known:
            missing.add(path)
        parts = path.split("/")
        for index in range(1, len(parts)):
            directory = "/".join(parts[:index])
            if is_meta_relevant(directory) and directory + ".meta" not in known:
                missing.add(directory)
    return sorted(missing)


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = parse_arguments(argv)
    repo_root = options.repo_root.resolve()
    tracked, untracked = repository_paths(repo_root)
    missing = missing_companions(tracked + untracked)
    if not missing:
        print("[meta-companions] ok: every imported path carries a .meta companion")
        return 0
    for path in missing:
        print(f"::error::{path} has no committed {path}.meta companion; Unity would "
              "generate one with a fresh GUID on the host (dirty tree, GUID drift). "
              "Commit the companion (copy an existing *.meta of the same kind and "
              "replace the guid) or move the file under a dot folder.")
    print(f"::error::meta companions missing for {len(missing)} path(s)")
    return 1


if __name__ == "__main__":
    sys.exit(main())
