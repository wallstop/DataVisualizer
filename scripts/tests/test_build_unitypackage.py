#!/usr/bin/env python3
"""Unity package builder self-tests (typed Python port of ``test-build-unitypackage.ps1``, issue #171).

Every case from the PowerShell original is kept with the same name and
assertions; only the driver language changed. The cases drive
``scripts/release/build_unitypackage.py`` as a Python child process and inspect the
built tar the same way.
"""

from __future__ import annotations

import hashlib
import os
import re
import subprocess
import sys
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
BUILD_SCRIPT = SCRIPTS_DIR / "release" / "build_unitypackage.py"

LONG_NAME = "L" + "o" * 76 + "ng.cs"

# Fixed 32-hex identities carried by the fixture's committed .meta files.
PACKAGE_META_GUID = "c1a2975a5c0e4fdd9e3f8a7b6c5d4e01"
README_META_GUID = "c1a2975a5c0e4fdd9e3f8a7b6c5d4e02"
EDITOR_META_GUID = "c1a2975a5c0e4fdd9e3f8a7b6c5d4e03"
SCRIPT_META_GUID = "c1a2975a5c0e4fdd9e3f8a7b6c5d4e04"
LONG_META_GUID = "c1a2975a5c0e4fdd9e3f8a7b6c5d4e05"

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


def new_build_fixture(version: str = "0.0.38", package_json: Optional[str] = None, no_git: bool = False, skip_commit: bool = False) -> Path:
    root = new_temp_root(prefix="unitypackage-fixture-")
    if not no_git:
        _, exit_code = git_capture(root, "init", "--quiet")
        if exit_code != 0:
            raise RuntimeError("git init failed for fixture")
        git_spawn(root, "config", "user.name", "Fixture Tester")
        git_spawn(root, "config", "user.email", "fixture@example.invalid")
    if package_json is None:
        package_json = DEFAULT_PACKAGE_JSON.replace('"0.0.38"', f'"{version}"')
    write_fixture_file(root, "package.json", package_json)
    write_fixture_file(root, "package.json.meta", new_meta_content(PACKAGE_META_GUID))
    write_fixture_file(root, "README.md", "# Fixture readme")
    write_fixture_file(root, "README.md.meta", new_meta_content(README_META_GUID))
    write_fixture_file(root, "Editor.meta", new_meta_content(EDITOR_META_GUID))
    write_fixture_file(root, "Editor/A.cs", "// fixture content")
    write_fixture_file(root, "Editor/A.cs.meta", new_meta_content(SCRIPT_META_GUID))
    write_fixture_file(root, f"Editor/Deep/{LONG_NAME}", "// long path content")
    write_fixture_file(root, f"Editor/Deep/{LONG_NAME}.meta", new_meta_content(LONG_META_GUID))
    if not no_git and not skip_commit:
        git_spawn(root, "add", "-A")
        exit_code = git_spawn(root, "commit", "--quiet", "-m", "Prepare release")
        if exit_code != 0:
            raise RuntimeError("git commit failed for fixture")
    return root


def invoke_build(python: str, root: Path, arguments: Sequence[str]) -> Tuple[str, int]:
    return run_tool_output([python, str(BUILD_SCRIPT), "--root", str(root), *arguments])


def get_fixture_file_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def get_archive_members(archive: Path) -> List[str]:
    # GNU tar parses the first colon in the -f archive name as a remote host spec, so a
    # Windows drive path fails with 'Cannot connect to C: resolve failed'; list from the
    # archive's directory under its leaf name, mirroring runTar in the validator.
    completed = subprocess.run(
        ["tar", "-tzf", archive.name],
        cwd=str(archive.parent),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return [line.strip() for line in (completed.stdout or "").splitlines() if line.strip()]


def case_builds_deterministic_archive(python: str) -> None:
    root = new_build_fixture()
    try:
        first_output = root / "out-first"
        second_output = root / "out-second"
        output, exit_code = invoke_build(python, root, ["--output", str(first_output)])
        assert_exit_code(0, "a valid fixture should build", exit_code)
        assert_true(
            "package_file: com.test.fixture-0.0.38.unitypackage" in output,
            f"output should report the archive name, got: {output}",
        )
        assert_true(
            "entries: 5" in output,
            f"output should report the four non-meta files plus the Editor folder, got: {output}",
        )
        assert_true("output: " in output, f"output should report the output directory, got: {output}")

        archive = first_output / "com.test.fixture-0.0.38.unitypackage"
        long_staged = f"Packages/com.test.fixture/Editor/Deep/{LONG_NAME}"
        # GUID directory names must equal the committed .meta guid fields.
        expected_guids = [
            {"guid": EDITOR_META_GUID, "staged": "Packages/com.test.fixture/Editor/", "kind": "folder", "meta_content": new_meta_content(EDITOR_META_GUID), "relative": "Editor"},
            {"guid": SCRIPT_META_GUID, "staged": "Packages/com.test.fixture/Editor/A.cs", "kind": "file", "meta_content": new_meta_content(SCRIPT_META_GUID), "relative": "Editor/A.cs"},
            {"guid": LONG_META_GUID, "staged": long_staged, "kind": "file", "meta_content": new_meta_content(LONG_META_GUID), "relative": f"Editor/Deep/{LONG_NAME}"},
            {"guid": README_META_GUID, "staged": "Packages/com.test.fixture/README.md", "kind": "file", "meta_content": new_meta_content(README_META_GUID), "relative": "README.md"},
            {"guid": PACKAGE_META_GUID, "staged": "Packages/com.test.fixture/package.json", "kind": "file", "meta_content": new_meta_content(PACKAGE_META_GUID), "relative": "package.json"},
        ]

        members = get_archive_members(archive)
        for expected in expected_guids:
            guid = expected["guid"]
            group = [member for member in members if member.startswith(f"{guid}/")]
            if expected["kind"] == "folder":
                assert_true(
                    len(group) == 2 and group[0] == f"{guid}/pathname" and group[1] == f"{guid}/asset.meta",
                    f"folder entry {expected['staged']} should carry pathname + asset.meta only, got: {', '.join(group)}",
                )
            else:
                assert_true(
                    len(group) == 3 and group[0] == f"{guid}/pathname" and group[1] == f"{guid}/asset" and group[2] == f"{guid}/asset.meta",
                    f"file entry {expected['staged']} should carry pathname + asset + asset.meta, got: {', '.join(group)}",
                )
        assert_true(
            len(members) == 14,
            f"the archive should carry 4x3 + 1x2 members, got {len(members)}: {', '.join(members)}",
        )

        extract = root / "extract"
        extract.mkdir(parents=True)
        # PowerShell-spawned tar cannot take Windows absolute paths: the -f name hits the
        # 'C:' remote-host parse and a -C path arrives mangled by the MSYS argument
        # conversion. Extract from inside the target directory and pass the archive by a
        # colon-free relative path, mirroring the cwd pattern of get_archive_members.
        relative_archive = os.path.relpath(archive, extract).replace("\\", "/")
        completed = subprocess.run(["tar", "-xzf", relative_archive], cwd=str(extract), check=False)
        if completed.returncode != 0:
            raise RuntimeError("tar extraction failed")
        for expected in expected_guids:
            guid = expected["guid"]
            pathname_content = (extract / guid / "pathname").read_text(encoding="utf-8")
            assert_true(pathname_content == expected["staged"] + "\n", f"pathname should carry the staged path, got: {pathname_content}")
            meta_content = (extract / guid / "asset.meta").read_text(encoding="utf-8")
            assert_true(meta_content == expected["meta_content"], f"asset.meta for {expected['staged']} should byte-match the committed meta, got: {meta_content}")
            if expected["kind"] == "folder":
                continue
            asset_hash = get_fixture_file_hash(extract / guid / "asset")
            assert_true(
                asset_hash == get_fixture_file_hash(root / str(expected["relative"])),
                f"asset for {expected['staged']} should byte-match the tracked file",
            )

        second_output_archive = second_output / "com.test.fixture-0.0.38.unitypackage"
        _, exit_code = invoke_build(python, root, ["--output", str(second_output)])
        assert_exit_code(0, "a second build should succeed", exit_code)
        assert_true(
            get_fixture_file_hash(archive) == get_fixture_file_hash(second_output_archive),
            "two builds of the same tree must be byte-identical",
        )

        checksum_line = Path(f"{archive}.sha256").read_text(encoding="utf-8").strip()
        assert_true(
            re.search("^" + get_fixture_file_hash(archive) + "  com\\.test\\.fixture-0\\.0\\.38\\.unitypackage$", checksum_line, re.IGNORECASE) is not None,
            f"the .sha256 sidecar should carry the archive hash in sha256sum format, got: {checksum_line}",
        )
    finally:
        remove_temp_root(root)


def case_writes_the_default_output_under_root_dist(python: str) -> None:
    root = new_build_fixture()
    try:
        output, exit_code = invoke_build(python, root, [])
        assert_exit_code(0, "the default invocation should build", exit_code)
        assert_true(
            (root / "dist/com.test.fixture-0.0.38.unitypackage").exists(),
            f"the archive should land under <root>/dist by default, got: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_when_payload_file_has_no_meta_companion(python: str) -> None:
    root = new_build_fixture()
    try:
        write_fixture_file(root, "Editor/B.cs", "// missing meta")
        git_spawn(root, "add", "-A")
        git_spawn(root, "commit", "--quiet", "-m", "Add unmetaed file")
        output, exit_code = invoke_build(python, root, [])
        assert_exit_code(1, "a payload file without .meta should fail closed", exit_code)
        assert_true("no committed '.meta' companion" in output, f"error should explain the missing meta companion, got: {output}")
        assert_true("B.cs" in output, f"error should name the offending file, got: {output}")
        assert_true(not (root / "dist").exists(), "a failed build must not write any archive")
    finally:
        remove_temp_root(root)


def case_fails_when_meta_targets_no_tracked_file(python: str) -> None:
    cases = [
        {"name": "orphan file meta", "relative_path": "Editor/Orphan.meta"},
        {"name": "orphan deep meta", "relative_path": "Editor/Orphan/Child.meta"},
    ]
    for case in cases:
        root = new_build_fixture()
        try:
            write_fixture_file(root, str(case["relative_path"]), "meta: orphan")
            git_spawn(root, "add", "-A")
            git_spawn(root, "commit", "--quiet", "-m", "Add orphan meta")
            output, exit_code = invoke_build(python, root, [])
            assert_exit_code(1, f"an orphan meta ({case['name']}) should fail closed", exit_code)
            assert_true(
                "target neither a tracked file nor a directory" in output,
                f"error should explain the orphan meta ({case['name']}), got: {output}",
            )
        finally:
            remove_temp_root(root)


def case_fails_when_package_json_is_invalid_or_selects_nothing(python: str) -> None:
    cases = [
        {
            "name": "empty files",
            "package_json": '{\n  "name": "com.test.fixture",\n  "version": "0.0.38",\n  "files": []\n}\n',
            "pattern": "non-empty 'files' publish allowlist",
        },
        {
            "name": "unsupported semver",
            "package_json": '{\n  "name": "com.test.fixture",\n  "version": "0.0.38-build.2",\n  "files": ["Editor"]\n}\n',
            "pattern": "not supported semver",
        },
    ]
    for case in cases:
        root = new_build_fixture(package_json=case["package_json"])
        try:
            output, exit_code = invoke_build(python, root, [])
            assert_exit_code(1, f"invalid package.json ({case['name']}) should fail", exit_code)
            assert_true(
                case["pattern"] in output,
                f"error should explain the failure ({case['name']}) with '{case['pattern']}', got: {output}",
            )
        finally:
            remove_temp_root(root)


def case_fails_when_root_is_not_a_repository(python: str) -> None:
    root = new_build_fixture(no_git=True)
    try:
        output, exit_code = invoke_build(python, root, [])
        assert_exit_code(1, "a non-git root should fail closed", exit_code)
        assert_true("not a git work tree" in output, f"error should explain the non-git root, got: {output}")
    finally:
        remove_temp_root(root)


def case_rejects_invalid_arguments(python: str) -> None:
    cases = [
        {"name": "unknown flag", "arguments": ["--bump", "patch"]},
        {"name": "flag without value", "arguments": ["--output"]},
    ]
    for case in cases:
        root = new_build_fixture()
        try:
            output, exit_code = invoke_build(python, root, case["arguments"])
            assert_exit_code(1, f"invalid arguments ({case['name']}) should fail", exit_code)
            assert_true("error: " in output, f"output should carry the error prefix ({case['name']}), got: {output}")
        finally:
            remove_temp_root(root)


def case_fails_when_used_meta_carries_no_guid_field(python: str) -> None:
    root = new_build_fixture()
    try:
        write_fixture_file(root, "Editor/A.cs.meta", "meta without guid")
        git_spawn(root, "add", "-A")
        git_spawn(root, "commit", "--quiet", "-m", "Drop guid field")
        output, exit_code = invoke_build(python, root, [])
        assert_exit_code(1, "a used meta without a guid field should fail closed", exit_code)
        assert_true("no well-formed 'guid:' field" in output, f"error should explain the missing guid field, got: {output}")
        assert_true("A.cs.meta" in output, f"error should name the offending meta, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_two_metas_carry_the_same_guid(python: str) -> None:
    root = new_build_fixture()
    try:
        write_fixture_file(root, "Editor/A.cs.meta", new_meta_content(README_META_GUID))
        git_spawn(root, "add", "-A")
        git_spawn(root, "commit", "--quiet", "-m", "Duplicate a guid")
        output, exit_code = invoke_build(python, root, [])
        assert_exit_code(1, "a duplicated guid should fail closed", exit_code)
        assert_true("reuses guid" in output, f"error should explain the duplicate guid, got: {output}")
        assert_true(README_META_GUID in output, f"error should name the duplicated guid, got: {output}")
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== build-unitypackage self-tests ==")

    python = find_python()

    run_case("Builds_DeterministicArchive_WithStandardGuidEntries", lambda: case_builds_deterministic_archive(python))
    run_case("Writes_The_Default_Output_Under_Root_Dist", lambda: case_writes_the_default_output_under_root_dist(python))
    run_case("Fails_When_PayloadFileHasNoMetaCompanion", lambda: case_fails_when_payload_file_has_no_meta_companion(python))
    run_case("Fails_When_MetaTargetsNoTrackedFileOrDirectory", lambda: case_fails_when_meta_targets_no_tracked_file(python))
    run_case("Fails_When_PackageJsonIsInvalidOrSelectsNothing", lambda: case_fails_when_package_json_is_invalid_or_selects_nothing(python))
    run_case("Fails_When_RootIsNotARepository", lambda: case_fails_when_root_is_not_a_repository(python))
    run_case("Rejects_InvalidArguments", lambda: case_rejects_invalid_arguments(python))
    run_case("Fails_When_UsedMetaCarriesNoGuidField", lambda: case_fails_when_used_meta_carries_no_guid_field(python))
    run_case("Fails_When_TwoMetasCarryTheSameGuid", lambda: case_fails_when_two_metas_carry_the_same_guid(python))

    failures = failure_count()
    print(f"== build-unitypackage: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
