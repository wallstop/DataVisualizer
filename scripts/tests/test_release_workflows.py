#!/usr/bin/env python3
"""Release workflow contract self-tests (typed Python port of ``test-release-workflows.ps1``, issue #171).

Every case from the PowerShell original is kept with the same name and
assertions; only the driver language changed. The workflow text is read
once at start like the original, and the end-to-end release-chain case
drives the same node scripts over a git fixture.
"""

from __future__ import annotations

import hashlib
import re
import subprocess
import sys
import uuid
from pathlib import Path
from typing import Tuple

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    failure_count,
    find_node,
    new_temp_root,
    remove_temp_root,
    run_case,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
REPO_ROOT = SCRIPTS_DIR.parent
RELEASE_SCRIPTS = SCRIPTS_DIR / "release"
WORKFLOW_PATH = REPO_ROOT / ".github/workflows/release.yml"

workflow = WORKFLOW_PATH.read_text(encoding="utf-8")
_release_marker = workflow.find("  release:\n")
if _release_marker < 0:
    raise RuntimeError(f"{WORKFLOW_PATH} does not carry a '  release:' job marker")
prep = workflow[:_release_marker]
publish = workflow[_release_marker:]


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


def node_spawn(node: str, script: Path, *arguments: str) -> int:
    """Run a release script whose output flows to the test log, like the original."""
    completed = subprocess.run([node, str(script), *arguments], check=False)
    return completed.returncode


def sha256_text(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def case_uses_one_release_workflow() -> None:
    assert_true(re.search("secrets.RELEASE_TOKEN", workflow, re.IGNORECASE) is None, "Release workflow must not need a custom token.")
    assert_true("gh workflow run llm-lint.yml" in prep, "Preparation must start release-branch CI.")
    assert_true(re.search("gh workflow run", publish, re.IGNORECASE) is None, "Tagging and publication must execute in the same job.")
    assert_true("id-token: write" in publish, "npm requires OIDC permission.")
    assert_true(
        re.search("pull_request:", workflow, re.IGNORECASE) is not None and re.search(r"types: \[closed\]", workflow, re.IGNORECASE) is not None,
        "Merged release PRs must trigger publication.",
    )
    for old in ("release-prep.yml", "release-tag.yml", "npm-publish.yml"):
        assert_true(not (WORKFLOW_PATH.parent / old).exists(), f"Obsolete {old} must be removed.")


def case_validates_all_artifacts_before_npm_publication() -> None:
    publication = publish.find("- name: Publish to npm with trusted publishing")
    for step in ("Build the release .unitypackage", "Validate the release .unitypackage", "Prepare release notes and the npm tarball checksum"):
        index = publish.find(f"- name: {step}")
        assert_true(0 <= index < publication, f"{step} must succeed before irreversible npm publication.")


def case_rehearses_without_publishing() -> None:
    assert_true("dry_run:" in workflow, "Publish workflow needs an artifact rehearsal input.")
    for name in ("Publish to npm with trusted publishing", "Create or update the GitHub Release"):
        pattern = re.escape("- name: " + name) + r"\r?\n        if: \$\{\{ inputs.dry_run != true \}\}"
        assert_true(re.search(pattern, publish, re.IGNORECASE) is not None, f"{name} must be disabled during a rehearsal.")
    assert_true("actions/upload-artifact@" in publish, "Rehearsal must retain reviewable artifacts.")


def case_validates_prepared_tree_even_during_dry_run() -> None:
    assert_true(re.search(r"args\+=\(--dry-run\)", prep, re.IGNORECASE) is None, "Runner must validate the bumped tree in a rehearsal.")
    assert_true(
        "extract-release-notes.mjs" in prep and "validate-unitypackage.mjs" in prep,
        "Preparation must validate the future release notes and Unity archive.",
    )
    assert_true("later release-automation slices" not in prep, "Release PR must describe the implemented chain.")
    assert_true("--body-file" in prep, "Release PR body must use a file.")


def case_prepares_tags_and_builds_a_complete_release(node: str) -> None:
    root = new_temp_root(prefix="release-chain-")
    try:
        write_fixture_file(
            root,
            "package.json",
            '{"name":"com.test.fixture","version":"1.2.3","files":["Editor","Editor.meta","package.json.meta","CHANGELOG.md","CHANGELOG.md.meta"]}',
        )
        write_fixture_file(root, ".llm/context.md", "**Version**: 1.2.3")
        write_fixture_file(root, "CHANGELOG.md", "## [Unreleased]\n\n### Fixed\n\n- Fix asset selection.\n")
        write_fixture_file(root, "Editor/fixture.txt", "release asset")
        for path in ("Editor.meta", "Editor/fixture.txt.meta", "package.json.meta", "CHANGELOG.md.meta"):
            guid = uuid.uuid4().hex
            write_fixture_file(root, path, f"fileFormatVersion: 2\nguid: {guid}\n")
        _, exit_code = git_capture(root, "init", "--quiet")
        assert_exit_code(0, "fixture init", exit_code)
        git_spawn(root, "config", "user.name", "Release Fixture")
        git_spawn(root, "config", "user.email", "fixture@example.invalid")
        git_spawn(root, "add", "-A")
        exit_code = git_spawn(root, "commit", "--quiet", "-m", "Initial fixture")
        assert_exit_code(0, "fixture commit", exit_code)
        exit_code = node_spawn(node, RELEASE_SCRIPTS / "prepare-release.mjs", "--root", str(root), "--bump", "patch")
        assert_exit_code(0, "prepare candidate", exit_code)
        git_spawn(root, "add", "-A")
        exit_code = git_spawn(root, "commit", "--quiet", "-m", "Prepare release")
        assert_exit_code(0, "candidate commit", exit_code)
        exit_code = node_spawn(node, RELEASE_SCRIPTS / "tag-release.mjs", "--root", str(root), "--branch", "release/v1.2.4")
        assert_exit_code(0, "create annotated release tag", exit_code)
        kind, _ = git_capture(root, "cat-file", "-t", "refs/tags/v1.2.4")
        assert_true(kind == "tag", "Release tag must be annotated.")
        exit_code = node_spawn(node, RELEASE_SCRIPTS / "verify-release.mjs", "--root", str(root), "--tag", "v1.2.4")
        assert_exit_code(0, "pack and validate npm payload", exit_code)
        dist = root / "dist"
        exit_code = node_spawn(node, RELEASE_SCRIPTS / "build-unitypackage.mjs", "--root", str(root), "--output", str(dist))
        assert_exit_code(0, "build Unity archive", exit_code)
        archive = dist / "com.test.fixture-1.2.4.unitypackage"
        exit_code = node_spawn(node, RELEASE_SCRIPTS / "validate-unitypackage.mjs", "--root", str(root), "--archive", str(archive))
        assert_exit_code(0, "validate Unity archive against tracked tree", exit_code)
        first_hash = sha256_text(archive)
        exit_code = node_spawn(node, RELEASE_SCRIPTS / "build-unitypackage.mjs", "--root", str(root), "--output", str(dist))
        assert_exit_code(0, "repeat archive build", exit_code)
        assert_true(sha256_text(archive).lower() == first_hash.lower(), "Rebuilding the same release must reproduce its Unity archive.")
        notes = dist / "release-notes.md"
        exit_code = node_spawn(node, RELEASE_SCRIPTS / "extract-release-notes.mjs", "--root", str(root), "--version", "1.2.4", "--output", str(notes))
        assert_exit_code(0, "extract release notes", exit_code)
        assert_true("Fix asset selection" in notes.read_text(encoding="utf-8"), "GitHub notes must contain the rotated changelog entry.")
        assert_true((root / "com.test.fixture-1.2.4.tgz").exists(), "npm archive must be ready for publication.")
        assert_true(Path(f"{archive}.sha256").exists(), "Unity archive must have its checksum companion.")
    finally:
        remove_temp_root(root)


def main() -> int:
    node = find_node()

    run_case("Uses_OneReleaseWorkflow_WithBuiltInAuthentication", case_uses_one_release_workflow)
    run_case("Validates_AllArtifacts_BeforeNpmPublication", case_validates_all_artifacts_before_npm_publication)
    run_case("Rehearses_WithoutPublishing", case_rehearses_without_publishing)
    run_case("Validates_PreparedTree_EvenDuringDryRun", case_validates_prepared_tree_even_during_dry_run)
    run_case("Prepares_Tags_AndBuilds_ACompleteRelease", lambda: case_prepares_tags_and_builds_a_complete_release(node))

    if failure_count() > 0:
        return 1
    print("Release workflow contracts: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
