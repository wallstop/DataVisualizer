#!/usr/bin/env python3
"""File-length lint self-tests (typed Python port of ``test-file-lengths.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions;
only the driver language changed.
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
    fixture_lines,
    new_temp_root,
    remove_temp_root,
    run_case,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
LINT_SCRIPT = SCRIPTS_DIR / "lint_file_lengths.py"


def run_lint(python: str, arguments: Sequence[str]) -> Tuple[str, int]:
    """Run the lint and return (merged output, exit code)."""
    completed = subprocess.run(
        [python, str(LINT_SCRIPT), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return (completed.stdout or "") + (completed.stderr or ""), completed.returncode


def case_passes_when_file_under_limit(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/context.md", fixture_lines(10))
        _, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(0, "short file should pass", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_when_file_exceeds_hard_limit(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/oversize.md", fixture_lines(350))
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "oversize file should fail", exit_code)
        assert_true("oversize.md" in output, f"output should name the offending file, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_staged_file_over_limit(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/context.md", fixture_lines(301))
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "301-line file should fail", exit_code)
        assert_true("file-length" in output, f"output should carry [file-length] prefix, got: {output}")
        assert_true("301" in output, "output should mention the offending line count")
    finally:
        remove_temp_root(root)


def case_passes_when_file_at_exact_limit(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/context.md", fixture_lines(300))
        _, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(0, "300-line file should pass", exit_code)
    finally:
        remove_temp_root(root)


def case_warns_when_file_near_limit_with_verbose_output(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/context.md", fixture_lines(290))
        output, exit_code = run_lint(python, ["--root", str(root), "--verbose-output"])
        assert_exit_code(0, "290-line file should pass", exit_code)
        assert_true("near" in output, f"output should warn near limit, got: {output}")
    finally:
        remove_temp_root(root)


def case_hides_near_limit_warning_without_verbose_output(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/context.md", fixture_lines(290))
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(0, "290-line file should pass", exit_code)
        assert_true("near" not in output, f"output should be silent without -VerboseOutput, got: {output}")
    finally:
        remove_temp_root(root)


def case_ignores_generated_skills_index(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/skills/index.md", fixture_lines(400))
        _, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(0, "generated index should be excluded", exit_code)
    finally:
        remove_temp_root(root)


def case_checks_skill_files_in_nested_directories(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/skills/big-skill/SKILL.md", fixture_lines(350))
        _, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "oversize SKILL.md should fail", exit_code)
    finally:
        remove_temp_root(root)


def case_scopes_to_paths_when_provided(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/context.md", fixture_lines(400))
        write_fixture_file(root, ".llm/skills/ok/SKILL.md", fixture_lines(10))
        _, exit_code = run_lint(python, ["--root", str(root), "--paths", ".llm/skills/ok/SKILL.md"])
        assert_exit_code(0, "scoped run should only check the named file", exit_code)
        output, exit_code = run_lint(python, ["--root", str(root), "--paths", ".llm/context.md"])
        assert_exit_code(1, "scoped run should catch the named bad file", exit_code)
        assert_true("context.md" in output, f"output should name the bad file, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_path_escapes_llm_directory(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, "README.md", fixture_lines(400))
        output, exit_code = run_lint(python, ["--root", str(root), "--paths", "README.md"])
        assert_exit_code(1, "paths outside .llm should be rejected", exit_code)
        assert_true("outside" in output, f"output should explain rejection, got: {output}")
    finally:
        remove_temp_root(root)


def case_counts_lines_with_crlf_endings(python: str) -> None:
    root = new_temp_root()
    try:
        content = fixture_lines(305).replace("\n", "\r\n")
        write_fixture_file(root, ".llm/context.md", content)
        _, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "CRLF file should be counted correctly", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_when_planning_doc_exceeds_planning_limit(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, "PLAN.md", fixture_lines(151))
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "oversize planning doc should fail", exit_code)
        assert_true("PLAN.md" in output, f"output should name the planning doc, got: {output}")
        assert_true("planning-doc" in output, "output should name the limit kind")
    finally:
        remove_temp_root(root)


def case_passes_when_planning_doc_at_planning_limit(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, "PLAN.md", fixture_lines(150))
        write_fixture_file(root, "GOAL.md", fixture_lines(82))
        _, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(0, "planning docs at or under the limit should pass", exit_code)
    finally:
        remove_temp_root(root)


def case_skips_missing_planning_docs(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/context.md", fixture_lines(10))
        _, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(0, "absent planning docs should be skipped (they are gitignored)", exit_code)
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== lint-file-lengths self-tests ==")

    # The lint is typed Python (issue #171); the tests only need a working
    # interpreter because the fixtures never touch a toolchain.
    python = find_python()

    run_case("Passes_When_FileUnderLimit", lambda: case_passes_when_file_under_limit(python))
    run_case("Fails_When_FileExceedsHardLimit", lambda: case_fails_when_file_exceeds_hard_limit(python))
    run_case("Fails_When_StagedFileOverLimit", lambda: case_fails_when_staged_file_over_limit(python))
    run_case("Passes_When_FileAtExactLimit", lambda: case_passes_when_file_at_exact_limit(python))
    run_case(
        "Warns_When_FileNearLimit_WithVerboseOutput",
        lambda: case_warns_when_file_near_limit_with_verbose_output(python),
    )
    run_case(
        "Hides_NearLimitWarning_WithoutVerboseOutput",
        lambda: case_hides_near_limit_warning_without_verbose_output(python),
    )
    run_case("Ignores_GeneratedSkillsIndex", lambda: case_ignores_generated_skills_index(python))
    run_case("Checks_SkillFiles_InNestedDirectories", lambda: case_checks_skill_files_in_nested_directories(python))
    run_case("Scopes_ToPaths_WhenProvided", lambda: case_scopes_to_paths_when_provided(python))
    run_case("Fails_When_PathEscapesLlmDirectory", lambda: case_fails_when_path_escapes_llm_directory(python))
    run_case("Counts_Lines_WithCrlfEndings", lambda: case_counts_lines_with_crlf_endings(python))
    run_case(
        "Fails_When_PlanningDocExceedsPlanningLimit",
        lambda: case_fails_when_planning_doc_exceeds_planning_limit(python),
    )
    run_case(
        "Passes_When_PlanningDocAtPlanningLimit",
        lambda: case_passes_when_planning_doc_at_planning_limit(python),
    )
    run_case("Skips_MissingPlanningDocs", lambda: case_skips_missing_planning_docs(python))

    failures = failure_count()
    print(f"== lint-file-lengths: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
