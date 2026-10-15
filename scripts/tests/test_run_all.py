#!/usr/bin/env python3
"""Runner contract self-tests (typed Python port of ``test-run-all.ps1``, issue #171).

The runner discovers ``test_*.py`` files, runs each in a child
interpreter, and fails safe on unknown names; these cases pin that
contract against sandbox trees with stub suites. The spawned-worker path
stays fail closed: a worker that cannot start records a failed result
and can never let the verdict report success.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from typing import Sequence, Tuple

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    failure_count,
    find_python,
    new_temp_root,
    remove_temp_root,
    run_case,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
RUNNER_SOURCE = TESTS_DIR / "run_all.py"


def new_runner_fixture() -> Path:
    """Copy the runner beside two stub suites, one passing and one failing."""
    root = new_temp_root(prefix="run-all-")
    tests_directory = root / "scripts" / "tests"
    tests_directory.mkdir(parents=True)
    (tests_directory / "run_all.py").write_text(RUNNER_SOURCE.read_text(encoding="utf-8"), encoding="utf-8")
    write_fixture_file(root, "scripts/tests/test_alpha_passes.py", "raise SystemExit(0)\n")
    write_fixture_file(root, "scripts/tests/test_zulu_fails.py", "raise SystemExit(1)\n")
    return root


def invoke_runner(root: Path, python: str, names: Sequence[str]) -> Tuple[str, int]:
    """Run the sandboxed runner with an optional name selection."""
    arguments = [str(root / "scripts" / "tests" / "run_all.py")]
    if names:
        arguments.extend(["--names", ",".join(names)])
    completed = subprocess.run(
        [python, *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    output = ((completed.stdout or "") + (completed.stderr or "")).rstrip()
    return output, completed.returncode


def case_names_run_only_the_selected_files(python: str) -> None:
    root = new_runner_fixture()
    try:
        output, exit_code = invoke_runner(root, python, ["test_alpha_passes.py"])
        assert_exit_code(0, "the passing selected suite should pass", exit_code)
        assert_true(
            "surface selection: 1 test file(s): test_alpha_passes.py" in output,
            f"the runner should report the selection, got: {output}",
        )
        assert_true(
            "all 1 test file(s) passed" in output,
            f"the runner should count only the selected file, got: {output}",
        )
        assert_true("test_zulu_fails" not in output, "the unselected failing suite must not run")
    finally:
        remove_temp_root(root)


def case_unknown_names_fail_safe_to_the_full_suite(python: str) -> None:
    root = new_runner_fixture()
    try:
        output, exit_code = invoke_runner(root, python, ["test_alpha_passes.py", "test-missing.py"])
        assert_exit_code(1, "the full suite must run when a requested name is unknown, so the failing stub fails", exit_code)
        assert_true(
            "unknown test file(s) requested: test-missing.py" in output,
            f"the runner should name the unknown selection, got: {output}",
        )
        assert_true(
            "1 of 2 test file(s) failed" in output,
            "the fallback must run every discovered file, so the failing stub is caught",
        )
    finally:
        remove_temp_root(root)


def case_empty_names_run_everything(python: str) -> None:
    root = new_runner_fixture()
    try:
        for names in ([], ["all"]):
            output, exit_code = invoke_runner(root, python, names)
            assert_exit_code(1, "the full selection must run the failing stub", exit_code)
            assert_true("1 of 2 test file(s) failed" in output, f"both stub files must run, got: {output}")
            assert_true("surface selection" not in output, "a full selection must not report a narrowed run")
            assert_true("unknown test file" not in output, "all is an explicit full-suite sentinel")
    finally:
        remove_temp_root(root)


# A worker that cannot start a child must record a failure, not vanish.
def case_spawn_failure_counts_as_failed(python: str) -> None:
    root = new_runner_fixture()
    driver = "\n".join(
        [
            "import os, subprocess, sys",
            'tests_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "scripts", "tests")',
            "sys.path.insert(0, tests_dir)",
            "import run_all",
            "def raise_spawn(*args, **kwargs):",
            '    raise OSError("spawn failed (fixture)")',
            "subprocess.run = raise_spawn",
            'sys.exit(run_all.main(["--names", "test_alpha_passes.py"]))',
        ]
    )
    write_fixture_file(root, "spawn-failure-driver.py", driver + "\n")
    try:
        completed = subprocess.run(
            [python, str(root / "spawn-failure-driver.py")],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        output = ((completed.stdout or "") + (completed.stderr or "")).rstrip()
        assert_exit_code(1, "a crashed worker must fail the run", completed.returncode)
        assert_true(
            "spawn failed (fixture)" in output,
            f"the failure must carry the worker's reason, got: {output}",
        )
        assert_true(
            "1 of 1 test file(s) failed" in output,
            f"the crashed suite must count as failed, got: {output}",
        )
        assert_true(
            "all 1 test file(s) passed" not in output,
            f"a crashed worker must never report success, got: {output}",
        )
    finally:
        remove_temp_root(root)


def case_bounds_parallel_files(python: str) -> None:
    root = new_runner_fixture()
    driver = """import subprocess, sys, threading, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent / "scripts" / "tests"))
import run_all
lock = threading.Lock()
active = peak = completed = 0
def probe(arguments, **kwargs):
    global active, peak, completed
    with lock:
        active += 1
        peak = max(peak, active)
    time.sleep(0.05)
    with lock:
        active -= 1
        completed += 1
    return subprocess.CompletedProcess(arguments, 0, stdout="passed", stderr="")
run_all.subprocess.run = probe
assert run_all.run_tests([f"test_{index}.py" for index in range(12)], sys.executable) == 0
assert completed == 12, completed
assert peak <= 4, peak
"""
    write_fixture_file(root, "workers.py", driver)
    try:
        completed = subprocess.run([python, str(root / "workers.py")], capture_output=True, text=True, check=False)
        assert_exit_code(0, f"file workers must stay bounded and complete every file: {completed.stderr}", completed.returncode)
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== run-all self-tests ==")

    # The runner is typed Python (issue #171); the tests only need a working
    # interpreter because the stub suites never touch a toolchain.
    python = find_python()

    run_case("Names_RunsOnlyTheSelectedFiles", lambda: case_names_run_only_the_selected_files(python))
    run_case("UnknownNames_FailSafeToTheFullSuite", lambda: case_unknown_names_fail_safe_to_the_full_suite(python))
    run_case("EmptyNames_RunEverything", lambda: case_empty_names_run_everything(python))
    run_case("Concurrency_BoundsFileWorkers", lambda: case_bounds_parallel_files(python))
    run_case("SpawnFailure_CountsAsFailed", lambda: case_spawn_failure_counts_as_failed(python))

    failures = failure_count()
    if failures > 0:
        print(f"run-all self-tests: {failures} failed")
        return 1
    print("run-all self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
