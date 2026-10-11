#!/usr/bin/env python3
"""Runner contract self-tests (typed Python port of ``test-run-all.ps1``, issue #171).

The runner discovers ``test_*.py`` beside the remaining ``test-*.ps1``
files, runs each in a child interpreter, and fails safe on unknown names;
these cases pin that contract against sandbox trees with stub suites. The
PowerShell originals' cases are kept case-for-case, with the pwsh
requirement now scoped to selections that actually contain a ``.ps1`` file:
a Python-only selection must run on hosts that never had pwsh, and a
PowerShell selection must still fail closed without it.
"""

from __future__ import annotations

import os
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


def invoke_runner_with_path(root: Path, python: str, names: Sequence[str]) -> Tuple[str, int]:
    """Run the sandboxed runner with an empty PATH, as the stripped-PATH cases do."""
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
        env=dict(os.environ, PATH=""),
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
        output, exit_code = invoke_runner(root, python, [])
        assert_exit_code(1, "the full suite must run without a selection, so the failing stub fails", exit_code)
        assert_true("surface selection" not in output, "an empty selection must not report a narrowed run")
        assert_true("1 of 2 test file(s) failed" in output, f"both stub files must run, got: {output}")
    finally:
        remove_temp_root(root)


# A worker that cannot start a child must record a failure, not vanish: the
# pwsh runner failed closed on a spawn error and the port must too.
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


# Without pwsh the runner cannot run a PowerShell test file and must fail
# closed instead of reporting an empty pass (mirrors the gate's
# stripped-PATH case).
def case_missing_pwsh_fails_closed(python: str) -> None:
    root = new_temp_root(prefix="run-all-")
    tests_directory = root / "scripts" / "tests"
    tests_directory.mkdir(parents=True)
    (tests_directory / "run_all.py").write_text(RUNNER_SOURCE.read_text(encoding="utf-8"), encoding="utf-8")
    write_fixture_file(root, "scripts/tests/test-powershell-suite.ps1", "exit 0\n")
    try:
        output, exit_code = invoke_runner_with_path(root, python, ["test-powershell-suite.ps1"])
        assert_exit_code(1, "a missing pwsh must fail the run", exit_code)
        assert_true(
            "pwsh is required" in output,
            f"the error must name the missing requirement, got: {output}",
        )
        assert_true("passed" not in output, "a run that started nothing must not report success")
    finally:
        remove_temp_root(root)


# The pwsh requirement is scoped to the files that need it: a Python-only
# selection runs on hosts that never had pwsh.
def case_missing_pwsh_still_runs_python_suites(python: str) -> None:
    root = new_runner_fixture()
    try:
        output, exit_code = invoke_runner_with_path(root, python, ["test_alpha_passes.py"])
        assert_exit_code(0, "a Python-only selection must run without pwsh", exit_code)
        assert_true(
            "all 1 test file(s) passed" in output,
            f"the Python selection must pass without pwsh, got: {output}",
        )
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
    run_case("SpawnFailure_CountsAsFailed", lambda: case_spawn_failure_counts_as_failed(python))
    run_case("MissingPwsh_FailsClosedForPowerShellSuites", lambda: case_missing_pwsh_fails_closed(python))
    run_case("MissingPwsh_StillRunsPythonSuites", lambda: case_missing_pwsh_still_runs_python_suites(python))

    failures = failure_count()
    if failures > 0:
        print(f"run-all self-tests: {failures} failed")
        return 1
    print("run-all self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
