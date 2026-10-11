#!/usr/bin/env python3
"""Enforces the .llm and planning-doc line limits (issue #171).

Typed Python port of ``scripts/lint-file-lengths.ps1``: every markdown file
under ``.llm`` stays within the 300-line hard limit (warn near 280), the
generated index is exempt, and PLAN.md/GOAL.md stay within the 150-line
planning limit (warn near 120). ``--paths`` scopes the run to named .md
files under .llm; absent planning docs are skipped because they are
gitignored.

Launch with ``uv run scripts/lint_file_lengths.py``; any Python 3.9+
interpreter works.
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path
from typing import List, Optional, Sequence

# Importing the shared config must not leave scripts/__pycache__ behind:
# the bytecode cache is an untracked surface path the next scan would see,
# fail safe on, and turn every subsequent run into the full suite.
sys.dont_write_bytecode = True

from llm_config import (
    MAX_FILE_LINES,
    MAX_PLANNING_DOC_LINES,
    PLANNING_DOC_PATHS,
    SKILLS_INDEX_FILE_NAME,
    SKILLS_RELATIVE_PATH,
    WARN_FILE_LINES,
    WARN_PLANNING_DOC_LINES,
    physical_line_count,
    read_utf8_text,
    resolve_llm_root,
)


def _display_path(full_path: Path, repo_root: Path) -> str:
    """Show a checked file as a repository-relative, forward-slash path."""
    return str(full_path)[len(str(repo_root)):].replace(os.sep, "/").lstrip("/")


def _is_under_llm_directory(full_path: Path, llm_path: Path) -> bool:
    """Return whether a checked path sits inside the .llm directory (case-insensitive)."""
    full = str(full_path).lower()
    llm = str(llm_path).lower()
    return full == llm or full.startswith(llm + os.sep)


def _collect_targets(repo_root: Path, llm_path: Path, paths: Sequence[str]) -> Optional[List[Path]]:
    """Resolve the checked file list from explicit paths or the .llm tree.

    Explicit-path validation failures print their error and return ``None``,
    which the caller turns into the same immediate exit the PowerShell
    original performed.
    """
    targets: List[Path] = []
    if paths:
        for relative_path in paths:
            full_path = Path(os.path.abspath(os.path.join(str(repo_root), relative_path)))
            if not _is_under_llm_directory(full_path, llm_path):
                print(f"[file-length] ERROR: path '{relative_path}' is outside the .llm directory")
                return None
            if not full_path.is_file():
                print(f"[file-length] ERROR: path '{relative_path}' does not exist")
                return None
            if not full_path.name.lower().endswith(".md"):
                print(f"[file-length] ERROR: path '{relative_path}' is not a markdown file")
                return None
            targets.append(full_path)
        return targets

    if not llm_path.exists():
        print("[file-length] no .llm directory found; checking planning docs only")
        return targets
    for directory, subdirectories, files in os.walk(llm_path):
        subdirectories[:] = [name for name in subdirectories if not name.startswith(".")]
        for name in files:
            if name.endswith(".md") and not name.startswith("."):
                targets.append(Path(directory) / name)
    return targets


def run_lint(repo_root: Path, paths: Sequence[str], verbose: bool) -> int:
    """Check every target against the line limits and return the exit code."""
    llm_path = repo_root / ".llm"
    targets = _collect_targets(repo_root, llm_path, paths)
    if targets is None:
        return 1

    generated_index_path = repo_root / SKILLS_RELATIVE_PATH / SKILLS_INDEX_FILE_NAME
    errors = 0
    warnings = 0
    for target in sorted(targets, key=str):
        if target == generated_index_path:
            continue
        count = physical_line_count(read_utf8_text(target))
        display_path = _display_path(target, repo_root)
        if count > MAX_FILE_LINES:
            print(
                f"[file-length] ERROR: {display_path} has {count} lines and exceeds the "
                f"{MAX_FILE_LINES}-line hard limit; split the file"
            )
            errors += 1
        elif count >= WARN_FILE_LINES:
            if verbose:
                print(
                    f"[file-length] WARNING: {display_path} has {count} lines and is near the "
                    f"{MAX_FILE_LINES}-line hard limit; consider splitting"
                )
            warnings += 1

    for plan_doc in PLANNING_DOC_PATHS:
        plan_path = repo_root / plan_doc
        if not plan_path.is_file():
            continue
        count = physical_line_count(read_utf8_text(plan_path))
        if count > MAX_PLANNING_DOC_LINES:
            print(
                f"[file-length] ERROR: {plan_doc} has {count} lines and exceeds the "
                f"{MAX_PLANNING_DOC_LINES}-line planning-doc hard limit; keep only current and "
                f"future work here and relocate history to progress/ and context to .llm/"
            )
            errors += 1
        elif count >= WARN_PLANNING_DOC_LINES:
            if verbose:
                print(
                    f"[file-length] WARNING: {plan_doc} has {count} lines and is near the "
                    f"{MAX_PLANNING_DOC_LINES}-line planning-doc hard limit; consider splitting"
                )
            warnings += 1

    if errors > 0:
        print(f"[file-length] FAILED: {errors} file(s) exceed the line-length hard limit")
        return 1
    if verbose and warnings > 0:
        print(f"[file-length] OK: all checked files are within the line-length hard limits ({warnings} file(s) near a limit)")
    else:
        print("[file-length] OK: all checked files are within the line-length hard limits")
    return 0


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="llm-file-lengths",
        description="Enforce the .llm and planning-doc line limits.",
    )
    parser.add_argument("--root", default="", help="repository root (default: the checkout holding this script)")
    parser.add_argument(
        "--paths",
        nargs="*",
        default=[],
        help="optional .md files under .llm to check instead of the whole tree",
    )
    parser.add_argument("--verbose-output", action="store_true", help="report near-limit warnings")
    options = parser.parse_args(argv)

    # PowerShell string-to-string[] conversion splits comma-joined values;
    # accept both '--paths a,b' and '--paths a b'.
    paths: List[str] = [part for raw in options.paths for part in raw.split(",") if part]

    try:
        repo_root = resolve_llm_root(options.root)
    except ValueError as error:
        print(f"[file-length] ERROR: {error}")
        return 1
    try:
        return run_lint(repo_root, paths, options.verbose_output)
    except (OSError, UnicodeDecodeError) as error:
        print(f"[file-length] ERROR: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
