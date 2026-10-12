#!/usr/bin/env python3
"""Release verification self-tests (typed Python port of ``test-verify-release.ps1``, issue #171).

Every case from the PowerShell original is kept with the same name and
assertions; only the driver language changed. The cases drive
``scripts/release/verify_release.py`` as Python child processes the same way.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
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
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
VERIFY_SCRIPT = SCRIPTS_DIR / "release" / "verify_release.py"


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


def new_verify_fixture(version: str = "0.0.38", package_json: Optional[str] = None, no_git: bool = False, skip_commit: bool = False) -> Path:
    root = new_temp_root(prefix="verify-fixture-")
    if not no_git:
        _, exit_code = git_capture(root, "init", "--quiet")
        if exit_code != 0:
            raise RuntimeError("git init failed for fixture")
        git_spawn(root, "config", "user.name", "Fixture Tester")
        git_spawn(root, "config", "user.email", "fixture@example.invalid")
    if package_json is None:
        package_json = '{\n  "name": "com.test.fixture",\n  "version": "' + version + '",\n  "files": [\n    "Editor",\n    "Editor.meta"\n  ]\n}\n'
    write_fixture_file(root, "package.json", package_json)
    write_fixture_file(root, "Editor/A.cs", "// fixture content")
    write_fixture_file(root, "Editor/A.cs.meta", "meta: A.cs")
    write_fixture_file(root, "Editor.meta", "meta: Editor")
    if not no_git and not skip_commit:
        git_spawn(root, "add", "-A")
        exit_code = git_spawn(root, "commit", "--quiet", "-m", "Prepare release")
        if exit_code != 0:
            raise RuntimeError("git commit failed for fixture")
    return root


def invoke_verify(python: str, root: Path, arguments: Sequence[str]) -> Tuple[str, int]:
    return run_tool_output([python, str(VERIFY_SCRIPT), "--root", str(root), *arguments])


def new_hand_tarball(root: Path, name: str, include_paths: Sequence[str], extra_paths: Sequence[str] = ()) -> Path:
    staging = root / "staging"
    (staging / "package").mkdir(parents=True)
    for relative in include_paths:
        source = root / relative
        target = staging / "package" / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy(source, target)
    for relative in extra_paths:
        write_fixture_file(staging, f"package/{relative}", "extra")
    tarball = root / name
    # GNU tar parses the first colon in the -f archive name as a remote host spec, so a
    # Windows drive path fails with 'Cannot connect to C: resolve failed'; pack from the
    # tarball's directory under its leaf name, mirroring runTar in the validator.
    completed = subprocess.run(
        ["tar", "-czf", tarball.name, "-C", str(staging), "package"],
        cwd=str(tarball.parent),
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError("tar failed for handcrafted fixture tarball")
    return tarball


def case_verifies_matching_tag_and_reports_publish_outputs(python: str) -> None:
    root = new_verify_fixture()
    try:
        output, exit_code = invoke_verify(python, root, ["--tag", "v0.0.38"])
        assert_exit_code(0, "a matching tag and payload should verify", exit_code)
        assert_true("pkg: com.test.fixture" in output, f"output should report the package name, got: {output}")
        assert_true("version: 0.0.38" in output, f"output should report the version, got: {output}")
        assert_true("npm_tag: latest" in output, f"stable version should resolve to the latest dist-tag, got: {output}")
        assert_true("package_file: com.test.fixture-0.0.38.tgz" in output, f"output should report the packed tarball, got: {output}")
        assert_true("entries: 4" in output, f"output should report all four allowlisted entries, got: {output}")
        assert_true((root / "com.test.fixture-0.0.38.tgz").exists(), "the packed tarball should exist for the publish step")
    finally:
        remove_temp_root(root)


def case_resolves_next_dist_tag_for_restricted_prereleases(python: str) -> None:
    cases = ["0.0.38-rc.1", "0.0.38-alpha", "0.0.39-beta.2", "1.0.0-preview"]
    for version in cases:
        root = new_verify_fixture(version=version)
        try:
            output, exit_code = invoke_verify(python, root, ["--tag", "v" + version])
            assert_exit_code(0, f"prerelease {version} should verify", exit_code)
            assert_true("npm_tag: next" in output, f"prerelease {version} should resolve to the next dist-tag, got: {output}")
        finally:
            remove_temp_root(root)


def case_verifies_an_explicit_package_file_without_packing(python: str) -> None:
    root = new_verify_fixture()
    try:
        output, exit_code = invoke_verify(python, root, ["--tag", "v0.0.38"])
        assert_exit_code(0, "the initial pack should verify", exit_code)
        tarball = root / "com.test.fixture-0.0.38.tgz"
        before = os.stat(tarball).st_mtime_ns
        time.sleep(0.01)
        output, exit_code = invoke_verify(python, root, ["--tag", "v0.0.38", "--package-file", "com.test.fixture-0.0.38.tgz"])
        assert_exit_code(0, "an explicit npm-produced tarball should verify", exit_code)
        assert_true(os.stat(tarball).st_mtime_ns == before, "verify mode with --package-file must not repack the tree")
        assert_true("entries: 4" in output, f"the tarball entry count should be reported, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_tag_does_not_match_package_version(python: str) -> None:
    cases = [
        {"name": "older tag", "tag": "v0.0.37"},
        {"name": "newer tag", "tag": "v0.0.39"},
        {"name": "bare tag", "tag": "0.0.38"},
        {"name": "main ref", "tag": "main"},
    ]
    for case in cases:
        root = new_verify_fixture()
        try:
            output, exit_code = invoke_verify(python, root, ["--tag", case["tag"]])
            assert_exit_code(1, f"tag mismatch ({case['name']}) should fail", exit_code)
            assert_true("does not match the package version" in output, f"error should explain the tag mismatch ({case['name']}), got: {output}")
            assert_true("error: " in output, f"output should carry the error prefix ({case['name']}), got: {output}")
        finally:
            remove_temp_root(root)


def case_fails_when_tarball_carries_unallowlisted_file(python: str) -> None:
    root = new_verify_fixture()
    try:
        tarball = new_hand_tarball(
            root,
            "junk.tgz",
            ["package.json", "Editor.meta", "Editor/A.cs", "Editor/A.cs.meta"],
            ["Extra.txt"],
        )
        output, exit_code = invoke_verify(python, root, ["--tag", "v0.0.38", "--package-file", str(tarball)])
        assert_exit_code(1, "an unallowlisted tarball entry should fail closed", exit_code)
        assert_true("outside the publish allowlist" in output, f"error should explain the allowlist violation, got: {output}")
        assert_true("Extra.txt" in output, f"error should name the offending file, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_allowlisted_payload_file_is_missing(python: str) -> None:
    root = new_verify_fixture()
    try:
        tarball = new_hand_tarball(root, "shrunk.tgz", ["package.json", "Editor/A.cs", "Editor/A.cs.meta"])
        output, exit_code = invoke_verify(python, root, ["--tag", "v0.0.38", "--package-file", str(tarball)])
        assert_exit_code(1, "a missing allowlisted payload file should fail closed", exit_code)
        assert_true("silently shrink" in output, f"error should explain the shrunk payload, got: {output}")
        assert_true("Editor.meta" in output, f"error should name the missing file, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_package_version_is_unsupported_semver(python: str) -> None:
    root = new_verify_fixture(
        package_json=(
            '{\n  "name": "com.test.fixture",\n  "version": "0.0.38-build.2",\n  "files": ["Editor"]\n}\n'
        )
    )
    try:
        output, exit_code = invoke_verify(python, root, ["--tag", "v0.0.38-build.2"])
        assert_exit_code(1, "unsupported semver should fail", exit_code)
        assert_true("not supported semver" in output, f"error should explain the unsupported semver, got: {output}")
    finally:
        remove_temp_root(root)


@dataclass(frozen=True)
class ArgumentCase:
    """One rejected argument vector the release script owes a failure."""

    name: str
    arguments: Sequence[str]


@dataclass(frozen=True)
class PackageJsonCase:
    """One broken package.json shape the verifier owes a named failure."""

    name: str
    package_json: Optional[str]
    removed: Optional[str]
    pattern: str


def case_fails_when_package_json_is_missing_or_incomplete(python: str) -> None:
    cases = [
        PackageJsonCase(name="missing package.json", package_json=None, removed="package.json", pattern="Cannot read required release file"),
        PackageJsonCase(name="invalid JSON", package_json="{not json", removed=None, pattern="not valid JSON"),
        PackageJsonCase(name="missing name", package_json='{\n  "version": "0.0.38",\n  "files": ["Editor"]\n}\n', removed=None, pattern="missing a name field"),
        PackageJsonCase(name="missing version", package_json='{\n  "name": "com.test.fixture",\n  "files": ["Editor"]\n}\n', removed=None, pattern="missing a version field"),
        PackageJsonCase(name="empty files", package_json='{\n  "name": "com.test.fixture",\n  "version": "0.0.38",\n  "files": []\n}\n', removed=None, pattern="non-empty 'files' publish allowlist"),
    ]
    for case in cases:
        root = new_verify_fixture(package_json=case.package_json)
        try:
            if case.removed:
                (root / case.removed).unlink()
            output, exit_code = invoke_verify(python, root, ["--tag", "v0.0.38"])
            assert_exit_code(1, f"invalid package.json ({case.name}) should fail", exit_code)
            assert_true(case.pattern in output, f"error should explain the failure ({case.name}) with '{case.pattern}', got: {output}")
        finally:
            remove_temp_root(root)


def case_fails_when_package_file_does_not_exist(python: str) -> None:
    root = new_verify_fixture()
    try:
        output, exit_code = invoke_verify(python, root, ["--tag", "v0.0.38", "--package-file", "does-not-exist.tgz"])
        assert_exit_code(1, "a missing package file should fail", exit_code)
        assert_true("Cannot read package file" in output, f"error should name the missing package file, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_root_is_not_a_repository(python: str) -> None:
    root = new_verify_fixture(no_git=True)
    try:
        output, exit_code = invoke_verify(python, root, ["--tag", "v0.0.38"])
        assert_exit_code(1, "a non-git root should fail closed", exit_code)
        assert_true("not a git work tree" in output, f"error should explain the non-git root, got: {output}")
    finally:
        remove_temp_root(root)


def case_rejects_invalid_arguments(python: str) -> None:
    cases = [
        ArgumentCase(name="missing tag", arguments=[]),
        ArgumentCase(name="unknown flag", arguments=["--tag", "v0.0.38", "--bump", "patch"]),
        ArgumentCase(name="flag without value", arguments=["--tag"]),
    ]
    for case in cases:
        root = new_verify_fixture()
        try:
            output, exit_code = invoke_verify(python, root, case.arguments)
            assert_exit_code(1, f"invalid arguments ({case.name}) should fail", exit_code)
            assert_true("error: " in output, f"output should carry the error prefix ({case.name}), got: {output}")
        finally:
            remove_temp_root(root)


def main() -> int:
    print("== verify-release self-tests ==")

    python = find_python()

    run_case("Verifies_MatchingTag_AndReportsPublishOutputs", lambda: case_verifies_matching_tag_and_reports_publish_outputs(python))
    run_case("Resolves_Next_DistTag_For_RestrictedPrereleases", lambda: case_resolves_next_dist_tag_for_restricted_prereleases(python))
    run_case("Verifies_An_ExplicitPackageFile_WithoutPacking", lambda: case_verifies_an_explicit_package_file_without_packing(python))
    run_case("Fails_When_TagDoesNotMatchPackageVersion", lambda: case_fails_when_tag_does_not_match_package_version(python))
    run_case("Fails_When_TarballCarriesUnallowlistedFile", lambda: case_fails_when_tarball_carries_unallowlisted_file(python))
    run_case("Fails_When_AllowlistedPayloadFileIsMissing", lambda: case_fails_when_allowlisted_payload_file_is_missing(python))
    run_case("Fails_When_PackageVersionIsUnsupportedSemver", lambda: case_fails_when_package_version_is_unsupported_semver(python))
    run_case("Fails_When_PackageJsonIsMissingOrIncomplete", lambda: case_fails_when_package_json_is_missing_or_incomplete(python))
    run_case("Fails_When_PackageFileDoesNotExist", lambda: case_fails_when_package_file_does_not_exist(python))
    run_case("Fails_When_RootIsNotARepository", lambda: case_fails_when_root_is_not_a_repository(python))
    run_case("Rejects_InvalidArguments", lambda: case_rejects_invalid_arguments(python))

    failures = failure_count()
    print(f"== verify-release: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
