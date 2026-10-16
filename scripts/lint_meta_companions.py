#!/usr/bin/env python3
"""Fail when Unity's .meta companions and their assets disagree.

Unity generates a ``.meta`` for every non-dot path it imports, so both
directions of drift make the host Unity rewrite GUIDs: a file or directory
committed without its companion makes Unity create one on the next import,
and an orphan companion (committed without its asset, PR #274 review) makes
Unity treat the asset as missing and delete or regenerate it. Either way the
result is a dirty checkout and GUID drift across machines (the class
documented as host churn in the agent instructions and #193). Dot folders
and dot files are never imported, so they never need companions;
``__pycache__`` content is likewise skipped, and an orphan companion whose
asset is ignored or still exists on disk is tolerated.

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
        description="Fail when Unity's .meta companions and their assets disagree.",
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


def _git_check_ignored(repo_root: Path, paths: Sequence[str]) -> List[str]:
    """Return the subset of paths git ignores (never present on a fresh checkout)."""
    if not paths:
        return []
    try:
        completed = subprocess.run(
            ["git", "-C", str(repo_root), "check-ignore", "--stdin", "-z"],
            input=("\0".join(paths) + "\0").encode("utf-8"),
            capture_output=True,
            check=False,
        )
    except OSError:
        return []
    if completed.returncode not in (0, 1):
        return []
    return [entry.decode("utf-8", errors="replace") for entry in completed.stdout.split(b"\0") if entry]


def missing_companions(paths: Sequence[str], repo_root: Path) -> List[str]:
    """Return the paths where Unity's companions and their assets disagree.

    Missing companions: every non-dot path whose ``.meta`` is absent from the
    set, with directory companions checked from every file's ancestors so a
    meta-carrying file inside a new folder still reports the folder's
    missing companion. Orphan companions: every ``.meta`` whose asset is in
    neither the set nor on disk (assets git ignores never reach a fresh
    checkout, so their companions are tolerated, like the tracked
    ``__pycache__`` folder metas).
    """
    known: Set[str] = set(paths)
    missing: Set[str] = set()
    for path in known:
        if path.endswith(".meta"):
            asset = path[: -len(".meta")]
            if asset not in known and not (repo_root / asset).exists():
                missing.add(path)
            continue
        if not is_meta_relevant(path):
            continue
        if path + ".meta" not in known:
            missing.add(path)
        parts = path.split("/")
        for index in range(1, len(parts)):
            directory = "/".join(parts[:index])
            if is_meta_relevant(directory) and directory + ".meta" not in known:
                missing.add(directory)
    # An orphan whose asset is ignored never reaches a fresh checkout, so
    # Unity cannot regenerate it there; the tracked __pycache__ folder metas
    # are the accepted precedent. Regenerated bytecode never needs a meta.
    candidates = {path for path in missing if path.endswith(".meta")}
    if candidates:
        ignored = set(_git_check_ignored(repo_root, sorted(path[: -len(".meta")] for path in candidates)))
        for path in candidates:
            asset = path[: -len(".meta")]
            if any(part == "__pycache__" for part in asset.split("/")) or asset in ignored:
                missing.discard(path)
    return sorted(missing)


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = parse_arguments(argv)
    repo_root = options.repo_root.resolve()
    tracked, untracked = repository_paths(repo_root)
    missing = missing_companions(tracked + untracked, repo_root)
    if not missing:
        print("[meta-companions] ok: every imported path carries a .meta companion")
        return 0
    for path in missing:
        if path.endswith(".meta"):
            print(f"::error::{path} is an orphan companion: its asset is committed nowhere "
                  "and does not exist on disk, so Unity deletes or regenerates it with a "
                  "fresh GUID (dirty tree, GUID drift). Remove the .meta or restore its asset.")
            continue
        print(f"::error::{path} has no committed {path}.meta companion; Unity would "
              "generate one with a fresh GUID on the host (dirty tree, GUID drift). "
              "Commit the companion (copy an existing *.meta of the same kind and "
              "replace the guid) or move the file under a dot folder.")
    print(f"::error::meta companions disagree for {len(missing)} path(s)")
    return 1


if __name__ == "__main__":
    sys.exit(main())
