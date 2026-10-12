#!/usr/bin/env python3
"""Release tagging self-tests (typed Python port of ``test-tag-release.ps1``, issue #171).

Every case from the PowerShell original is kept with the same name and
assertions; only the driver language changed. The cases drive
``scripts/release/tag_release.py`` as Python child processes the same way.
"""

from __future__ import annotations

import hashlib
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional, Sequence, Tuple

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    failure_count,
    find_python,
    new_temp_root,
    remove_temp_root,
    run_case,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
TAG_SCRIPT = SCRIPTS_DIR / "release" / "tag_release.py"

def default_changelog(version: str) -> str:
    return (
        "# Changelog\n"
        "\n"
        "All notable changes to this package are documented in this file.\n"
        "\n"
        "## [Unreleased]\n"
        "\n"
        "- Next up.\n"
        "\n"
        f"## [{version}] - 2026-09-16\n"
        "\n"
        "### Added\n"
        "\n"
        "- Initial changelog.\n"
    )


def git_capture(root: Path, *arguments: str) -> Tuple[str, int]:
    completed = subprocess.run(
        ["git", "-C", str(root), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return (completed.stdout or "").strip(), completed.returncode


def git_spawn(root: Path, *arguments: str) -> int:
    """Run a fixture git step whose output flows to the test log, like the original."""
    completed = subprocess.run(["git", "-C", str(root), *arguments], check=False)
    return completed.returncode


def new_tag_fixture(version: str = "0.0.38", changelog: Optional[str] = None, no_git: bool = False, skip_commit: bool = False) -> Path:
    root = new_temp_root(prefix="tag-fixture-")
    if not no_git:
        _, exit_code = git_capture(root, "init", "--quiet")
        if exit_code != 0:
            raise RuntimeError("git init failed for fixture")
        git_spawn(root, "config", "user.name", "Fixture Tester")
        git_spawn(root, "config", "user.email", "fixture@example.invalid")
    write_fixture_file(root, "package.json", '{\n  "name": "com.test.fixture",\n  "version": "' + version + '"\n}\n')
    if changelog is None:
        changelog = default_changelog(version)
    write_fixture_file(root, "CHANGELOG.md", changelog)
    if not no_git and not skip_commit:
        git_spawn(root, "add", "-A")
        exit_code = git_spawn(root, "commit", "--quiet", "-m", "Prepare release")
        if exit_code != 0:
            raise RuntimeError("git commit failed for fixture")
    return root


def invoke_tag(python: str, root: Path, arguments: Sequence[str]) -> Tuple[str, int]:
    return run_tool_output([python, str(TAG_SCRIPT), "--root", str(root), *arguments])


def get_tag_names(root: Path) -> List[str]:
    output, _ = git_capture(root, "tag")
    return [line for line in output.splitlines() if line.strip()]


def get_tag_target(root: Path, tag: str) -> str:
    output, _ = git_capture(root, "rev-parse", tag + "^{commit}")
    return output


def get_head_sha(root: Path) -> str:
    output, _ = git_capture(root, "rev-parse", "HEAD")
    return output


def get_tag_message(root: Path, tag: str) -> str:
    output, _ = git_capture(root, "tag", "-l", "--format", "%(contents)", tag)
    return output


def get_fixture_hash_text(root: Path) -> str:
    hashes = [hashlib.md5((root / name).read_bytes()).hexdigest().upper() for name in ("package.json", "CHANGELOG.md")]
    return ",".join(hashes)


def case_creates_annotated_tag_at_head(python: str) -> None:
    root = new_tag_fixture()
    try:
        head = get_head_sha(root)
        output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38"])
        assert_exit_code(0, "tagging a matching release tree should succeed", exit_code)
        tags = get_tag_names(root)
        assert_true(len(tags) == 1 and tags[0] == "v0.0.38", f"exactly one tag v0.0.38 should exist, got: {', '.join(tags)}")
        kind, _ = git_capture(root, "cat-file", "-t", "v0.0.38")
        assert_true(kind == "tag", "the created tag must be annotated, not lightweight")
        assert_true(get_tag_target(root, "v0.0.38") == head, f"the tag must point at HEAD ({head}), got: {get_tag_target(root, 'v0.0.38')}")
        assert_true(get_tag_message(root, "v0.0.38") == "Release v0.0.38", f"the tag message should be 'Release v0.0.38', got: {get_tag_message(root, 'v0.0.38')}")
        assert_true("version: 0.0.38" in output, f"output should report the version, got: {output}")
        assert_true("tag: v0.0.38" in output, f"output should report the tag, got: {output}")
    finally:
        remove_temp_root(root)


def case_tags_the_requested_commit(python: str) -> None:
    root = new_tag_fixture()
    try:
        first_sha = get_head_sha(root)
        write_fixture_file(root, "docs-note.txt", "later commit")
        git_spawn(root, "add", "-A")
        git_spawn(root, "commit", "--quiet", "-m", "Later commit")
        assert_true(get_head_sha(root) != first_sha, "the fixture should have a second commit")
        _, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38", "--commit", first_sha])
        assert_exit_code(0, "tagging an explicit commit should succeed", exit_code)
        assert_true(get_tag_target(root, "v0.0.38") == first_sha, f"the tag must point at the requested commit ({first_sha}), got: {get_tag_target(root, 'v0.0.38')}")
    finally:
        remove_temp_root(root)


def case_supports_prerelease_versions(python: str) -> None:
    root = new_tag_fixture(version="0.0.38-rc.1")
    try:
        _, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38-rc.1"])
        assert_exit_code(0, "prerelease tagging should succeed", exit_code)
        tags = get_tag_names(root)
        assert_true(len(tags) == 1 and tags[0] == "v0.0.38-rc.1", f"exactly one tag v0.0.38-rc.1 should exist, got: {', '.join(tags)}")
    finally:
        remove_temp_root(root)


def case_dry_run_verifies_without_creating_a_tag(python: str) -> None:
    root = new_tag_fixture()
    try:
        before = get_fixture_hash_text(root)
        output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38", "--dry-run"])
        assert_exit_code(0, "dry-run should succeed", exit_code)
        assert_true("dry-run" in output, f"output should carry the dry-run marker, got: {output}")
        assert_true(len(get_tag_names(root)) == 0, "dry-run must not create a tag")
        assert_true(get_fixture_hash_text(root) == before, "dry-run must not mutate files")
    finally:
        remove_temp_root(root)


def case_fails_when_branch_does_not_match(python: str) -> None:
    cases = [
        {"name": "older version branch", "arguments": ["--branch", "release/v0.0.37"]},
        {"name": "newer version branch", "arguments": ["--branch", "release/v0.0.39"]},
        {"name": "bare version branch", "arguments": ["--branch", "0.0.38"]},
        {"name": "main branch", "arguments": ["--branch", "main"]},
    ]
    for case in cases:
        root = new_tag_fixture()
        try:
            before = get_fixture_hash_text(root)
            output, exit_code = invoke_tag(python, root, case["arguments"])
            assert_exit_code(1, f"branch mismatch ({case['name']}) should fail", exit_code)
            assert_true("does not match the release branch" in output, f"error should explain the branch mismatch ({case['name']}), got: {output}")
            assert_true(len(get_tag_names(root)) == 0, f"a rejected run must not create a tag ({case['name']})")
            assert_true(get_fixture_hash_text(root) == before, f"a rejected run must not mutate files ({case['name']})")
        finally:
            remove_temp_root(root)


def case_fails_when_changelog_section_is_missing(python: str) -> None:
    changelog = "# Changelog\n\n## [Unreleased]\n\n- Entry."
    root = new_tag_fixture(changelog=changelog)
    try:
        output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38"])
        assert_exit_code(1, "a missing version section should fail", exit_code)
        assert_true("## [0.0.38] - YYYY-MM-DD" in output, f"error should name the missing section, got: {output}")
        assert_true(len(get_tag_names(root)) == 0, "a rejected run must not create a tag")
    finally:
        remove_temp_root(root)


def case_fails_when_changelog_section_is_duplicated(python: str) -> None:
    changelog = "# Changelog\n\n## [0.0.38] - 2026-09-15\n\n- First section.\n\n## [0.0.38] - 2026-09-16\n\n- Second section."
    root = new_tag_fixture(changelog=changelog)
    try:
        output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38"])
        assert_exit_code(1, "a duplicated version section should fail", exit_code)
        assert_true("exactly one is required" in output, f"error should explain the duplicate sections, got: {output}")
        assert_true(len(get_tag_names(root)) == 0, "a rejected run must not create a tag")
    finally:
        remove_temp_root(root)


def case_fails_when_changelog_section_has_no_notes(python: str) -> None:
    changelog = "# Changelog\n\n## [0.0.38] - 2026-09-16\n\n## [0.0.37] - 2026-08-01\n\n- Old entry."
    root = new_tag_fixture(changelog=changelog)
    try:
        output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38"])
        assert_exit_code(1, "an empty version section should fail", exit_code)
        assert_true("no release notes" in output, f"error should explain the missing notes, got: {output}")
        assert_true(len(get_tag_names(root)) == 0, "a rejected run must not create a tag")
    finally:
        remove_temp_root(root)


def case_fails_when_tag_already_exists(python: str) -> None:
    root = new_tag_fixture()
    try:
        expected_target = get_head_sha(root)
        git_spawn(root, "tag", "-a", "v0.0.38", "-m", "Pre-existing tag", "HEAD")
        output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38"])
        assert_exit_code(1, "an existing tag should fail closed", exit_code)
        assert_true("already exists" in output, f"error should name the existing tag, got: {output}")
        tags = get_tag_names(root)
        assert_true(len(tags) == 1, f"no second tag may be created, got: {', '.join(tags)}")
        assert_true(get_tag_message(root, "v0.0.38") == "Pre-existing tag", "the pre-existing tag must not be retargeted or rewritten")
        assert_true(get_tag_target(root, "v0.0.38") == expected_target, "the pre-existing tag must keep its target")
    finally:
        remove_temp_root(root)


def case_fails_when_working_tree_is_dirty(python: str) -> None:
    root = new_tag_fixture()
    try:
        write_fixture_file(root, "uncommitted-note.txt", "untracked")
        output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38"])
        assert_exit_code(1, "a dirty working tree should fail closed", exit_code)
        assert_true("working tree is dirty" in output, f"error should explain the dirty tree, got: {output}")
        assert_true(len(get_tag_names(root)) == 0, "a rejected run must not create a tag")
    finally:
        remove_temp_root(root)


@dataclass(frozen=True)
class ArgumentCase:
    """One rejected argument vector the release script owes a failure."""

    name: str
    arguments: Sequence[str]


def case_rejects_invalid_arguments(python: str) -> None:
    cases = [
        ArgumentCase(name="missing branch", arguments=[]),
        ArgumentCase(name="unknown flag", arguments=["--branch", "release/v0.0.38", "--bump", "patch"]),
        ArgumentCase(name="flag without value", arguments=["--branch"]),
    ]
    for case in cases:
        root = new_tag_fixture()
        try:
            output, exit_code = invoke_tag(python, root, case.arguments)
            assert_exit_code(1, f"invalid arguments ({case.name}) should fail", exit_code)
            assert_true("error: " in output, f"output should carry the error prefix ({case.name}), got: {output}")
            assert_true(len(get_tag_names(root)) == 0, f"a rejected run must not create a tag ({case.name})")
        finally:
            remove_temp_root(root)


def case_fails_when_package_version_is_unsupported_semver(python: str) -> None:
    root = new_tag_fixture()
    try:
        write_fixture_file(root, "package.json", '{\n  "name": "com.test.fixture",\n  "version": "0.0.38-build.2"\n}\n')
        git_spawn(root, "add", "-A")
        git_spawn(root, "commit", "--quiet", "-m", "Bad version")
        output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38-build.2"])
        assert_exit_code(1, "unsupported semver should fail", exit_code)
        assert_true("not supported semver" in output, f"error should explain the unsupported semver, got: {output}")
        assert_true(len(get_tag_names(root)) == 0, "a rejected run must not create a tag")
    finally:
        remove_temp_root(root)


def case_fails_when_required_files_are_missing(python: str) -> None:
    cases = [
        {"name": "missing package.json", "relative_path": "package.json"},
        {"name": "missing CHANGELOG.md", "relative_path": "CHANGELOG.md"},
    ]
    for case in cases:
        root = new_tag_fixture()
        try:
            (root / case["relative_path"]).unlink()
            output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38"])
            assert_exit_code(1, f"missing required file ({case['name']}) should fail", exit_code)
            assert_true("Cannot read required release file" in output, f"error should name the missing file ({case['name']}), got: {output}")
            assert_true(len(get_tag_names(root)) == 0, f"a rejected run must not create a tag ({case['name']})")
        finally:
            remove_temp_root(root)


def case_fails_when_root_is_not_a_repository(python: str) -> None:
    root = new_tag_fixture(no_git=True)
    try:
        output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38"])
        assert_exit_code(1, "a non-git root should fail closed", exit_code)
        assert_true("not a git work tree" in output, f"error should explain the non-git root, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_commit_argument_does_not_resolve(python: str) -> None:
    root = new_tag_fixture()
    try:
        output, exit_code = invoke_tag(python, root, ["--branch", "release/v0.0.38", "--commit", "deadbeef"])
        assert_exit_code(1, "an unresolvable commit should fail", exit_code)
        assert_true("Cannot resolve" in output, f"error should explain the unresolvable commit, got: {output}")
        assert_true(len(get_tag_names(root)) == 0, "a rejected run must not create a tag")
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== tag-release self-tests ==")

    python = find_python()

    run_case("Creates_AnnotatedTag_AtHead_OnTheMergedReleaseCommit", lambda: case_creates_annotated_tag_at_head(python))
    run_case("Tags_TheRequestedCommit_WhenCommitArgumentIsGiven", lambda: case_tags_the_requested_commit(python))
    run_case("Supports_PrereleaseVersions", lambda: case_supports_prerelease_versions(python))
    run_case("DryRun_Verifies_WithoutCreatingATag", lambda: case_dry_run_verifies_without_creating_a_tag(python))
    run_case("Fails_When_BranchDoesNotMatchPackageVersion", lambda: case_fails_when_branch_does_not_match(python))
    run_case("Fails_When_ChangelogSection_IsMissing", lambda: case_fails_when_changelog_section_is_missing(python))
    run_case("Fails_When_ChangelogSection_IsDuplicated", lambda: case_fails_when_changelog_section_is_duplicated(python))
    run_case("Fails_When_ChangelogSection_HasNoNotes", lambda: case_fails_when_changelog_section_has_no_notes(python))
    run_case("Fails_When_TagAlreadyExists_AndNeverRetargets", lambda: case_fails_when_tag_already_exists(python))
    run_case("Fails_When_WorkingTreeIsDirty", lambda: case_fails_when_working_tree_is_dirty(python))
    run_case("Rejects_InvalidArguments", lambda: case_rejects_invalid_arguments(python))
    run_case("Fails_When_PackageVersion_IsUnsupportedSemver", lambda: case_fails_when_package_version_is_unsupported_semver(python))
    run_case("Fails_When_RequiredFiles_AreMissing", lambda: case_fails_when_required_files_are_missing(python))
    run_case("Fails_When_RootIsNotARepository", lambda: case_fails_when_root_is_not_a_repository(python))
    run_case("Fails_When_CommitArgument_DoesNotResolve", lambda: case_fails_when_commit_argument_does_not_resolve(python))

    failures = failure_count()
    print(f"== tag-release: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
