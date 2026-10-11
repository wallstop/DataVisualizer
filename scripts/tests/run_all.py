#!/usr/bin/env python3
"""Runs the harness self-test suite, optionally narrowed by surface (issue #171).

Typed Python port of ``scripts/tests/run-all.ps1``: every ``test-*.ps1``
file next to this script runs in a child pwsh with its output captured and
replayed in name order, so the log shape is identical to the PowerShell
runner. ``--names`` takes the same comma-separated test file names the fast
check's surface selection produces; an unknown name fails safe to the full
suite, so a stale mapping or a typo can never silently run a zero-test
suite. When no test file is found the runner reports it and exits 0, like
the original.

The child launches stay pwsh because the test files are PowerShell; this
slice removes the parent pwsh (and its startup) from the hot path. The test
files themselves are the remaining surface for this issue.

Launch with ``python scripts/tests/run_all.py`` (or ``uv run
scripts/tests/run_all.py``); any Python 3.9+ interpreter works.
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import threading
from pathlib import Path
from typing import Callable, List, Optional, Sequence, Tuple

TESTS_DIR = Path(__file__).resolve().parent


def discover_test_files() -> List[str]:
    """List the test file names in name order."""
    return sorted(path.name for path in TESTS_DIR.glob("test-*.ps1"))


def resolve_selection(names_csv: str, available: Sequence[str]) -> Tuple[List[str], bool]:
    """Resolve ``--names`` to selected files, failing safe to the full suite."""
    requested = [name.strip() for name in names_csv.split(",") if name.strip()]
    if not requested:
        return list(available), False
    unknown = [name for name in requested if name not in available]
    if unknown:
        print(f"[run-all] unknown test file(s) requested: {', '.join(unknown)}; running the full suite.")
        return list(available), False
    return requested, True


def run_tests(selected: Sequence[str], pwsh: str) -> int:
    """Run each test in a child pwsh and replay the captured output in name order."""
    results: List[Tuple[str, Optional[int], str]] = []
    lock = threading.Lock()

    def run_one(name: str) -> Tuple[str, Optional[int], str]:
        completed = subprocess.run(
            [pwsh, "-NoProfile", "-File", str(TESTS_DIR / name)],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
            cwd=str(TESTS_DIR.parent.parent),
        )
        output = ((completed.stdout or "") + (completed.stderr or "")).rstrip()
        return name, completed.returncode, output

    threads = [threading.Thread(target=_collect, args=(name, run_one, results, lock)) for name in selected]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()

    failed = 0
    for name, exit_code, output in sorted(results):
        print(f"\nRunning {name}...")
        print(output)
        if exit_code != 0:
            failed += 1

    print()
    if failed > 0:
        print(f"Self-tests: {failed} of {len(selected)} test file(s) failed.")
        return 1
    print(f"Self-tests: all {len(selected)} test file(s) passed.")
    return 0


def _collect(
    name: str,
    run_one: Callable[[str], Tuple[str, Optional[int], str]],
    results: List[Tuple[str, Optional[int], str]],
    lock: threading.Lock,
) -> None:
    result: Tuple[str, Optional[int], str] = run_one(name)
    with lock:
        results.append(result)


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="run-all",
        description="Run the harness self-test suite, optionally narrowed to named test files.",
    )
    parser.add_argument(
        "--names",
        default="",
        help="comma-separated test file names; empty runs everything",
    )
    options = parser.parse_args(argv)

    pwsh = shutil.which("pwsh")
    if pwsh is None:
        print("[run-all] ERROR: pwsh is required to run the test files; install PowerShell 7+")
        return 1

    available = discover_test_files()
    if not available:
        print("No self-tests found.")
        return 0

    selected, narrowed = resolve_selection(options.names, available)
    if narrowed:
        print(f"[run-all] surface selection: {len(selected)} test file(s): {', '.join(selected)}")
    return run_tests(selected, pwsh)


if __name__ == "__main__":
    sys.exit(main())
