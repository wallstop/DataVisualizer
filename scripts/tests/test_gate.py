#!/usr/bin/env python3
"""Gate plan self-tests (typed Python port of ``test-gate.ps1``, issue #171).

The plan is the gate's contract: every check the ladder requires appears
exactly once, with its command and dependencies, and nothing executes.
Every case from the PowerShell original is kept with the same assertions;
only the driver language changed.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

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
REPO_ROOT = TESTS_DIR.parent.parent
GATE_PATH = REPO_ROOT / "scripts" / "gate.py"


def invoke_gate(python: str, arguments: Sequence[str]) -> Tuple[str, int]:
    """Run the gate with the given arguments and return (merged output, exit code)."""
    completed = subprocess.run(
        [python, str(GATE_PATH), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return (completed.stdout or "") + (completed.stderr or ""), completed.returncode


def load_plan(output: str) -> Dict[str, Any]:
    """Parse the plan JSON the gate prints on stdout."""
    return json.loads(output)


def lane_names(plan: Dict[str, Any]) -> List[str]:
    return [lane["name"] for lane in plan["lanes"]]


def step_names(plan: Dict[str, Any]) -> List[str]:
    return [step["name"] for lane in plan["lanes"] for step in lane["steps"]]


def case_plan_prints_the_full_lane_table(python: str) -> None:
    output, exit_code = invoke_gate(python, ["--plan"])
    assert_exit_code(0, f"the plan must exit 0 (got {exit_code}): {output}", exit_code)
    plan = load_plan(output)
    assert_true(plan["schema"] == 1, f"the plan must carry schema 1, got {plan['schema']}")
    names = lane_names(plan)
    assert_true(
        ",".join(names) == "csharp,markdown,assembly,harness,tests,pack",
        f"the plan must list the six lanes in order, got: {','.join(names)}",
    )
    steps = step_names(plan)
    assert_true(len(steps) == 16, f"the plan must list 16 steps, got {len(steps)}")
    assert_true(len(steps) == len(set(steps)), "step names must be unique")
    for name in steps:
        assert_true(
            re.fullmatch(r"[a-z]+\.[a-z-]+", name) is not None,
            f"step names must be lane-qualified, got: {name}",
        )


def case_plan_carries_commands_and_dependencies(python: str) -> None:
    output, _ = invoke_gate(python, ["--plan"])
    plan = load_plan(output)
    steps: Dict[str, Dict[str, Any]] = {}
    for lane in plan["lanes"]:
        for step in lane["steps"]:
            assert_true(len(step["args"]) >= 1, f"step {step['name']} must carry a command")
            assert_true(step["title"], f"step {step['name']} must carry a title")
            steps[step["name"]] = step
    assert_true(
        ",".join(steps["csharp.member-order"]["after"]) == "csharp.format",
        "the member-order lint must run after the CSharpier format",
    )
    assert_true(
        ",".join(steps["harness.skills-index"]["after"]) == "markdown.format",
        "the harness lane must wait for Markdown formatting, which rewrites .llm files",
    )
    assert_true(
        ",".join(steps["harness.skills-index-drift"]["after"]) == "harness.skills-index",
        "the drift check must run after the index regeneration",
    )
    for dependency in steps["harness.skills-index-drift"]["after"]:
        assert_true(dependency in steps, f"dependency {dependency} must exist")


def case_plan_marks_the_drift_check_as_an_empty_output_check(python: str) -> None:
    output, _ = invoke_gate(python, ["--plan"])
    plan = load_plan(output)
    checks: Dict[str, Optional[str]] = {}
    for lane in plan["lanes"]:
        for step in lane["steps"]:
            checks[step["name"]] = step["check"]
    assert_true(
        checks["harness.skills-index-drift"] == "empty-output",
        "the skills index drift check must require empty output",
    )
    with_checks = [name for name, check in checks.items() if check and name != "harness.skills-index-drift"]
    assert_true(
        len(with_checks) == 0,
        f"only the drift check may carry a check, got: {','.join(with_checks)}",
    )


def case_plan_resolves_every_program(python: str) -> None:
    output, _ = invoke_gate(python, ["--plan"])
    plan = load_plan(output)
    assert_true(
        len(plan["unresolved_programs"]) == 0,
        f"every step program must resolve on PATH, unresolved: {','.join(plan['unresolved_programs'])}",
    )


# --only re-runs a failed step without retyping the lane: the requested step
# plus every transitive dependency, and nothing else.
def case_only_selects_the_requested_steps_with_dependencies(python: str) -> None:
    output, exit_code = invoke_gate(python, ["--plan", "--only", "harness.skills-index-drift"])
    assert_exit_code(0, f"the plan must exit 0 (got {exit_code}): {output}", exit_code)
    plan = load_plan(output)
    selected = step_names(plan)
    assert_true(
        ",".join(selected) == "markdown.format,harness.skills-index,harness.skills-index-drift",
        f"the selection must keep the requested step and its dependencies, got: {','.join(selected)}",
    )


def case_only_fails_on_unknown_step_names(python: str) -> None:
    output, exit_code = invoke_gate(python, ["--only", "bogus.step"])
    assert_exit_code(2, f"an unknown step must exit 2 (got {exit_code}): {output}", exit_code)
    assert_true("bogus.step" in output, "the error must name the unknown step")


# The gate orchestrates the non-Unity ladder; the Unity suites are a separate
# host-dependent gate and must never appear in the step table.
def case_plan_excludes_the_unity_suites(python: str) -> None:
    output, _ = invoke_gate(python, ["--plan"])
    plan = load_plan(output)
    commands = [" ".join(step["args"]) for lane in plan["lanes"] for step in lane["steps"]]
    unity_commands = [command for command in commands if any(mark in command for mark in ("runTests", "testPlatform", "-runTests"))]
    assert_true(len(unity_commands) == 0, f"no step may launch a Unity suite, got: {'; '.join(unity_commands)}")


# A failed step must never report without a reason: a launch failure reaches
# the console and the report JSON, and an unusable git in the environment
# block degrades to null instead of crashing the gate (PR #172 review).
def case_launch_failure_surfaces_the_reason_in_console_and_report(python: str) -> None:
    temp_root = new_temp_root(prefix="gate-launchfail-")
    report_path = temp_root / "report.json"
    output, exit_code = invoke_gate_stripped_path(python, ["--only", "csharp.tool-restore", "--report", str(report_path)])
    assert_exit_code(1, f"a failed launch must exit 1 (got {exit_code}): {output}", exit_code)
    assert_true(
        "program not found on PATH: 'dotnet'" in output,
        f"the console must name the missing program: {output}",
    )
    assert_true("Traceback" not in output, f"a launch failure must not crash with a traceback: {output}")
    assert_true(report_path.exists(), "the report must still be written after a launch failure")
    report = json.loads(report_path.read_text(encoding="utf-8"))
    step = report["steps"][0]
    assert_true(step["ok"] is False, "the launch-failed step must be recorded as failed")
    assert_true(step["exit_code"] is None, f"a step that never ran must have no exit code, got {step['exit_code']}")
    assert_true("dotnet" in step["failure"], "the report must carry the failure reason")
    assert_true(report["environment"]["git_head"] is None, "an unusable git must degrade to a null git head")
    remove_temp_root(temp_root)


def invoke_gate_stripped_path(python: str, arguments: Sequence[str]) -> Tuple[str, int]:
    """Run the gate with an empty PATH so every step launch fails."""
    completed = subprocess.run(
        [python, str(GATE_PATH), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
        env=dict(os.environ, PATH=""),
    )
    return (completed.stdout or "") + (completed.stderr or ""), completed.returncode


# An unwritable report is a visible error, not a crash that drowns the gate
# in a traceback or changes its verdict. The stripped PATH keeps every step
# unspawned so the case costs interpreter time only (no toolchain on CI).
def case_report_write_failure_is_visible_without_crashing_the_gate(python: str) -> None:
    temp_root = new_temp_root(prefix="gate-reportfail-")
    report_path = temp_root / "missing" / "report.json"
    try:
        output, exit_code = invoke_gate_stripped_path(python, ["--only", "csharp.tool-restore", "--report", str(report_path)])
    finally:
        remove_temp_root(temp_root)
    assert_exit_code(1, f"the step failure must set the verdict (got {exit_code}): {output}", exit_code)
    assert_true(
        "[gate] report FAILED:" in output,
        f"the console must report the unwritable report path: {output}",
    )
    assert_true(
        "program not found on PATH: 'dotnet'" in output,
        f"the step failure reason must stay visible: {output}",
    )
    assert_true("Traceback" not in output, f"a report write failure must not crash: {output}")


# A lane thread that dies without recording must fail the gate AND surface
# the belt's reason on the console, not only in the report JSON: the belt is
# itself a failure path. KeyboardInterrupt escapes the lane's ``except
# Exception`` guard, so the patched step reaches the belt unrecorded.
def case_lane_crash_surfaces_the_reason_in_console_and_report(python: str) -> None:
    scripts_source = TESTS_DIR.parent
    temp_root = new_temp_root(prefix="gate-lanecrash-")
    report_path = temp_root / "report.json"
    path_line = f'sys.path.insert(0, r"{scripts_source}")'
    driver = "\n".join(
        [
            "import sys",
            path_line,
            "import gate",
            "def crash(self, lane_name, step):",
            '    raise KeyboardInterrupt("lane died (fixture)")',
            "gate.Gate._run_step = crash",
            'sys.exit(gate.main(["--only", "csharp.tool-restore", "--report", sys.argv[1]]))',
        ]
    )
    write_fixture_file(temp_root, "lane-crash-driver.py", driver + "\n")
    try:
        completed = subprocess.run(
            [python, str(temp_root / "lane-crash-driver.py"), str(report_path)],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        output = ((completed.stdout or "") + (completed.stderr or "")).rstrip()
        assert_exit_code(1, "a crashed lane must fail the gate", completed.returncode)
        assert_true(
            "[gate] FAIL csharp.tool-restore" in output,
            f"the console must carry the belt's FAIL line, got: {output}",
        )
        assert_true(
            "no result recorded" in output,
            f"the console must carry the belt's failure reason, got: {output}",
        )
        assert_true(
            "failed: 1 of 1 step" in output,
            f"the summary must count the belt record, got: {output}",
        )
        report = json.loads(report_path.read_text(encoding="utf-8"))
        step = report["steps"][0]
        assert_true(step["ok"] is False, "the belt-recorded step must be failed in the report")
        assert_true(
            "no result recorded" in step["failure"],
            f"the report must carry the belt's failure reason, got: {step['failure']}",
        )
        assert_true(report["passed"] is False, "the report verdict must be failed")
    finally:
        remove_temp_root(temp_root)


def run_gate_driver(python: str, statements: List[str]) -> Tuple[str, int]:
    """Run a driver that imports the gate module and executes the given statements.

    The generated driver inserts the scripts directory on ``sys.path`` so the
    gate's internals are reachable in-process; the driver's JSON stdout is the
    case's evidence, and no toolchain beyond the interpreter is touched.
    """
    scripts_source = TESTS_DIR.parent
    temp_root = new_temp_root(prefix="gate-scope-")
    path_line = f'sys.path.insert(0, r"{scripts_source}")'
    driver = "\n".join(["import sys, json", path_line, "import gate", *statements])
    driver_path = temp_root / "scope-driver.py"
    write_fixture_file(temp_root, "scope-driver.py", driver + "\n")
    try:
        completed = subprocess.run(
            [python, str(driver_path)],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        output = ((completed.stdout or "") + (completed.stderr or "")).rstrip()
        return output, completed.returncode
    finally:
        remove_temp_root(temp_root)


def case_scoped_lane_table_rewrites_the_three_lint_steps(python: str) -> None:
    output, exit_code = run_gate_driver(
        python,
        [
            'scoped = gate.scoped_lanes(("Editor/A.cs", "Runtime/B.cs"))',
            "steps = {step.name: list(step.args) for _, lane in scoped for step in lane}",
            "static = {step.name: list(step.args) for _, lane in gate.LANES for step in lane}",
            'print(json.dumps({"order": steps["csharp.member-order"], "null": steps["csharp.null-assertions"], "usings": steps["csharp.usings"], "restore": steps["csharp.tool-restore"] == static["csharp.tool-restore"], "format": steps["csharp.format"] == static["csharp.format"], "total": len(steps), "static_total": len(static)}))',
        ],
    )
    assert_exit_code(0, f"the driver must succeed (got {exit_code}): {output}", exit_code)
    result = json.loads(output)
    for name in ("order", "null", "usings"):
        args = result[name]
        assert_true(
            args[-3:] == ["--paths", "Editor/A.cs", "Runtime/B.cs"] and "lint_csharp_" in " ".join(args),
            f"the {name} step must scan exactly the changed files, got {args}",
        )
    assert_true(result["restore"] is True, "the tool-restore step must stay whole-tree")
    assert_true(result["format"] is True, "the format step must stay whole-tree")
    assert_true(result["total"] == result["static_total"], "scoping must not drop steps when C# changed")


def case_scoped_lane_table_drops_the_lint_steps_when_no_csharp_changed(python: str) -> None:
    output, exit_code = run_gate_driver(
        python,
        [
            "scoped = gate.scoped_lanes(())",
            "steps = [step.name for _, lane in scoped for step in lane]",
            "dropped = [name for name in gate.CSHARP_LINT_STEPS if name not in steps]",
            'print(json.dumps({"steps": steps, "dropped": dropped, "lanes": [name for name, _ in scoped]}))',
        ],
    )
    assert_exit_code(0, f"the driver must succeed (got {exit_code}): {output}", exit_code)
    result = json.loads(output)
    assert_true(
        sorted(result["dropped"]) == ["csharp.member-order", "csharp.null-assertions", "csharp.usings"],
        f"all three lint steps must be scoped out, got {result['dropped']}",
    )
    assert_true("csharp.tool-restore" in result["steps"] and "csharp.format" in result["steps"], "the restore and format steps must stay")
    assert_true("tests.self-tests" in result["steps"], "the suite must stay")


def case_scoped_lane_table_keeps_whole_tree_without_a_base(python: str) -> None:
    output, exit_code = run_gate_driver(
        python,
        [
            "scoped = gate.scoped_lanes(None)",
            'same = [(name, [(step.name, list(step.args)) for step in lane]) for name, lane in scoped] == [(name, [(step.name, list(step.args)) for step in lane]) for name, lane in gate.LANES]',
            'print(json.dumps({"same": same}))',
        ],
    )
    assert_exit_code(0, f"the driver must succeed (got {exit_code}): {output}", exit_code)
    assert_true(json.loads(output)["same"] is True, "a missing base must keep the whole-tree ladder byte for byte")


def case_execution_scopes_the_lint_steps_to_the_scanned_diff(python: str) -> None:
    scripts_source = TESTS_DIR.parent
    temp_root = new_temp_root(prefix="gate-scopeexec-")
    path_line = f'sys.path.insert(0, r"{scripts_source}")'
    changed = "Editor/DataVisualizer/Utilities/AssetGuidDiscovery.cs"
    driver = "\n".join(
        [
            "import sys",
            path_line,
            "import gate",
            f'gate.scan_changed_csharp = lambda base_sha: ("{changed}",)',
            'sys.exit(gate.main(["--only", "csharp.member-order"]))',
        ]
    )
    write_fixture_file(temp_root, "scope-exec-driver.py", driver + "\n")
    try:
        completed = subprocess.run(
            [python, str(temp_root / "scope-exec-driver.py")],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        output = ((completed.stdout or "") + (completed.stderr or "")).rstrip()
        assert_exit_code(0, f"the scoped lint run must pass: {output}", completed.returncode)
        assert_true(
            "csharp lints: scoped to 1 changed file(s)" in output,
            f"the console must announce the scoped scan, got: {output}",
        )
        assert_true("[gate] ok   csharp.member-order" in output, f"the scoped step must run and pass, got: {output}")
    finally:
        remove_temp_root(temp_root)


def case_execution_skips_the_lint_steps_when_no_csharp_changed(python: str) -> None:
    scripts_source = TESTS_DIR.parent
    temp_root = new_temp_root(prefix="gate-scopeskip-")
    path_line = f'sys.path.insert(0, r"{scripts_source}")'
    driver = "\n".join(
        [
            "import sys",
            path_line,
            "import gate",
            "gate.scan_changed_csharp = lambda base_sha: ()",
            'sys.exit(gate.main(["--only", "csharp.member-order"]))',
        ]
    )
    write_fixture_file(temp_root, "scope-skip-driver.py", driver + "\n")
    try:
        completed = subprocess.run(
            [python, str(temp_root / "scope-skip-driver.py")],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        output = ((completed.stdout or "") + (completed.stderr or "")).rstrip()
        assert_exit_code(0, f"the skipped step must not fail the gate: {output}", completed.returncode)
        assert_true(
            "csharp lints: skipped, no C# changed" in output,
            f"the console must announce the skip, got: {output}",
        )
        assert_true("passed: 0 step(s)" in output, f"the summary must show the empty run, got: {output}")
    finally:
        remove_temp_root(temp_root)


def main() -> int:
    print("== Gate plan self-tests ==")

    # The gate is typed Python executed through uv locally; the self-tests
    # only need a working interpreter because --plan executes nothing.
    python = find_python()

    run_case("Plan_PrintsTheFullLaneTable", lambda: case_plan_prints_the_full_lane_table(python))
    run_case("Plan_CarriesCommandsAndDependencies", lambda: case_plan_carries_commands_and_dependencies(python))
    run_case(
        "Plan_MarksTheDriftCheckAsAnEmptyOutputCheck",
        lambda: case_plan_marks_the_drift_check_as_an_empty_output_check(python),
    )
    run_case("Plan_ResolvesEveryProgram", lambda: case_plan_resolves_every_program(python))
    run_case(
        "Only_SelectsTheRequestedStepsWithDependencies",
        lambda: case_only_selects_the_requested_steps_with_dependencies(python),
    )
    run_case("Only_FailsOnUnknownStepNames", lambda: case_only_fails_on_unknown_step_names(python))
    run_case("Plan_ExcludesTheUnitySuites", lambda: case_plan_excludes_the_unity_suites(python))
    run_case(
        "LaunchFailure_SurfacesTheReasonInConsoleAndReport",
        lambda: case_launch_failure_surfaces_the_reason_in_console_and_report(python),
    )
    run_case(
        "ReportWriteFailure_IsVisibleWithoutCrashingTheGate",
        lambda: case_report_write_failure_is_visible_without_crashing_the_gate(python),
    )
    run_case(
        "LaneCrash_SurfacesTheReasonInConsoleAndReport",
        lambda: case_lane_crash_surfaces_the_reason_in_console_and_report(python),
    )
    run_case(
        "ScopedLaneTable_RewritesTheThreeLintSteps",
        lambda: case_scoped_lane_table_rewrites_the_three_lint_steps(python),
    )
    run_case(
        "ScopedLaneTable_DropsTheLintStepsWhenNoCSharpChanged",
        lambda: case_scoped_lane_table_drops_the_lint_steps_when_no_csharp_changed(python),
    )
    run_case(
        "ScopedLaneTable_KeepsWholeTreeWithoutABase",
        lambda: case_scoped_lane_table_keeps_whole_tree_without_a_base(python),
    )
    run_case(
        "Execution_ScopesTheLintStepsToTheScannedDiff",
        lambda: case_execution_scopes_the_lint_steps_to_the_scanned_diff(python),
    )
    run_case(
        "Execution_SkipsTheLintStepsWhenNoCSharpChanged",
        lambda: case_execution_skips_the_lint_steps_when_no_csharp_changed(python),
    )

    failures = failure_count()
    if failures > 0:
        print(f"gate self-tests: {failures} failed")
        return 1
    print("gate self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
