#!/usr/bin/env python3
"""Instructions lint self-tests (typed Python port of ``test-lint-llm-instructions.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions;
only the driver language changed. The fixture copies the scripts directory
verbatim (both ``*.py`` and the remaining ``*.ps1`` files) so the lint sees
a realistic tooling surface.
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
    find_python,
    fixture_path,
    new_llm_fixture_root,
    new_temp_root,
    remove_temp_root,
    run_cases,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
LINT_SCRIPT = SCRIPTS_DIR / "lint_llm_instructions.py"
GENERATE_SCRIPT = SCRIPTS_DIR / "generate_skills_index.py"


def run_program(python: str, script: Path, arguments: Sequence[str]) -> Tuple[str, int]:
    """Run a script and return (merged output, exit code)."""
    completed = subprocess.run(
        [python, str(script), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return (completed.stdout or "") + (completed.stderr or ""), completed.returncode


def invoke_lint(python: str, arguments: Sequence[str]) -> Tuple[str, int]:
    return run_program(python, LINT_SCRIPT, arguments)


def invoke_generate(python: str, arguments: Sequence[str]) -> Tuple[str, int]:
    return run_program(python, GENERATE_SCRIPT, arguments)


def new_linted_fixture(python: str, package_version: str = "1.2.3") -> Path:
    """Build the valid fixture tree, copy the scripts in, and generate its index."""
    root = new_llm_fixture_root(package_version)
    for script_file in sorted(list(SCRIPTS_DIR.glob("*.ps1")) + list(SCRIPTS_DIR.glob("*.py"))):
        write_fixture_file(root, f"scripts/{script_file.name}", script_file.read_text(encoding="utf-8"))
    _, exit_code = invoke_generate(python, ["--root", str(root)])
    assert_exit_code(0, "fixture index generation should succeed", exit_code)
    return root


def case_passes_when_fixture_is_valid(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        _, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(0, "valid fixture should pass", exit_code)
    finally:
        remove_temp_root(root)


def case_passes_with_optional_frontmatter_keys(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        skill = "\n".join(
            [
                "---",
                "name: extra-skill",
                "description: Optional-keys fixture skill. Use when testing tolerance.",
                "license: MIT",
                "allowed-tools: Read Grep",
                "metadata:",
                "  category: Core",
                "---",
                "",
                "# Skill: Extra",
                "",
                "Body.",
            ]
        )
        write_fixture_file(root, ".llm/skills/extra-skill/SKILL.md", skill + "\n")
        _, exit_code = invoke_generate(python, ["--root", str(root)])
        assert_exit_code(0, "index generation should tolerate optional keys", exit_code)
        _, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(0, "lint should tolerate optional frontmatter keys", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_when_pointer_file_missing_delegation_link(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        write_fixture_file(root, "CLAUDE.md", "# Claude Configuration\n\nAll rules live elsewhere, sorry.\n")
        output, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(1, "pointer missing delegation link should fail", exit_code)
        assert_true("CLAUDE.md" in output, f"output should name the pointer, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_pointer_file_missing(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        fixture_path(root, ".cursorrules").unlink()
        output, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(1, "missing pointer file should fail", exit_code)
        assert_true(".cursorrules" in output, f"output should name the pointer, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_skill_name_uppercase(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        skill = "\n".join(
            [
                "---",
                "name: Bad-Name",
                "description: Uppercase fixture skill. Use when testing validation.",
                "metadata:",
                "  category: Core",
                "---",
                "",
                "# Skill: Bad",
                "",
                "Body.",
            ]
        )
        write_fixture_file(root, ".llm/skills/Bad-Name/SKILL.md", skill + "\n")
        output, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(1, "uppercase skill name should fail", exit_code)
        assert_true("Bad-Name" in output, f"output should name the skill, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_description_over_1024_chars(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        long_description = "x" * 1025
        skill = "\n".join(
            [
                "---",
                "name: long-description-skill",
                f"description: {long_description}",
                "metadata:",
                "  category: Core",
                "---",
                "",
                "# Skill: Long",
                "",
                "Body.",
            ]
        )
        write_fixture_file(root, ".llm/skills/long-description-skill/SKILL.md", skill + "\n")
        output, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(1, "description over 1024 characters should fail", exit_code)
        assert_true("1024" in output, "output should mention the limit")
    finally:
        remove_temp_root(root)


def case_fails_when_index_drift_and_fix_regenerates(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        write_fixture_file(root, ".llm/skills/index.md", "stale")
        output, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(1, "stale index should fail", exit_code)
        assert_true("index" in output, f"output should mention the index, got: {output}")
        _, exit_code = invoke_lint(python, ["--root", str(root), "--fix"])
        assert_exit_code(0, "-Fix should regenerate the index and pass", exit_code)
        content = fixture_path(root, ".llm/skills/index.md").read_text(encoding="utf-8")
        assert_true("# Skills Index" in content, "regenerated index should be real content")
    finally:
        remove_temp_root(root)


def case_fails_when_context_has_multiple_h1(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        context = "\n".join(
            [
                "# LLM Agent Instructions",
                "",
                "See the generated [Skills Index](./skills/index.md).",
                "",
                "## Repository Overview",
                "",
                "- **Version**: 1.2.3",
                "",
                "# Another Title",
                "",
                "Oops.",
            ]
        )
        write_fixture_file(root, ".llm/context.md", context + "\n")
        output, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(1, "multiple H1s should fail", exit_code)
        assert_true("H1" in output, "output should mention the H1 rule")
    finally:
        remove_temp_root(root)


def case_fails_when_context_version_mismatches_package_and_fix_syncs(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        context = fixture_path(root, ".llm/context.md").read_text(encoding="utf-8")
        stale_context = context.replace("1.2.3", "0.0.1")
        write_fixture_file(root, ".llm/context.md", stale_context)
        output, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(1, "stale version should fail", exit_code)
        assert_true("ersion" in output, "output should mention version")
        _, exit_code = invoke_lint(python, ["--root", str(root), "--fix"])
        assert_exit_code(0, "-Fix should sync the version and pass", exit_code)
        fixed = fixture_path(root, ".llm/context.md").read_text(encoding="utf-8")
        assert_true(
            "**Version**: 1.2.3" in fixed,
            f"fixed context should carry the package version, got: {fixed}",
        )
    finally:
        remove_temp_root(root)


def case_fails_when_skill_directory_has_no_skill_md(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        write_fixture_file(root, ".llm/skills/empty-skill/README.md", "not a skill")
        output, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(1, "skill directory without SKILL.md should fail", exit_code)
        assert_true("empty-skill" in output, f"output should name the directory, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_context_missing_skills_link(python: str) -> None:
    root = new_linted_fixture(python)
    try:
        context = fixture_path(root, ".llm/context.md").read_text(encoding="utf-8")
        stripped = context.replace("See the generated [Skills Index](./skills/index.md).", "")
        write_fixture_file(root, ".llm/context.md", stripped)
        output, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(1, "context without skills index link should fail", exit_code)
        assert_true("skills/index.md" in output, "output should mention the required link")
    finally:
        remove_temp_root(root)


def case_fails_when_missing_context_file(python: str) -> None:
    root = new_temp_root()
    try:
        output, exit_code = invoke_lint(python, ["--root", str(root)])
        assert_exit_code(1, "missing context should fail", exit_code)
        assert_true("context.md" in output, f"output should name context.md, got: {output}")
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== lint-llm-instructions self-tests ==")

    # The lints are typed Python (issue #171); the tests only need a working
    # interpreter because the fixtures never touch a toolchain.
    python = find_python()

    failures = run_cases(
        (
            ("Passes_When_FixtureIsValid", lambda: case_passes_when_fixture_is_valid(python)),
            ("Passes_With_OptionalFrontmatterKeys", lambda: case_passes_with_optional_frontmatter_keys(python)),
            ("Fails_When_PointerFileMissingDelegationLink", lambda: case_fails_when_pointer_file_missing_delegation_link(python)),
            ("Fails_When_PointerFileMissing", lambda: case_fails_when_pointer_file_missing(python)),
            ("Fails_When_SkillNameUppercase", lambda: case_fails_when_skill_name_uppercase(python)),
            ("Fails_When_DescriptionOver1024Chars", lambda: case_fails_when_description_over_1024_chars(python)),
            ("Fails_When_IndexDrift_And_FixRegenerates", lambda: case_fails_when_index_drift_and_fix_regenerates(python)),
            ("Fails_When_ContextHasMultipleH1", lambda: case_fails_when_context_has_multiple_h1(python)),
            (
                "Fails_When_ContextVersionMismatchesPackage_And_FixSyncs",
                lambda: case_fails_when_context_version_mismatches_package_and_fix_syncs(python),
            ),
            ("Fails_When_SkillDirectoryHasNoSkillMd", lambda: case_fails_when_skill_directory_has_no_skill_md(python)),
            ("Fails_When_ContextMissingSkillsLink", lambda: case_fails_when_context_missing_skills_link(python)),
            ("Fails_When_MissingContextFile", lambda: case_fails_when_missing_context_file(python)),
        )
    )
    print(f"== lint-llm-instructions: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
