#!/usr/bin/env python3
"""Release preparation self-tests (typed Python port of ``test-prepare-release.ps1``, issue #171).

Every case from the PowerShell original is kept with the same name and
assertions; only the driver language changed. The cases drive
``scripts/release/prepare-release.mjs`` through node the same way.
"""

from __future__ import annotations

import hashlib
import json
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Optional, Sequence, Tuple

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    failure_count,
    find_node,
    new_temp_root,
    remove_temp_root,
    run_case,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
PREPARE_SCRIPT = SCRIPTS_DIR / "release" / "prepare-release.mjs"

DEFAULT_CHANGELOG = (
    "# Changelog\n"
    "\n"
    "All notable changes to this package are documented in this file.\n"
    "\n"
    "## [Unreleased]\n"
    "\n"
    "### Added\n"
    "\n"
    "- Initial changelog.\n"
    "\n"
    "## [0.0.36] - 2026-07-07\n"
    "\n"
    "### Fixed\n"
    "\n"
    "- Old entry.\n"
    "\n"
)


def new_release_fixture(version: str = "0.0.37", changelog: Optional[str] = None) -> Path:
    root = new_temp_root(prefix="release-fixture-")
    write_fixture_file(
        root,
        "package.json",
        '{\n  "name": "com.test.fixture",\n  "version": "' + version + '"\n}\n',
    )
    write_fixture_file(root, ".llm/context.md", f"- **Package**: com.test.fixture\n- **Version**: {version}\n")
    if changelog is None:
        changelog = DEFAULT_CHANGELOG
    write_fixture_file(root, "CHANGELOG.md", changelog)
    return root


def invoke_prepare(node: str, root: Path, arguments: Sequence[str]) -> Tuple[str, int]:
    return run_tool_output([node, str(PREPARE_SCRIPT), "--root", str(root), *arguments])


def get_file_hash_text(root: Path) -> str:
    hashes = [hashlib.md5((root / name).read_bytes()).hexdigest().upper() for name in ("package.json", "CHANGELOG.md", ".llm/context.md")]
    return ",".join(hashes)


def read_fixture_json(root: Path, relative_path: str) -> "dict[str, Any]":
    return json.loads((root / relative_path).read_text(encoding="utf-8"))


def case_bumps_each_bump_kind_and_syncs_every_file(node: str) -> None:
    cases = [
        {"bump": "patch", "start": "0.0.37", "expected": "0.0.38"},
        {"bump": "minor", "start": "0.0.37", "expected": "0.1.0"},
        {"bump": "major", "start": "0.0.37", "expected": "1.0.0"},
    ]
    for case in cases:
        root = new_release_fixture(version=case["start"])
        try:
            output, exit_code = invoke_prepare(node, root, ["--bump", case["bump"]])
            assert_exit_code(0, f"bump {case['bump']} should succeed", exit_code)
            package = read_fixture_json(root, "package.json")
            assert_true(package["version"] == case["expected"], f"package.json should carry {case['expected']}, got {package['version']}")
            context = (root / ".llm/context.md").read_text(encoding="utf-8")
            assert_true(f"**Version**: {case['expected']}" in context, f"context.md should carry **Version**: {case['expected']}, got: {context}")
            assert_true(f"previous-version: {case['start']}" in output, f"output should report the previous version, got: {output}")
            assert_true(f"next-version: {case['expected']}" in output, f"output should report the next version, got: {output}")
        finally:
            remove_temp_root(root)


def case_applies_explicit_prerelease_version(node: str) -> None:
    root = new_release_fixture()
    try:
        output, exit_code = invoke_prepare(node, root, ["--version", "0.0.38-rc.1"])
        assert_exit_code(0, "explicit prerelease version should succeed", exit_code)
        package = read_fixture_json(root, "package.json")
        assert_true(package["version"] == "0.0.38-rc.1", f"package.json should carry 0.0.38-rc.1, got {package['version']}")
        context = (root / ".llm/context.md").read_text(encoding="utf-8")
        assert_true("**Version**: 0.0.38-rc.1" in context, f"context.md should carry **Version**: 0.0.38-rc.1, got: {context}")
        changelog = (root / "CHANGELOG.md").read_text(encoding="utf-8")
        assert_true("## [0.0.38-rc.1] - " in changelog, f"changelog should gain the prerelease section, got: {changelog}")
        assert_true("next-version: 0.0.38-rc.1" in output, f"output should report the prerelease version, got: {output}")
    finally:
        remove_temp_root(root)


def case_rotates_unreleased_into_dated_section(node: str) -> None:
    changelog = (
        "# Changelog\n"
        "\n"
        "All notable changes to this package are documented in this file.\n"
        "\n"
        "## [Unreleased]\n"
        "\n"
        "### Added\n"
        "\n"
        "- First entry.\n"
        "- Second entry.\n"
        "\n"
        "## [0.0.36] - 2026-07-07\n"
        "\n"
        "### Fixed\n"
        "\n"
        "- Old entry.\n"
        "\n"
        "[keepachangelog]: https://keepachangelog.com/en/1.1.0/\n"
    )
    root = new_release_fixture(changelog=changelog)
    date_before = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    try:
        _, exit_code = invoke_prepare(node, root, ["--bump", "patch"])
        assert_exit_code(0, "rotation should succeed", exit_code)
        date_after = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        changelog_result = (root / "CHANGELOG.md").read_text(encoding="utf-8")
        expected_fragments = [
            "## [0.0.38] - ",
            date_before,
            date_after,
            "## [Unreleased]",
            "### Added",
            "- First entry.",
            "- Second entry.",
            "## [0.0.36] - 2026-07-07",
            "[keepachangelog]: https://keepachangelog.com/en/1.1.0/",
        ]
        for fragment in expected_fragments:
            assert_true(fragment in changelog_result, f"rotated changelog should contain '{fragment}', got: {changelog_result}")
        unreleased_index = changelog_result.find("## [Unreleased]")
        released_index = changelog_result.find("## [0.0.38] - ")
        previous_index = changelog_result.find("## [0.0.36] - ")
        assert_true(
            unreleased_index < released_index < previous_index,
            f"Unreleased must precede the new section which precedes older sections, got: {changelog_result}",
        )
        tail = changelog_result[released_index:]
        assert_true("- Second entry.\n\n## [0.0.36]" in tail, f"the new section body must be separated from the next heading by a blank line, got: {changelog_result}")
    finally:
        remove_temp_root(root)


def case_fails_when_unreleased_section_is_missing(node: str) -> None:
    changelog = "# Changelog\n\n## [0.0.36] - 2026-07-07\n\n- Old entry."
    root = new_release_fixture(changelog=changelog)
    try:
        before = get_file_hash_text(root)
        output, exit_code = invoke_prepare(node, root, ["--bump", "patch"])
        assert_exit_code(1, "missing Unreleased should fail", exit_code)
        assert_true("Unreleased" in output, f"error should name Unreleased, got: {output}")
        assert_true(get_file_hash_text(root) == before, "a failed run must not mutate files")
    finally:
        remove_temp_root(root)


def case_fails_when_unreleased_section_has_no_notes(node: str) -> None:
    changelog = "# Changelog\n\n## [Unreleased]\n\n### Added\n\n## [0.0.36] - 2026-07-07"
    root = new_release_fixture(changelog=changelog)
    try:
        before = get_file_hash_text(root)
        output, exit_code = invoke_prepare(node, root, ["--bump", "patch"])
        assert_exit_code(1, "empty Unreleased should fail", exit_code)
        assert_true("no release notes" in output, f"error should explain the missing notes, got: {output}")
        assert_true(get_file_hash_text(root) == before, "a failed run must not mutate files")
    finally:
        remove_temp_root(root)


def case_fails_when_target_section_already_exists(node: str) -> None:
    changelog = "# Changelog\n\n## [Unreleased]\n\n- Entry.\n\n## [0.0.38] - 2026-09-15\n\n- Already released."
    root = new_release_fixture(changelog=changelog)
    try:
        before = get_file_hash_text(root)
        output, exit_code = invoke_prepare(node, root, ["--bump", "patch"])
        assert_exit_code(1, "duplicate target section should fail", exit_code)
        assert_true("## [0.0.38]" in output, f"error should name the duplicate section, got: {output}")
        assert_true(get_file_hash_text(root) == before, "a failed run must not mutate files")
    finally:
        remove_temp_root(root)


def case_fails_when_explicit_version_no_ops(node: str) -> None:
    root = new_release_fixture()
    try:
        before = get_file_hash_text(root)
        output, exit_code = invoke_prepare(node, root, ["--version", "0.0.37"])
        assert_exit_code(1, "no-op explicit version should fail", exit_code)
        assert_true("equals the current version" in output, f"error should explain the no-op, got: {output}")
        assert_true(get_file_hash_text(root) == before, "a failed run must not mutate files")
    finally:
        remove_temp_root(root)


@dataclass(frozen=True)
class ArgumentCase:
    """One rejected argument vector the release script owes a failure."""

    name: str
    arguments: Sequence[str]


def case_rejects_invalid_arguments(node: str) -> None:
    cases = [
        ArgumentCase(name="unknown bump word", arguments=["--bump", "beta"]),
        ArgumentCase(name="v-prefixed version", arguments=["--version", "v0.0.38"]),
        ArgumentCase(name="incomplete semver", arguments=["--version", "0.0"]),
        ArgumentCase(name="unsupported prerelease", arguments=["--version", "0.0.38-build.2"]),
        ArgumentCase(name="both bump and version", arguments=["--bump", "patch", "--version", "0.0.38"]),
        ArgumentCase(name="neither bump nor version", arguments=[]),
    ]
    for case in cases:
        root = new_release_fixture()
        try:
            before = get_file_hash_text(root)
            output, exit_code = invoke_prepare(node, root, case.arguments)
            assert_exit_code(1, f"invalid arguments ({case.name}) should fail", exit_code)
            assert_true("error: " in output, f"output should carry the error prefix ({case.name}), got: {output}")
            assert_true(get_file_hash_text(root) == before, f"a rejected run must not mutate files ({case.name})")
        finally:
            remove_temp_root(root)


def case_dry_run_reports_the_plan_without_mutating(node: str) -> None:
    root = new_release_fixture()
    try:
        before = get_file_hash_text(root)
        output, exit_code = invoke_prepare(node, root, ["--bump", "patch", "--dry-run"])
        assert_exit_code(0, "dry-run should succeed", exit_code)
        assert_true("0.0.37 -> 0.0.38" in output, f"dry-run should report the planned bump, got: {output}")
        assert_true(get_file_hash_text(root) == before, "dry-run must not mutate files")
    finally:
        remove_temp_root(root)


def case_fails_when_package_json_is_missing_version(node: str) -> None:
    root = new_release_fixture()
    try:
        write_fixture_file(root, "package.json", '{\n  "name": "com.test.fixture"\n}\n')
        output, exit_code = invoke_prepare(node, root, ["--bump", "patch"])
        assert_exit_code(1, "missing version field should fail", exit_code)
        assert_true("missing a version field" in output, f"error should name the missing field, got: {output}")
    finally:
        remove_temp_root(root)


def case_preserves_package_json_formatting(node: str) -> None:
    root = new_release_fixture()
    try:
        raw = '{\n "name":"com.test.fixture",\n "version":  "0.0.37" ,\n "nested": {"version": "9.9.9"},\n  "keep": true\n}\n\n\n'
        write_fixture_file(root, "package.json", raw)
        before = (root / "package.json").read_text(encoding="utf-8").split("\n")
        _, exit_code = invoke_prepare(node, root, ["--bump", "patch"])
        assert_exit_code(0, "the patch should succeed", exit_code)
        after = (root / "package.json").read_text(encoding="utf-8").split("\n")
        assert_true(len(before) == len(after), f"line count must not change ({len(before)} vs {len(after)})")
        for index, before_line in enumerate(before):
            if "version" in before_line:
                continue
            assert_true(before_line == after[index], f"line {index} must be unchanged: '{before_line}' vs '{after[index]}'")
        package = read_fixture_json(root, "package.json")
        assert_true(package["version"] == "0.0.38", f"package.json should carry 0.0.38, got {package['version']}")
        assert_true(package["nested"]["version"] == "9.9.9", "a nested version key must stay untouched")
    finally:
        remove_temp_root(root)


def case_fails_when_changelog_has_duplicate_unreleased_sections(node: str) -> None:
    changelog = (
        "# Changelog\n"
        "\n"
        "## [Unreleased]\n"
        "\n"
        "- First.\n"
        "\n"
        "## [Unreleased]\n"
        "\n"
        "- Second.\n"
        "\n"
        "## [0.0.36] - 2026-07-07"
    )
    root = new_release_fixture(changelog=changelog)
    try:
        before = get_file_hash_text(root)
        output, exit_code = invoke_prepare(node, root, ["--bump", "patch"])
        assert_exit_code(1, "duplicate Unreleased sections should fail", exit_code)
        assert_true("more than one Unreleased" in output, f"error should explain the duplicate sections, got: {output}")
        assert_true(get_file_hash_text(root) == before, "a failed run must not mutate files")
    finally:
        remove_temp_root(root)


def case_fails_when_package_json_is_invalid_json(node: str) -> None:
    root = new_release_fixture()
    try:
        write_fixture_file(root, "package.json", '{"name": "broken"')
        output, exit_code = invoke_prepare(node, root, ["--bump", "patch"])
        assert_exit_code(1, "invalid JSON should fail", exit_code)
        assert_true("not valid JSON" in output, f"error should explain the invalid JSON, got: {output}")
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== prepare-release self-tests ==")

    node = find_node()

    run_case("Bumps_EachBumpKind_AndSyncsEveryFile", lambda: case_bumps_each_bump_kind_and_syncs_every_file(node))
    run_case("Applies_ExplicitPrereleaseVersion", lambda: case_applies_explicit_prerelease_version(node))
    run_case("Rotates_Unreleased_IntoDatedSection_And_PreservesSurroundingContent", lambda: case_rotates_unreleased_into_dated_section(node))
    run_case("Fails_When_UnreleasedSection_IsMissing", lambda: case_fails_when_unreleased_section_is_missing(node))
    run_case("Fails_When_UnreleasedSection_HasNoNotes", lambda: case_fails_when_unreleased_section_has_no_notes(node))
    run_case("Fails_When_TargetSectionAlreadyExists", lambda: case_fails_when_target_section_already_exists(node))
    run_case("Fails_When_ExplicitVersion_NoOps", lambda: case_fails_when_explicit_version_no_ops(node))
    run_case("Rejects_InvalidArguments", lambda: case_rejects_invalid_arguments(node))
    run_case("DryRun_ReportsThePlan_WithoutMutating", lambda: case_dry_run_reports_the_plan_without_mutating(node))
    run_case("Fails_When_PackageJson_IsMissingVersion", lambda: case_fails_when_package_json_is_missing_version(node))
    run_case("Preserves_PackageJsonFormatting", lambda: case_preserves_package_json_formatting(node))
    run_case("Fails_When_Changelog_HasDuplicateUnreleasedSections", lambda: case_fails_when_changelog_has_duplicate_unreleased_sections(node))
    run_case("Fails_When_PackageJson_IsInvalidJson", lambda: case_fails_when_package_json_is_invalid_json(node))

    failures = failure_count()
    print(f"== prepare-release: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
