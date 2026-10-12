#!/usr/bin/env python3
"""Unity package validator self-tests (typed Python port of ``test-validate-unitypackage.ps1``, issue #171).

Every case from the PowerShell original is kept with the same name and
assertions; only the driver language changed. The cases drive
``scripts/release/validate_unitypackage.py`` as a Python child process and build the
same hand-made tar fixtures.
"""

from __future__ import annotations

import gzip
import hashlib
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Dict, Sequence, Tuple

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
BUILD_SCRIPT = SCRIPTS_DIR / "release" / "build_unitypackage.py"
VALIDATE_SCRIPT = SCRIPTS_DIR / "release" / "validate_unitypackage.py"

LONG_NAME = "L" + "o" * 76 + "ng.cs"

# Fixed 32-hex identities carried by the fixture's committed .meta files.
PACKAGE_META_GUID = "c1a2975a5c0e4fdd9e3f8a7b6c5d4e01"
README_META_GUID = "c1a2975a5c0e4fdd9e3f8a7b6c5d4e02"
EDITOR_META_GUID = "c1a2975a5c0e4fdd9e3f8a7b6c5d4e03"
SCRIPT_META_GUID = "c1a2975a5c0e4fdd9e3f8a7b6c5d4e04"
LONG_META_GUID = "c1a2975a5c0e4fdd9e3f8a7b6c5d4e05"
STRAY_GUID = "ffffffffffffffffffffffffffffffff"

DEFAULT_PACKAGE_JSON = (
    '{\n'
    '  "name": "com.test.fixture",\n'
    '  "version": "0.0.38",\n'
    '  "files": [\n'
    '    "Editor",\n'
    '    "Editor.meta",\n'
    '    "package.json.meta",\n'
    '    "README.md.meta"\n'
    "  ]\n"
    "}\n"
)


@dataclass(frozen=True)
class FixtureEntry:
    """One fixture payload entry the validator derives, in builder order."""

    guid: str
    relative: str
    folder: bool


@dataclass(frozen=True)
class SidecarCase:
    """One checksum-sidecar mutation the validator must reject by name."""

    name: str
    mutate: Callable[[Path], None]
    pattern: str


@dataclass(frozen=True)
class MemberCase:
    """One hand-built archive whose members a case rearranges before validating."""

    name: str
    mutate: Callable[[Dict[str, str]], None]
    pattern: str


@dataclass(frozen=True)
class ArchiveCase:
    """One corrupt archive body the validator must reject by name."""

    name: str
    payload: bytes
    pattern: str


@dataclass(frozen=True)
class DriftCase:
    """One tracked-tree edit made after the archive was built."""

    name: str
    relative_path: str
    content: str
    pattern: str


def new_meta_content(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
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


def new_validator_fixture(no_git: bool = False) -> Path:
    root = new_temp_root(prefix="unitypackage-validator-")
    if not no_git:
        _, exit_code = git_capture(root, "init", "--quiet")
        if exit_code != 0:
            raise RuntimeError("git init failed for fixture")
        git_spawn(root, "config", "user.name", "Fixture Tester")
        git_spawn(root, "config", "user.email", "fixture@example.invalid")
    write_fixture_file(root, "package.json", DEFAULT_PACKAGE_JSON)
    write_fixture_file(root, "package.json.meta", new_meta_content(PACKAGE_META_GUID))
    write_fixture_file(root, "README.md", "# Fixture readme")
    write_fixture_file(root, "README.md.meta", new_meta_content(README_META_GUID))
    write_fixture_file(root, "Editor.meta", new_meta_content(EDITOR_META_GUID))
    write_fixture_file(root, "Editor/A.cs", "// fixture content")
    write_fixture_file(root, "Editor/A.cs.meta", new_meta_content(SCRIPT_META_GUID))
    write_fixture_file(root, f"Editor/Deep/{LONG_NAME}", "// long path content")
    write_fixture_file(root, f"Editor/Deep/{LONG_NAME}.meta", new_meta_content(LONG_META_GUID))
    if not no_git:
        git_spawn(root, "add", "-A")
        exit_code = git_spawn(root, "commit", "--quiet", "-m", "Prepare release")
        if exit_code != 0:
            raise RuntimeError("git commit failed for fixture")
    return root


def invoke_validator(python: str, root: Path, archive: Path) -> Tuple[str, int]:
    return run_tool_output([python, str(VALIDATE_SCRIPT), "--root", str(root), "--archive", str(archive)])


def invoke_validator_raw(python: str, arguments: Sequence[str]) -> Tuple[str, int]:
    return run_tool_output([python, str(VALIDATE_SCRIPT), *arguments])


def build_test_archive(python: str, root: Path, output: Path) -> Path:
    completed = subprocess.run(
        [python, str(BUILD_SCRIPT), "--root", str(root), "--output", str(output)],
        capture_output=True,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError("the builder failed while preparing a validator fixture")
    return output / "com.test.fixture-0.0.38.unitypackage"


def set_checksum_sidecar(archive_path: Path, hash_text: str, name: str) -> None:
    # newline="" keeps the bytes verbatim (no-BOM UTF-8, like the original's WriteAllText).
    with open(f"{archive_path}.sha256", "w", encoding="utf-8", newline="") as handle:
        handle.write(f"{hash_text}  {name}\n")


def write_checksum_sidecar(archive_path: Path) -> None:
    set_checksum_sidecar(
        archive_path,
        hashlib_sha256(archive_path).lower(),
        archive_path.name,
    )


def hashlib_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def get_staged_path(relative: str, folder: bool = False) -> str:
    staged = f"Packages/com.test.fixture/{relative}"
    if folder:
        staged = staged + "/"
    return staged + "\n"


# The fixture entries the tracked payload derives, in builder order.
FIXTURE_ENTRIES = [
    FixtureEntry(guid=EDITOR_META_GUID, relative="Editor", folder=True),
    FixtureEntry(guid=SCRIPT_META_GUID, relative="Editor/A.cs", folder=False),
    FixtureEntry(guid=LONG_META_GUID, relative=f"Editor/Deep/{LONG_NAME}", folder=False),
    FixtureEntry(guid=README_META_GUID, relative="README.md", folder=False),
    FixtureEntry(guid=PACKAGE_META_GUID, relative="package.json", folder=False),
]


def get_fixture_members(root: Path) -> Dict[str, str]:
    members: Dict[str, str] = {}
    for entry in FIXTURE_ENTRIES:
        members[f"{entry.guid}/pathname"] = get_staged_path(entry.relative, entry.folder)
        if not entry.folder:
            members[f"{entry.guid}/asset"] = (root / entry.relative).read_text(encoding="utf-8")
        members[f"{entry.guid}/asset.meta"] = (root / f"{entry.relative}.meta").read_text(encoding="utf-8")
    return members


def write_member_file(stage: Path, name: str, content: str) -> None:
    full_path = stage / name
    full_path.parent.mkdir(parents=True, exist_ok=True)
    with open(full_path, "w", encoding="utf-8", newline="") as handle:
        handle.write(content)


def new_hand_built_archive(stage: Path, archive_path: Path, members: Dict[str, str], member_order: Sequence[str]) -> None:
    stage.mkdir(parents=True)
    for name in member_order:
        write_member_file(stage, name, members[name])
    # GNU tar parses the first colon in the -f archive name as a remote host spec, so a
    # Windows drive path fails with 'Cannot connect to C: resolve failed'; stage from the
    # archive's directory under its leaf name, mirroring runTar in the validator.
    completed = subprocess.run(
        ["tar", "-czf", archive_path.name, "-C", str(stage), *member_order],
        cwd=str(archive_path.parent),
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError("tar staging failed for the hand-built fixture archive")


def new_empty_tar_bytes() -> bytes:
    # A valid gzip stream over 1024 zero bytes: tar sees two empty blocks, no members.
    return gzip.compress(b"\x00" * 1024)


def remove_asset_member(members: Dict[str, str]) -> None:
    members.pop(f"{SCRIPT_META_GUID}/asset")


def remove_asset_meta_member(members: Dict[str, str]) -> None:
    members.pop(f"{PACKAGE_META_GUID}/asset.meta")


def invoke_hand_built_failure_case(python: str, root: Path, members: Dict[str, str], name: str, pattern: str) -> None:
    work = root / "work"
    file_name = "hand-" + re.sub(r"[^a-z0-9]+", "-", name) + ".unitypackage"
    archive = work / file_name
    new_hand_built_archive(work / "stage", archive, members, sorted(members))
    write_checksum_sidecar(archive)
    output, exit_code = invoke_validator(python, root, archive)
    assert_exit_code(1, f"the hand-built archive ({name}) should fail closed", exit_code)
    assert_true(re.search(pattern, output, re.IGNORECASE) is not None, f"error should explain the failure ({name}) with '{pattern}', got: {output}")


def case_validates_a_builder_archive(python: str) -> None:
    root = new_validator_fixture()
    try:
        archive = build_test_archive(python, root, root / "dist")
        output, exit_code = invoke_validator(python, root, archive)
        assert_exit_code(0, "a builder-produced archive should validate", exit_code)
        assert_true("pkg: com.test.fixture" in output, f"output should report the package name, got: {output}")
        assert_true("version: 0.0.38" in output, f"output should report the package version, got: {output}")
        assert_true("package_file: com.test.fixture-0.0.38.unitypackage" in output, f"output should report the archive name, got: {output}")
        assert_true("entries: 5" in output, f"output should report the four files plus the Editor folder, got: {output}")
        expected_sha = hashlib_sha256(archive).lower()
        assert_true(f"sha256: {expected_sha}" in output, f"output should report the archive hash, got: {output}")
    finally:
        remove_temp_root(root)


def case_validates_a_hand_built_standard_tar(python: str) -> None:
    root = new_validator_fixture()
    try:
        work = root / "work"
        members = get_fixture_members(root)
        archive = work / "hand-built.unitypackage"
        new_hand_built_archive(work / "stage", archive, members, sorted(members))
        write_checksum_sidecar(archive)
        output, exit_code = invoke_validator(python, root, archive)
        assert_exit_code(0, "a standards-conformant hand-built tar should validate", exit_code)
        assert_true("entries: 5" in output, f"output should report the same entry count as the builder, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_the_checksum_sidecar_disagrees(python: str) -> None:
    cases = [
        SidecarCase(
            name="stale hash",
            mutate=lambda archive_path: set_checksum_sidecar(archive_path, "f" * 64, archive_path.name),
            pattern="hashes to",
        ),
        SidecarCase(
            name="missing sidecar",
            mutate=lambda archive_path: Path(f"{archive_path}.sha256").unlink(),
            pattern="checksum sidecar",
        ),
        SidecarCase(
            name="renamed archive",
            mutate=lambda archive_path: set_checksum_sidecar(archive_path, hashlib_sha256(archive_path).lower(), "other.unitypackage"),
            pattern="names 'other\\.unitypackage'",
        ),
    ]
    for case in cases:
        root = new_validator_fixture()
        try:
            archive = build_test_archive(python, root, root / "dist")
            case.mutate(archive)
            output, exit_code = invoke_validator(python, root, archive)
            assert_exit_code(1, f"a disagreeing checksum sidecar ({case.name}) should fail closed", exit_code)
            assert_true(
                re.search(case.pattern, output, re.IGNORECASE) is not None,
                f"error should explain the sidecar failure ({case.name}) with '{case.pattern}', got: {output}",
            )
        finally:
            remove_temp_root(root)


def case_fails_when_the_archive_is_corrupt_or_empty(python: str) -> None:
    cases = [
        ArchiveCase(name="zero-byte file", payload=b"", pattern="Cannot read a non-empty archive"),
        ArchiveCase(name="non-tar garbage", payload="this is not a tarball".encode("utf-8"), pattern="Cannot list the archive"),
        ArchiveCase(name="tar with no members", payload=new_empty_tar_bytes(), pattern="no GUID-directory entries"),
    ]
    for case in cases:
        root = new_validator_fixture()
        try:
            file_name = "bad-" + re.sub(r"[^a-z0-9]+", "-", case.name) + ".unitypackage"
            archive = root / file_name
            archive.write_bytes(case.payload)
            write_checksum_sidecar(archive)
            output, exit_code = invoke_validator(python, root, archive)
            assert_exit_code(1, f"a corrupt or empty archive ({case.name}) should fail closed", exit_code)
            assert_true(
                re.search(case.pattern, output, re.IGNORECASE) is not None,
                f"error should explain the archive failure ({case.name}) with '{case.pattern}', got: {output}",
            )
        finally:
            remove_temp_root(root)


def case_fails_when_the_archive_carries_malformed_members(python: str) -> None:
    cases = [
        MemberCase(
            name="member outside guid directories",
            mutate=lambda members: members.__setitem__("Packages/com.test.fixture/README.md", "staged file content"),
            pattern="unexpected member",
        ),
        MemberCase(
            name="unknown member inside guid directory",
            mutate=lambda members: members.__setitem__(f"{SCRIPT_META_GUID}/extra", "junk"),
            pattern="unexpected member",
        ),
        MemberCase(
            name="folder entry with asset",
            mutate=lambda members: members.__setitem__(f"{EDITOR_META_GUID}/asset", "folder junk"),
            pattern="carries an .asset. member",
        ),
        MemberCase(
            name="file entry without asset",
            mutate=remove_asset_member,
            pattern="missing its .asset. member",
        ),
        MemberCase(
            name="missing asset meta",
            mutate=remove_asset_meta_member,
            pattern="is incomplete",
        ),
    ]
    for case in cases:
        root = new_validator_fixture()
        try:
            members = get_fixture_members(root)
            case.mutate(members)
            invoke_hand_built_failure_case(python, root, members, case.name, case.pattern)
        finally:
            remove_temp_root(root)


def case_fails_when_the_archive_carries_duplicate_members(python: str) -> None:
    root = new_validator_fixture()
    try:
        members = get_fixture_members(root)
        order = sorted(members)
        order.append(f"{SCRIPT_META_GUID}/asset")
        work = root / "work"
        archive = work / "hand-duplicate.unitypackage"
        new_hand_built_archive(work / "stage", archive, members, order)
        write_checksum_sidecar(archive)
        output, exit_code = invoke_validator(python, root, archive)
        assert_exit_code(1, "a duplicated member should fail closed", exit_code)
        assert_true("duplicate member" in output, f"error should explain the duplicate member, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_the_archive_disagrees_with_the_repository(python: str) -> None:
    def rename_to_stray(members: Dict[str, str]) -> None:
        for suffix in ("pathname", "asset", "asset.meta"):
            members[f"{STRAY_GUID}/{suffix}"] = members[f"{SCRIPT_META_GUID}/{suffix}"]
            members.pop(f"{SCRIPT_META_GUID}/{suffix}")

    def duplicate_claims(members: Dict[str, str]) -> None:
        for suffix in ("pathname", "asset", "asset.meta"):
            members[f"{STRAY_GUID}/{suffix}"] = members[f"{SCRIPT_META_GUID}/{suffix}"]

    cases = [
        MemberCase(name="wrong guid directory name", mutate=rename_to_stray, pattern="sits in guid directory"),
        MemberCase(
            name="tampered asset bytes",
            mutate=lambda members: members.__setitem__(f"{SCRIPT_META_GUID}/asset", "// tampered"),
            pattern="asset does not byte-match",
        ),
        MemberCase(
            name="tampered asset meta bytes",
            mutate=lambda members: members.__setitem__(f"{SCRIPT_META_GUID}/asset.meta", "fileFormatVersion: 2"),
            pattern="asset\\.meta does not byte-match",
        ),
        MemberCase(
            name="wrong staged path",
            mutate=lambda members: members.__setitem__(f"{SCRIPT_META_GUID}/pathname", get_staged_path("Editor/B.cs")),
            pattern="outside the tracked payload",
        ),
        MemberCase(
            name="unsafe staged path",
            mutate=lambda members: members.__setitem__(f"{SCRIPT_META_GUID}/pathname", "Packages/../evil\n"),
            pattern="unsafe path",
        ),
        MemberCase(name="duplicate staged path claim", mutate=duplicate_claims, pattern="both stage"),
    ]
    for case in cases:
        root = new_validator_fixture()
        try:
            members = get_fixture_members(root)
            case.mutate(members)
            invoke_hand_built_failure_case(python, root, members, case.name, case.pattern)
        finally:
            remove_temp_root(root)


def case_fails_when_the_tree_drifts_after_build(python: str) -> None:
    cases = [
        DriftCase(name="tracked file without meta", relative_path="Editor/B.cs", content="// drift", pattern="no committed '\\.meta' companion"),
        DriftCase(name="orphan meta", relative_path="Editor/Orphan.meta", content="meta: orphan", pattern="target neither a tracked file nor a directory"),
        DriftCase(name="dropped guid field", relative_path="Editor/A.cs.meta", content="meta without guid", pattern="no well-formed 'guid:' field"),
        DriftCase(name="duplicated guid", relative_path="Editor/A.cs.meta", content=new_meta_content(README_META_GUID), pattern="reuses guid"),
    ]
    for case in cases:
        root = new_validator_fixture()
        try:
            archive = build_test_archive(python, root, root / "dist")
            write_fixture_file(root, case.relative_path, case.content)
            git_spawn(root, "add", "-A")
            exit_code = git_spawn(root, "commit", "--quiet", "-m", "Drift the tree")
            if exit_code != 0:
                raise RuntimeError("git commit failed for drift fixture")
            output, exit_code = invoke_validator(python, root, archive)
            assert_exit_code(1, f"tree drift ({case.name}) should fail closed", exit_code)
            assert_true(
                re.search(case.pattern, output, re.IGNORECASE) is not None,
                f"error should explain the drift ({case.name}) with '{case.pattern}', got: {output}",
            )
        finally:
            remove_temp_root(root)


def case_fails_when_the_root_is_not_a_repository(python: str) -> None:
    root = new_validator_fixture(no_git=True)
    try:
        members = get_fixture_members(root)
        work = root / "work"
        archive = work / "hand-built.unitypackage"
        new_hand_built_archive(work / "stage", archive, members, sorted(members))
        write_checksum_sidecar(archive)
        output, exit_code = invoke_validator(python, root, archive)
        assert_exit_code(1, "a non-git root should fail closed", exit_code)
        assert_true("not a git work tree" in output, f"error should explain the non-git root, got: {output}")
    finally:
        remove_temp_root(root)


def case_rejects_invalid_arguments(python: str) -> None:
    root = new_validator_fixture()
    try:
        cases = [
            {"name": "unknown flag", "arguments": ["--bogus"]},
            {"name": "missing required archive flag", "arguments": ["--root", str(root)]},
        ]
        for case in cases:
            output, exit_code = invoke_validator_raw(python, case["arguments"])
            assert_exit_code(1, f"invalid arguments ({case['name']}) should fail", exit_code)
            assert_true("error: " in output, f"output should carry the error prefix ({case['name']}), got: {output}")
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== validate-unitypackage self-tests ==")

    python = find_python()

    run_case("Validates_A_BuilderArchive_AgainstTheTrackedTree", lambda: case_validates_a_builder_archive(python))
    run_case("Validates_A_HandBuiltStandardTar", lambda: case_validates_a_hand_built_standard_tar(python))
    run_case("Fails_When_TheChecksumSidecarDisagrees", lambda: case_fails_when_the_checksum_sidecar_disagrees(python))
    run_case("Fails_When_TheArchiveIsCorruptOrEmpty", lambda: case_fails_when_the_archive_is_corrupt_or_empty(python))
    run_case("Fails_When_TheArchiveCarriesMalformedMembers", lambda: case_fails_when_the_archive_carries_malformed_members(python))
    run_case("Fails_When_TheArchiveCarriesDuplicateMembers", lambda: case_fails_when_the_archive_carries_duplicate_members(python))
    run_case("Fails_When_TheArchiveDisagreesWithTheRepository", lambda: case_fails_when_the_archive_disagrees_with_the_repository(python))
    run_case("Fails_When_TheTreeDriftsAfterBuild", lambda: case_fails_when_the_tree_drifts_after_build(python))
    run_case("Fails_When_TheRootIsNotARepository", lambda: case_fails_when_the_root_is_not_a_repository(python))
    run_case("Rejects_InvalidArguments", lambda: case_rejects_invalid_arguments(python))

    failures = failure_count()
    print(f"== validate-unitypackage: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
