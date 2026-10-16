#!/usr/bin/env python3
"""Runs the harness self-test suite, optionally narrowed by surface (issue #171).

Every ``test_*.py`` file next to this script runs in a child interpreter
with its output captured and replayed in name order. At most
``max_file_workers()`` files run at once - four on small CI runners, up to
eight on larger hosts - bounding the nested Node/Bash/Python subprocess
load. The suite is typed
Python after the issue #171 migration, so the selection runs on any
interpreter, including hosts that never had PowerShell.

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
import concurrent.futures
import os
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import Callable, List, Optional, Sequence, Tuple

TESTS_DIR = Path(__file__).resolve().parent


def max_file_workers() -> int:
    """Return the file-worker bound for this host.

    Each file spawns its own nested Node/Bash/Python children, so the floor
    of four keeps small CI runners free of nested-startup starvation and
    leaves runner scheduling unchanged. Larger hosts may overlap more of the
    cold per-interpreter I/O that dominates a full-suite run, up to a cap of
    eight so nested children always keep headroom (measured on a 12-core
    host: warm full-suite wall 34.5s at four workers, 16.6-21.6s at eight).
    """
    return max(4, min(os.cpu_count() or 4, 8))


def discover_test_files() -> List[str]:
    """List the test file names in name order.

    Helper modules never match the ``test_*.py`` pattern by design, so the
    suite can never try to execute one.
    """
    return sorted(path.name for path in TESTS_DIR.glob("test_*.py"))


def resolve_selection(names_csv: str, available: Sequence[str]) -> Tuple[List[str], bool]:
    """Resolve ``--names`` to selected files, failing safe to the full suite."""
    requested = [name.strip() for name in names_csv.split(",") if name.strip()]
    if not requested or requested == ["all"]:
        return list(available), False
    unknown = [name for name in requested if name not in available]
    if unknown:
        print(f"[run-all] unknown test file(s) requested: {', '.join(unknown)}; running the full suite.")
        return list(available), False
    return requested, True


def run_tests(selected: Sequence[str], python_executable: str) -> int:
    """Run each test in a child interpreter and replay the captured output in name order.

    Every selected file must produce exactly one recorded result before the
    verdict is computed: a worker that crashes records a failed result, and
    a file whose result never arrives is counted as failed, so a resource
    error under suite concurrency can never report success.
    """
    results: List[Tuple[str, Optional[int], str]] = []
    lock = threading.Lock()
    startup_ready = threading.Event()
    startup_file = "test_unity_mcp_host.py"
    coordinate_startup = sys.platform.startswith("linux") and startup_file in selected
    startup_marker = f"[run-all-ready:{os.getpid()}:{time.perf_counter_ns()}]"

    def run_one(name: str) -> Tuple[str, Optional[int], str]:
        if coordinate_startup and name == startup_file:
            try:
                environment = dict(os.environ, DATAVIZ_HARNESS_STARTUP_MARKER=startup_marker)
                with subprocess.Popen(
                    [python_executable, str(TESTS_DIR / name)],
                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                    text=True, encoding="utf-8", errors="replace", env=environment,
                ) as child:
                    if child.stdout is None:
                        raise RuntimeError("Bridge startup output is unavailable.")
                    lines: List[str] = []
                    for line in child.stdout:
                        if line.rstrip("\r\n") == startup_marker:
                            startup_ready.set()
                        else:
                            lines.append(line)
                    return name, child.wait(), "".join(lines).rstrip()
            finally:
                # A failed or skipped startup must still release the other files.
                startup_ready.set()
        completed = subprocess.run(
            [python_executable, str(TESTS_DIR / name)],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        output = ((completed.stdout or "") + (completed.stderr or "")).rstrip()
        return name, completed.returncode, output

    # Linux measurements favor quiet bridge startup followed by overlapping I/O.
    # Other platforms retain their existing launch path and input ordering.
    queued = list(selected)
    # Each file can spawn its own subprocesses. Bound file concurrency so
    # cold Node/Bash startups are not starved on the Windows CI runner;
    # ``max_file_workers`` keeps the runner-sized floor at four.
    with concurrent.futures.ThreadPoolExecutor(max_workers=max_file_workers()) as executor:
        futures: List[concurrent.futures.Future[None]] = []
        if coordinate_startup:
            queued.remove(startup_file)
            futures.append(executor.submit(_collect, startup_file, run_one, results, lock))
            startup_ready.wait()
            queued.sort(key=lambda name: name != "test_validate_unitypackage.py")
        futures.extend(executor.submit(_collect, name, run_one, results, lock) for name in queued)
        for future in futures:
            future.result()

    # Completeness belt: a selected file without a recorded result cannot
    # count as passed. The worker conversion below should make this
    # unreachable; it pins the verdict against any future gap.
    recorded = {name for name, _, _ in results}
    for name in selected:
        if name not in recorded:
            results.append((name, None, "[run-all] ERROR: no result recorded; the worker crashed before reporting"))

    failed = 0
    failed_names: List[str] = []
    for name, exit_code, output in sorted(results):
        print(f"\nRunning {name}...")
        print(output)
        if exit_code != 0:
            failed += 1
            failed_names.append(name)

    print()
    if failed > 0:
        print(f"Self-tests: {failed} of {len(selected)} test file(s) failed.")
        """
            Name the failed files in the summary block: a full-suite run on a
            shared-surface change produces a log whose head is truncated, and the
            failing file's own output can be cut off with it, while this final
            line always survives.
        """
        print(f"Failing file(s): {', '.join(failed_names)}")
        return 1
    print(f"Self-tests: all {len(selected)} test file(s) passed.")
    return 0


def _collect(
    name: str,
    run_one: Callable[[str], Tuple[str, Optional[int], str]],
    results: List[Tuple[str, Optional[int], str]],
    lock: threading.Lock,
) -> None:
    started = time.perf_counter()
    try:
        result: Tuple[str, Optional[int], str] = run_one(name)
    except Exception as error:
        # A crashed worker (spawn failure, resource limits) must record a
        # failed result; a dead thread that records nothing would otherwise
        # let the suite report success. ``None`` counts as failed because
        # it is not the exit code of a test that ran.
        result = (name, None, f"[run-all] ERROR: could not run {name}: {error!r}")
    elapsed = time.perf_counter() - started
    result_name, exit_code, output = result
    result = (result_name, exit_code, f"{output}\n[run-all] {name}: {elapsed:.3f}s")
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
        help="comma-separated test file names; empty or all runs everything",
    )
    options = parser.parse_args(argv)

    available = discover_test_files()
    if not available:
        print("No self-tests found.")
        return 0

    selected, narrowed = resolve_selection(options.names, available)
    if narrowed:
        print(f"[run-all] surface selection: {len(selected)} test file(s): {', '.join(selected)}")

    return run_tests(selected, sys.executable)


if __name__ == "__main__":
    sys.exit(main())
