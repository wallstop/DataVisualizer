#!/usr/bin/env python3
"""Runs the harness self-test suite, optionally narrowed by surface (issue #171).

Typed Python port of ``scripts/tests/run-all.ps1``: every self-test file
next to this script runs in a child interpreter with its output captured
and replayed in name order, so the log shape is identical to the PowerShell
runner. The suite is migrating to typed Python (issue #171): ``test_*.py``
files run under this process's own interpreter, while the not-yet-ported
``test-*.ps1`` files keep running in a child pwsh, so a tranche can port
its files without a flag day. pwsh is required only when a selected file
is still a ``.ps1``; a Python-only selection runs on any interpreter.

``--names`` takes the same comma-separated test file names the fast check's
surface selection produces; an unknown name fails safe to the full suite,
so a stale mapping or a typo can never silently run a zero-test suite.
When no test file is found the runner reports it and exits 0, like the
original.

The runner stays deliberately self-contained (one file, standard library
only) so the self-tests can copy it into a sandbox tree and exercise it
against stub suites; its discovery therefore keeps its own glob instead of
importing the scope module.

Workers fail closed: a child that cannot start, or a worker thread that
crashes, records a failed result, and the verdict verifies that every
selected file produced a result, so a resource error under suite
concurrency can never report success.

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
    """List the test file names in name order across both migration patterns.

    Typed Python files (``test_*.py``) and the remaining PowerShell files
    (``test-*.ps1``) share one name-ordered list so the suite treats them
    identically; helper modules never match either pattern by design.
    """
    names = [path.name for path in TESTS_DIR.glob("test_*.py")]
    names.extend(path.name for path in TESTS_DIR.glob("test-*.ps1"))
    return sorted(names)


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


def run_tests(selected: Sequence[str], python_executable: str, pwsh: Optional[str]) -> int:
    """Run each test in a child interpreter and replay the captured output in name order.

    ``test_*.py`` files run under ``python_executable`` (this process's own
    interpreter); ``test-*.ps1`` files run in a child pwsh, which ``main``
    guarantees is resolved whenever any selected file needs it.

    Every selected file must produce exactly one recorded result before the
    verdict is computed: a worker that crashes records a failed result, and
    a file whose result never arrives is counted as failed, so a resource
    error under suite concurrency can never report success.
    """
    results: List[Tuple[str, Optional[int], str]] = []
    lock = threading.Lock()

    def run_one(name: str) -> Tuple[str, Optional[int], str]:
        if name.endswith(".py"):
            command = [python_executable, str(TESTS_DIR / name)]
        else:
            command = [pwsh or "", "-NoProfile", "-File", str(TESTS_DIR / name)]
        completed = subprocess.run(
            command,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        output = ((completed.stdout or "") + (completed.stderr or "")).rstrip()
        return name, completed.returncode, output

    threads = [threading.Thread(target=_collect, args=(name, run_one, results, lock)) for name in selected]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()

    # Completeness belt: a selected file without a recorded result cannot
    # count as passed. The worker conversion below should make this
    # unreachable; it pins the verdict against any future gap.
    recorded = {name for name, _, _ in results}
    for name in selected:
        if name not in recorded:
            results.append((name, None, "[run-all] ERROR: no result recorded; the worker crashed before reporting"))

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
    try:
        result: Tuple[str, Optional[int], str] = run_one(name)
    except Exception as error:
        # A crashed worker (spawn failure, resource limits) must record a
        # failed result; a dead thread that records nothing would otherwise
        # let the suite report success. ``None`` counts as failed because
        # it is not the exit code of a test that ran.
        result = (name, None, f"[run-all] ERROR: could not run {name}: {error!r}")
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

    available = discover_test_files()
    if not available:
        print("No self-tests found.")
        return 0

    selected, narrowed = resolve_selection(options.names, available)
    if narrowed:
        print(f"[run-all] surface selection: {len(selected)} test file(s): {', '.join(selected)}")

    # pwsh is only a requirement of the not-yet-ported test files: a Python
    # selection must run on hosts that never had pwsh, while a mixed or
    # PowerShell selection still fails closed without it.
    pwsh: Optional[str] = None
    if any(name.endswith(".ps1") for name in selected):
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            print("[run-all] ERROR: pwsh is required to run the test files; install PowerShell 7+")
            return 1

    return run_tests(selected, sys.executable, pwsh)


if __name__ == "__main__":
    sys.exit(main())
