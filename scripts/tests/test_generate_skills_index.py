#!/usr/bin/env python3
"""Skills index generator self-tests (typed Python port of ``test-generate-skills-index.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions;
only the driver language changed.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from typing import Optional, Sequence, Tuple

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
SCRIPTS_DIR = TESTS_DIR.parent
GENERATE_SCRIPT = SCRIPTS_DIR / "generate_skills_index.py"


def run_generate(python: str, arguments: Sequence[str]) -> Tuple[str, int]:
    """Run the generator and return (merged output, exit code)."""
    completed = subprocess.run(
        [python, str(GENERATE_SCRIPT), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return (completed.stdout or "") + (completed.stderr or ""), completed.returncode


def write_skill(root: Path, directory: str, name: str, description: str, category: Optional[str]) -> None:
    """Write one SKILL.md fixture with the given frontmatter."""
    frontmatter = ["---", f"name: {name}", f"description: {description}"]
    if category is not None:
        frontmatter.append("metadata:")
        frontmatter.append(f"  category: {category}")
    frontmatter.extend(["---", "", f"# Skill: {directory}", "", "Body."])
    write_fixture_file(root, f".llm/skills/{directory}/SKILL.md", "\n".join(frontmatter) + "\n")


def assert_section_order(content: str) -> None:
    core_index = content.find("## Core Skills")
    workflow_index = content.find("## Workflow Skills")
    feature_index = content.find("## Feature Skills")
    assert_true(core_index >= 0, "index should contain a Core Skills section")
    assert_true(workflow_index >= 0, "index should contain a Workflow Skills section")
    assert_true(feature_index >= 0, "index should contain a Feature Skills section")
    assert_true(
        core_index < workflow_index and workflow_index < feature_index,
        "sections should appear in fixed category order",
    )


def case_generates_index_grouped_and_sorted(python: str) -> None:
    root = new_temp_root()
    try:
        # (directory, name, category) triples; a None category defaults to Feature.
        skills: Sequence[Tuple[str, str, Optional[str]]] = [
            ("zulu-skill", "zulu-skill", "Feature"),
            ("alpha-skill", "alpha-skill", "Core"),
            ("mid-skill", "mid-skill", "Workflow"),
            ("uncat-skill", "uncat-skill", None),
        ]
        for directory, name, category in skills:
            write_skill(
                root,
                directory,
                name,
                f"{directory} description for testing. Use when handling {directory}.",
                category,
            )
        _, exit_code = run_generate(python, ["--root", str(root)])
        assert_exit_code(0, "generation should succeed", exit_code)
        index_path = root / ".llm" / "skills" / "index.md"
        assert_true(index_path.exists(), "index.md should be created")
        content = index_path.read_text(encoding="utf-8")
        assert_section_order(content)
        assert_true("# Skills Index" in content, "index should have an H1")
        assert_true(
            "[alpha-skill](./alpha-skill/SKILL.md)" in content,
            "core table should link alpha-skill with SKILL.md path",
        )
        assert_true("generated" in content, "index should carry a generated-file banner")
        core_section = content[content.find("## Core Skills") : content.find("## Workflow Skills")]
        assert_true("alpha-skill" in core_section, "alpha-skill should be listed under Core")
        assert_true("zulu-skill" not in core_section, "zulu-skill should not be listed under Core")
        feature_section = content[content.find("## Feature Skills") :]
        assert_true("zulu-skill" in feature_section, "zulu-skill should be listed under Feature")
        assert_true("uncat-skill" in feature_section, "uncategorized skill should default to Feature")
    finally:
        remove_temp_root(root)


def case_fails_when_name_mismatches_directory(python: str) -> None:
    root = new_temp_root()
    try:
        write_skill(root, "foo", "other-name", "Mismatched fixture skill. Use when testing validation.", "Core")
        output, exit_code = run_generate(python, ["--root", str(root)])
        assert_exit_code(1, "name/directory mismatch should fail", exit_code)
        assert_true("name" in output, f"output should explain the mismatch, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_frontmatter_missing(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/skills/foo/SKILL.md", "# Skill: Foo\n\nNo frontmatter here.\n")
        _, exit_code = run_generate(python, ["--root", str(root)])
        assert_exit_code(1, "missing frontmatter should fail", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_when_description_missing(python: str) -> None:
    root = new_temp_root()
    try:
        # The description line must be absent entirely, not empty.
        write_fixture_file(
            root,
            ".llm/skills/foo/SKILL.md",
            "---\nname: foo\n---\n\n# Skill: Foo\n\nBody.\n",
        )
        _, exit_code = run_generate(python, ["--root", str(root)])
        assert_exit_code(1, "missing description should fail", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_when_category_unknown(python: str) -> None:
    root = new_temp_root()
    try:
        write_skill(root, "foo", "foo", "Unknown category fixture. Use when testing validation.", "Invalid")
        _, exit_code = run_generate(python, ["--root", str(root)])
        assert_exit_code(1, "unknown category should fail", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_when_no_skills_found(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, ".llm/context.md", "placeholder")
        output, exit_code = run_generate(python, ["--root", str(root)])
        assert_exit_code(1, "empty skills directory should fail", exit_code)
        assert_true("no skills" in output, f"output should explain the empty state, got: {output}")
    finally:
        remove_temp_root(root)


def case_output_is_deterministic_and_encoded_without_bom_with_lf(python: str) -> None:
    root = new_temp_root()
    try:
        write_skill(root, "alpha-skill", "alpha-skill", "Determinism fixture skill. Use when testing output stability.", "Core")
        _, exit_code = run_generate(python, ["--root", str(root)])
        assert_exit_code(0, "first run should succeed", exit_code)
        index_path = root / ".llm" / "skills" / "index.md"
        first = index_path.read_bytes()
        _, exit_code = run_generate(python, ["--root", str(root)])
        assert_exit_code(0, "second run should succeed", exit_code)
        second = index_path.read_bytes()
        assert_true(len(first) > 0, "index should not be empty")
        assert_true(first == second, "index bytes should be identical across runs")
        assert_true(
            not (first[0] == 0xEF and first[1] == 0xBB and first[2] == 0xBF),
            "index should not start with a BOM",
        )
        text = first.decode("utf-8")
        assert_true("\r" not in text, "index should use LF endings only")
    finally:
        remove_temp_root(root)


def case_fails_when_skill_name_exceeds_64_chars(python: str) -> None:
    root = new_temp_root()
    try:
        long_name = "a" * 65
        write_skill(root, long_name, long_name, "Long-name fixture skill. Use when testing validation.", None)
        _, exit_code = run_generate(python, ["--root", str(root)])
        assert_exit_code(1, "names over 64 characters should fail", exit_code)
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== generate-skills-index self-tests ==")

    # The generator is typed Python (issue #171); the tests only need a
    # working interpreter because the fixtures never touch a toolchain.
    python = find_python()

    run_case("Generates_Index_GroupedAndSorted", lambda: case_generates_index_grouped_and_sorted(python))
    run_case("Fails_When_NameMismatchesDirectory", lambda: case_fails_when_name_mismatches_directory(python))
    run_case("Fails_When_FrontmatterMissing", lambda: case_fails_when_frontmatter_missing(python))
    run_case("Fails_When_DescriptionMissing", lambda: case_fails_when_description_missing(python))
    run_case("Fails_When_CategoryUnknown", lambda: case_fails_when_category_unknown(python))
    run_case("Fails_When_NoSkillsFound", lambda: case_fails_when_no_skills_found(python))
    run_case(
        "Output_IsDeterministic_And_EncodedWithoutBom_WithLf",
        lambda: case_output_is_deterministic_and_encoded_without_bom_with_lf(python),
    )
    run_case("Fails_When_SkillNameExceeds64Chars", lambda: case_fails_when_skill_name_exceeds_64_chars(python))

    failures = failure_count()
    print(f"== generate-skills-index: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
