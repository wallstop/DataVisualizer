#!/usr/bin/env python3
"""Direct Unity validation runner tests (typed Python port of ``test-run-unity-validation.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions;
only the driver language changed. The runner is driven as a child process:
a Python child costs a few milliseconds to start, so the in-process
invocation the PowerShell original needed to dodge expensive pwsh spawns
is no longer required, and the real CLI contract is exercised instead.
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Sequence

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_true,
    failure_count,
    new_temp_root,
    remove_temp_root,
    run_case,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
RUNNER = REPO_ROOT / "scripts" / "run_unity_validation.py"


def new_unity_project_fixture(root: Path, version: str = "6000.4.6f1") -> None:
    (root / "Assets").mkdir(parents=True, exist_ok=True)
    write_fixture_file(
        root,
        "ProjectSettings/ProjectVersion.txt",
        f"m_EditorVersion: {version}\nm_EditorVersionWithRevision: {version} (fixture)\n",
    )


def invoke_runner(
    project: Path,
    output: Path,
    version: str = "6000.4.6f1",
    size: int = 100,
    mode: str = "editmode",
    plan_only: bool = False,
) -> int:
    """Run the validation runner as a child and return its exit code.

    sys.executable is a deterministic non-Unity executable for the child:
    it starts successfully but rejects Unity's command-line switches.
    """
    arguments: Sequence[str] = [
        sys.executable,
        str(RUNNER),
        "--host-project", str(project),
        "--unity-executable", sys.executable,
        "--unity-version", version,
        "--fixture-size", str(size),
        "--suite", mode,
        "--output-directory", str(output),
    ]
    if plan_only:
        arguments = [*arguments, "--plan-only"]
    completed = subprocess.run(arguments, capture_output=True, text=True, check=False)
    return completed.returncode


def case_writes_explicit_plan_when_inputs_are_valid() -> None:
    root = new_temp_root(prefix="unity-runner-")
    try:
        project = root / "Host Project"
        output = root / "Evidence Output"
        new_unity_project_fixture(project)

        exit_code = invoke_runner(project, output, size=10000, mode="playmode", plan_only=True)
        assert_true(exit_code == 0, f"plan-only invocation should pass, got exit {exit_code}")

        manifest_path = output / "data-visualizer-validation.json"
        assert_true(manifest_path.is_file(), "plan manifest should exist")
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        assert_true(manifest["status"] == "planned", "plan status should be planned")
        assert_true(manifest["configuration"]["fixtureSize"] == 10000, "fixture size should be recorded")
        assert_true(manifest["configuration"]["suite"] == "playmode", "suite should be recorded")
        assert_true("-runTests" in manifest["command"], "Unity test argument should be present")
        assert_true(str(project) in manifest["command"], "path containing spaces should be one argument")
        assert_true(
            manifest["artifacts"]["testResults"].endswith("playmode-results.xml"),
            "result path should match the suite",
        )
    finally:
        remove_temp_root(root)


def case_rejects_unity_version_mismatch_before_writing_evidence() -> None:
    root = new_temp_root(prefix="unity-runner-")
    try:
        project = root / "HostProject"
        output = root / "Output"
        new_unity_project_fixture(project, version="2022.3.50f1")

        exit_code = invoke_runner(project, output, plan_only=True)
        assert_true(exit_code == 2, f"version mismatch should exit 2, got {exit_code}")
        assert_true(not output.exists(), "invalid run should not create output")
    finally:
        remove_temp_root(root)


def case_rejects_output_inside_package_repository() -> None:
    root = new_temp_root(prefix="unity-runner-")
    try:
        project = root / "HostProject"
        new_unity_project_fixture(project)
        inside_repo = REPO_ROOT / "Temp/forbidden-unity-validation-output"

        exit_code = invoke_runner(project, inside_repo, plan_only=True)
        assert_true(exit_code == 2, f"in-repository output should exit 2, got {exit_code}")
        assert_true(not inside_repo.exists(), "rejected output should not be created")
    finally:
        remove_temp_root(root)


def case_rejects_unsupported_fixture_size_at_parameter_binding() -> None:
    root = new_temp_root(prefix="unity-runner-")
    try:
        project = root / "HostProject"
        output = root / "Output"
        new_unity_project_fixture(project)

        exit_code = invoke_runner(project, output, size=999, plan_only=True)
        assert_true(exit_code != 0, "unsupported fixture size should fail")
        assert_true(not output.exists(), "invalid size should not create output")
    finally:
        remove_temp_root(root)


def case_preserves_process_failure_exit_code_and_records_failure() -> None:
    root = new_temp_root(prefix="unity-runner-")
    try:
        project = root / "HostProject"
        output = root / "Output"
        new_unity_project_fixture(project)

        # sys.executable is a deterministic non-Unity executable: it starts
        # successfully but rejects Unity's command-line switches, exercising
        # child-process exit propagation.
        exit_code = invoke_runner(project, output)
        assert_true(exit_code != 0, "failed child process should return a nonzero exit code")

        manifest = json.loads((output / "data-visualizer-validation.json").read_text(encoding="utf-8"))
        assert_true(manifest["status"] == "failed", "process failure should be recorded")
        assert_true(manifest["exitCode"] == exit_code, "child exit code should be preserved")
        assert_true(
            str(manifest["completedAtUtc"]).strip() != "",
            "completion time should be recorded",
        )
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== direct Unity validation runner tests ==")

    run_case("Writes_ExplicitPlan_When_InputsAreValid", case_writes_explicit_plan_when_inputs_are_valid)
    run_case("Rejects_UnityVersionMismatch_BeforeWritingEvidence", case_rejects_unity_version_mismatch_before_writing_evidence)
    run_case("Rejects_OutputInsidePackageRepository", case_rejects_output_inside_package_repository)
    run_case("Rejects_UnsupportedFixtureSize_AtParameterBinding", case_rejects_unsupported_fixture_size_at_parameter_binding)
    run_case("Preserves_ProcessFailureExitCode_AndRecordsFailure", case_preserves_process_failure_exit_code_and_records_failure)

    failures = failure_count()
    print(f"== direct Unity validation runner: {failures} failure(s) ==")
    if failures > 0:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
