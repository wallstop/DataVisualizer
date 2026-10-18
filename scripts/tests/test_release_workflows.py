#!/usr/bin/env python3
"""Release workflow contract self-tests (typed Python port of ``test-release-workflows.ps1``, issue #171).

Every case from the PowerShell original is kept with the same name and
assertions; only the driver language changed. The workflow text is read
once at start like the original, and the end-to-end release-chain case
drives the same release scripts over a git fixture.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import uuid
from pathlib import Path
from typing import Any, Dict, List, Tuple
from unittest.mock import patch

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
REPO_ROOT = SCRIPTS_DIR.parent
RELEASE_SCRIPTS = SCRIPTS_DIR / "release"
sys.path.insert(0, str(SCRIPTS_DIR))
from release import complete_release_pr
from release.repository_identity import CANONICAL_URL, LEGACY_URL, REPOSITORY, verify_identity

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


def python_spawn(python: str, script: Path, *arguments: str) -> int:
    """Run a release script whose output flows to the test log, like the original."""
    completed = subprocess.run([python, str(script), *arguments], check=False)
    return completed.returncode


def sha256_text(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def case_uses_one_release_workflow() -> None:
    assert_true(re.search("secrets.RELEASE_TOKEN", workflow, re.IGNORECASE) is None, "Release workflow must not need a custom token.")
    assert_true("complete_release_pr.py" in prep, "Preparation must wait for CI and merge its release PR.")
    assert_true("needs: prepare" in publish and "always() && !cancelled()" in publish, "Recovery must run even when preparation is skipped.")
    assert_true("needs.prepare.outputs.merge_sha" in publish, "Bot merges must continue publication in the same run.")
    assert_true(re.search("gh workflow run", publish, re.IGNORECASE) is None, "Tagging and publication must execute in the same job.")
    assert_true("id-token: write" in publish, "npm requires OIDC permission.")
    assert_true(
        re.search("pull_request:", workflow, re.IGNORECASE) is not None and re.search(r"types: \[closed\]", workflow, re.IGNORECASE) is not None,
        "Merged release PRs must trigger publication.",
    )
    for old in ("release-prep.yml", "release-tag.yml", "npm-publish.yml"):
        assert_true(not (WORKFLOW_PATH.parent / old).exists(), f"Obsolete {old} must be removed.")


def case_validates_all_artifacts_before_npm_publication() -> None:
    assert_true("repository_identity.py" in prep, "Provenance mismatch must fail before the release branch push.")
    assert_true(prep.find("repository_identity.py") < prep.find("git push origin"), "Check identity before remote writes.")
    assert_true('INPUT_TAG:-v$(node' in publish, "Blank recovery tags must use the current package version.")
    assert_true("github.workflow_sha" in publish and "release-tools/repository_identity.py" in publish, "Recovery must use current tooling on immutable old tags.")
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
        "extract_release_notes.py" in prep and "validate_unitypackage.py" in prep,
        "Preparation must validate the future release notes and Unity archive.",
    )
    assert_true("later release-automation slices" not in prep, "Release PR must describe the implemented chain.")
    assert_true("--body-file" in prep, "Release PR body must use a file.")


def case_prepares_tags_and_builds_a_complete_release(python: str) -> None:
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
        exit_code = python_spawn(python, RELEASE_SCRIPTS / "prepare_release.py", "--root", str(root), "--bump", "patch")
        assert_exit_code(0, "prepare candidate", exit_code)
        git_spawn(root, "add", "-A")
        exit_code = git_spawn(root, "commit", "--quiet", "-m", "Prepare release")
        assert_exit_code(0, "candidate commit", exit_code)
        exit_code = python_spawn(python, RELEASE_SCRIPTS / "tag_release.py", "--root", str(root), "--branch", "release/v1.2.4")
        assert_exit_code(0, "create annotated release tag", exit_code)
        kind, _ = git_capture(root, "cat-file", "-t", "refs/tags/v1.2.4")
        assert_true(kind == "tag", "Release tag must be annotated.")
        exit_code = python_spawn(python, RELEASE_SCRIPTS / "verify_release.py", "--root", str(root), "--tag", "v1.2.4")
        assert_exit_code(0, "pack and validate npm payload", exit_code)
        dist = root / "dist"
        exit_code = python_spawn(python, RELEASE_SCRIPTS / "build_unitypackage.py", "--root", str(root), "--output", str(dist))
        assert_exit_code(0, "build Unity archive", exit_code)
        archive = dist / "com.test.fixture-1.2.4.unitypackage"
        exit_code = python_spawn(python, RELEASE_SCRIPTS / "validate_unitypackage.py", "--root", str(root), "--archive", str(archive))
        assert_exit_code(0, "validate Unity archive against tracked tree", exit_code)
        first_hash = sha256_text(archive)
        exit_code = python_spawn(python, RELEASE_SCRIPTS / "build_unitypackage.py", "--root", str(root), "--output", str(dist))
        assert_exit_code(0, "repeat archive build", exit_code)
        assert_true(sha256_text(archive).lower() == first_hash.lower(), "Rebuilding the same release must reproduce its Unity archive.")
        notes = dist / "release-notes.md"
        exit_code = python_spawn(python, RELEASE_SCRIPTS / "extract_release_notes.py", "--root", str(root), "--version", "1.2.4", "--output", str(notes))
        assert_exit_code(0, "extract release notes", exit_code)
        assert_true("Fix asset selection" in notes.read_text(encoding="utf-8"), "GitHub notes must contain the rotated changelog entry.")
        assert_true((root / "com.test.fixture-1.2.4.tgz").exists(), "npm archive must be ready for publication.")
        assert_true(Path(f"{archive}.sha256").exists(), "Unity archive must have its checksum companion.")
    finally:
        remove_temp_root(root)




def case_resolves_publish_and_automatic_merge_sources() -> None:
    bash = shutil.which("bash")
    if bash is None:
        print("  SKIP: release source shell probe requires bash; Linux CI runs it.")
        return
    marker = publish.index("      - name: Resolve the release source")
    start = publish.index("        run: |\n", marker) + len("        run: |\n")
    end = publish.index("\n      - name:", start)
    script = "\n".join(line[10:] for line in publish[start:end].splitlines())
    root = new_temp_root(prefix="release-source-")
    try:
        version = json.loads((REPO_ROOT / "package.json").read_text())["version"]
        for event, input_tag, prepared_sha, expected_tag, expected_ref in [
            ("workflow_dispatch", "", "", f"v{version}", f"refs/tags/v{version}"),
            ("workflow_dispatch", "v0.1.0", "", "v0.1.0", "refs/tags/v0.1.0"),
            ("workflow_dispatch", "", "validated-merge", "v0.1.2", "validated-merge"),
            ("push", "", "", "v0.1.0", "refs/tags/v0.1.0"),
        ]:
            output = root / "outputs"
            output.write_text("")
            env = {**os.environ, "EVENT": event, "INPUT_TAG": input_tag, "PREPARED_SHA": prepared_sha,
                   "PREPARED_BRANCH": "release/v0.1.2", "HEAD_REF": "", "MERGE_SHA": "",
                   "REF": "refs/tags/v0.1.0" if event == "push" else "refs/heads/main", "GITHUB_OUTPUT": str(output)}
            result = subprocess.run([bash, "-c", script], cwd=REPO_ROOT, env=env, capture_output=True, text=True, check=False)
            assert_exit_code(0, f"resolve {event} {input_tag or 'current version'}", result.returncode)
            values = dict(line.split("=", 1) for line in output.read_text().splitlines())
            assert_true(values["tag"] == expected_tag and values["checkout_ref"] == expected_ref, "Resolve the exact tag or validated merge.")
            assert_true(values["create_tag"] == ("true" if prepared_sha else "false"), "Only prepared merges create tags.")
    finally:
        remove_temp_root(root)

def case_provenance_identity_and_narrow_recovery() -> None:
    root = new_temp_root(prefix="release-identity-")
    try:
        original: Dict[str, Any] = {"name": "com.wallstop-studios.data-visualizer", "version": "0.1.1",
                                    "repository": {"type": "git", "url": LEGACY_URL}, "files": ["Runtime"], "description": "Keep this."}
        path = root / "package.json"
        path.write_text(json.dumps(original), encoding="utf-8")
        try:
            verify_identity(root, REPOSITORY)
            raise AssertionError("Preparation accepted the exact provenance mismatch that broke 0.1.1.")
        except ValueError as error:
            assert_true("provenance requires" in str(error), "Explain the metadata mismatch.")
        assert_true(json.loads(path.read_text()) == original, "Validation must not mutate metadata.")
        assert_true(verify_identity(root, REPOSITORY, True), "Recover the observed legacy release.")
        repaired = json.loads(path.read_text())
        repaired["repository"]["url"] = LEGACY_URL
        assert_true(repaired == original, "Recovery may change only repository.url.")
        assert_true(not verify_identity(root, REPOSITORY, True), "Recovery must be idempotent.")
        for version, repository, url in [("0.1.2", REPOSITORY, LEGACY_URL), ("0.1.1", "other/repo", LEGACY_URL),
                                         ("0.1.1", REPOSITORY, "git+https://github.com/unknown/repo.git")]:
            original["version"] = version
            original["repository"]["url"] = url
            path.write_text(json.dumps(original), encoding="utf-8")
            try:
                verify_identity(root, repository, True)
                raise AssertionError("Recovery accepted an unrelated identity or version.")
            except ValueError:
                pass
        package = json.loads((REPO_ROOT / "package.json").read_text())
        assert_true(package["repository"]["url"] == CANONICAL_URL, "The committed manifest must match provenance.")
    finally:
        remove_temp_root(root)


def case_automation_waits_for_exact_ci_and_merges_exact_head() -> None:
    pr = {"number": 42, "headRefOid": "candidate", "baseRefName": "main", "isCrossRepository": False, "state": "OPEN"}
    unrelated = {"display_title": "another-run", "head_sha": "candidate", "status": "completed", "conclusion": "success"}
    success = {"display_title": "request", "head_sha": "candidate", "status": "completed", "conclusion": "success"}
    with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPOSITORY}), patch.object(complete_release_pr.time, "sleep"):
        with patch.object(complete_release_pr, "gh", side_effect=[pr, None, {"workflow_runs": [unrelated]},
                                                                 {"workflow_runs": [success]}, {"merged": True, "sha": "merged"}]) as mock:
            assert_true(complete_release_pr.complete("release/v0.1.2", "request", "candidate") == "merged", "Report the actual merge commit.")
            arguments = mock.call_args_list[-1].args[0]
            assert_true("sha=candidate" in arguments, "Merge must compare the validated head atomically.")
        for runs, merge_result in [([{**success, "conclusion": "failure", "html_url": "https://ci.invalid"}], None),
                                    ([success], {"merged": False, "message": "Policy denied"})]:
            responses: List[Any] = [pr, None, {"workflow_runs": runs}]
            if merge_result is not None:
                responses.append(merge_result)
            with patch.object(complete_release_pr, "gh", side_effect=responses) as mock:
                try:
                    complete_release_pr.complete("release/v0.1.2", "request", "candidate")
                    raise AssertionError("Failed CI or merge policy must stop publication.")
                except ValueError:
                    pass
                if merge_result is None:
                    assert_true(mock.call_count == 3, "Failed CI must not attempt a merge.")
        with patch.object(complete_release_pr, "gh", return_value=pr) as mock:
            try:
                complete_release_pr.complete("release/v0.1.2", "request", "changed")
                raise AssertionError("Changed candidates must fail before dispatch.")
            except ValueError:
                pass
            assert_true(mock.call_count == 1, "Changed candidates must not dispatch or merge.")
        with patch.object(complete_release_pr, "gh", side_effect=[pr, None]) as mock:
            try:
                complete_release_pr.complete("release/v0.1.2", "request", "candidate", timeout=0)
                raise AssertionError("Missing CI must time out without merge.")
            except ValueError:
                pass
            assert_true(mock.call_count == 2, "Timeout must not merge.")


def main() -> int:
    python = find_python()

    run_case("Resolves_Publish_And_Automatic_Merge_Sources", case_resolves_publish_and_automatic_merge_sources)
    run_case("Provenance_Identity_And_Narrow_Recovery", case_provenance_identity_and_narrow_recovery)
    run_case("Automation_Waits_For_Exact_CI_And_Merges_Exact_Head", case_automation_waits_for_exact_ci_and_merges_exact_head)
    run_case("Uses_OneReleaseWorkflow_WithBuiltInAuthentication", case_uses_one_release_workflow)
    run_case("Validates_AllArtifacts_BeforeNpmPublication", case_validates_all_artifacts_before_npm_publication)
    run_case("Rehearses_WithoutPublishing", case_rehearses_without_publishing)
    run_case("Validates_PreparedTree_EvenDuringDryRun", case_validates_prepared_tree_even_during_dry_run)
    run_case("Prepares_Tags_AndBuilds_ACompleteRelease", lambda: case_prepares_tags_and_builds_a_complete_release(python))

    if failure_count() > 0:
        return 1
    print("Release workflow contracts: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
