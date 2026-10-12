#!/usr/bin/env python3
"""Harness scope self-tests (typed Python port of ``test-harness-scope.ps1``, issue #171).

The scope module and the verdict wrapper are typed Python, and the tests
only need a working interpreter because the probes execute nothing. Every
case from the PowerShell original is kept with the same assertions; the
mapped subject names now point at the ported ``test_*.py`` files.
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, Sequence

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
REPO_ROOT = TESTS_DIR.parent.parent
SCOPE_PATH = REPO_ROOT / "scripts" / "harness_scope.py"
WRAPPER_PATH = REPO_ROOT / "scripts" / "should_run_self_tests.py"


def new_scope_repo() -> Path:
    """Create a committed fixture tree the wrapper can diff against."""
    repo = new_temp_root(prefix="harness-scope-")
    _git_output(repo, ["init", "-q"])
    _git_output(repo, ["config", "user.email", "agent@example.com"])
    _git_output(repo, ["config", "user.name", "Agent"])
    write_fixture_file(repo, "README.md", "base\n")
    write_fixture_file(repo, "scripts/keep.js", "// base\n")
    _git_output(repo, ["add", "-A"])
    _git_output(repo, ["commit", "-q", "-m", "base"])
    return repo


def _git_output(repo: Path, arguments: Sequence[str]) -> str:
    """Run a git query in a fixture repo and return its stripped stdout."""
    completed = subprocess.run(
        ["git", "-C", str(repo), *arguments],
        capture_output=True,
        text=True,
        check=False,
    )
    assert_exit_code(0, f"git {' '.join(arguments)} failed: {completed.stderr}", completed.returncode)
    return (completed.stdout or "").strip()


def scope_commit(repo: Path, files: Dict[str, str]) -> None:
    """Write fixture files and commit them, mirroring one change."""
    for relative_path, content in files.items():
        write_fixture_file(repo, relative_path, content)
    _git_output(repo, ["add", "-A"])
    _git_output(repo, ["commit", "-q", "-m", "change"])


def invoke_should_run(repo: Path, python: str, base_sha: str, selection: bool = False) -> str:
    """Run the verdict wrapper and return its trimmed output."""
    arguments = [str(WRAPPER_PATH), "--repo-root", str(repo), "--base-sha", base_sha]
    if selection:
        arguments.append("--selection")
    completed = subprocess.run(
        [python, *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    output = ((completed.stdout or "") + (completed.stderr or "")).strip()
    assert_exit_code(0, f"the wrapper must exit 0 (got {completed.returncode}): {output}", completed.returncode)
    return output


def invoke_scope_probe(python: str, paths: str, available: str = "") -> Dict[str, Any]:
    """Run the scope module's probe subcommand and parse its JSON."""
    arguments = [str(SCOPE_PATH), "probe", "--paths", paths]
    if available:
        arguments.extend(["--available", available])
    completed = subprocess.run(
        [python, *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    output = (completed.stdout or "") + (completed.stderr or "")
    assert_exit_code(0, f"the probe must exit 0 (got {completed.returncode}): {output}", completed.returncode)
    return json.loads(output)


def case_skips_a_docs_only_commit(python: str) -> None:
    repo = new_scope_repo()
    try:
        base = _git_output(repo, ["rev-parse", "HEAD"])
        scope_commit(repo, {"README.md": "changed\n"})
        assert_true(
            invoke_should_run(repo, python, base) == "skip",
            "a docs-only commit must skip the suite",
        )
    finally:
        remove_temp_root(repo)


def case_skips_a_csharp_only_commit(python: str) -> None:
    repo = new_scope_repo()
    try:
        base = _git_output(repo, ["rev-parse", "HEAD"])
        scope_commit(repo, {"Editor/Foo.cs": "class Foo { }\n"})
        assert_true(
            invoke_should_run(repo, python, base) == "skip",
            "a C#-only commit must skip the suite",
        )
    finally:
        remove_temp_root(repo)


def case_runs_for_a_scripts_commit(python: str) -> None:
    repo = new_scope_repo()
    try:
        base = _git_output(repo, ["rev-parse", "HEAD"])
        scope_commit(repo, {"scripts/keep.js": "// changed\n"})
        assert_true(
            invoke_should_run(repo, python, base) == "run",
            "a scripts commit must run the suite",
        )
    finally:
        remove_temp_root(repo)


def case_runs_for_harness_directory_commits(python: str) -> None:
    repo = new_scope_repo()
    try:
        base = _git_output(repo, ["rev-parse", "HEAD"])
        for path in (".llm/context.md", ".github/workflows/pipeline.yml", ".devcontainer/install-npm-tools.sh", ".config/dotnet-tools.json"):
            write_fixture_file(repo, path, "changed\n")
            _git_output(repo, ["add", "-A"])
            _git_output(repo, ["commit", "-q", "-m", "surface change"])
            assert_true(
                invoke_should_run(repo, python, base) == "run",
                f"a change under {path} must run the suite",
            )
    finally:
        remove_temp_root(repo)


def case_runs_for_a_root_manifest_commit(python: str) -> None:
    repo = new_scope_repo()
    try:
        base = _git_output(repo, ["rev-parse", "HEAD"])
        scope_commit(repo, {"package.json": "{}\n"})
        assert_true(
            invoke_should_run(repo, python, base) == "run",
            "a package.json commit must run the suite",
        )
    finally:
        remove_temp_root(repo)


def case_runs_for_an_untracked_surface_file(python: str) -> None:
    repo = new_scope_repo()
    try:
        base = _git_output(repo, ["rev-parse", "HEAD"])
        scope_commit(repo, {"README.md": "changed\n"})
        write_fixture_file(repo, "scripts/new.js", "// new\n")
        assert_true(
            invoke_should_run(repo, python, base) == "run",
            "an untracked scripts file must run the suite",
        )
    finally:
        remove_temp_root(repo)


def case_runs_for_a_working_tree_surface_edit(python: str) -> None:
    repo = new_scope_repo()
    try:
        base = _git_output(repo, ["rev-parse", "HEAD"])
        scope_commit(repo, {"README.md": "changed\n"})
        write_fixture_file(repo, "scripts/keep.js", "// dirty\n")
        assert_true(
            invoke_should_run(repo, python, base) == "run",
            "an uncommitted scripts edit must run the suite",
        )
    finally:
        remove_temp_root(repo)


def case_runs_when_the_base_cannot_be_resolved(python: str) -> None:
    repo = new_scope_repo()
    try:
        for base in ("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", "0000000000000000000000000000000000000000", ""):
            assert_true(
                invoke_should_run(repo, python, base) == "run",
                f"an unusable base '{base}' must run the suite",
            )
    finally:
        remove_temp_root(repo)


# The surface match list pins the prefixes and the root-level manifest files;
# everything else, including nested manifests, stays outside the surface.
def case_surface_path_matches_the_declared_surface_only(python: str) -> None:
    surface_paths = (
        "scripts/lint.js",
        "scripts\\nested\\lint.js",
        "./.llm/context.md",
        ".github/workflows/ci.yml",
        "package.json",
        "package-lock.json",
        "opencode.json",
        ".pre-commit-config.yaml",
        ".mcp.json",
        ".env.local.example",
    )
    probe = invoke_scope_probe(python, ",".join(surface_paths))
    for path in surface_paths:
        assert_true(probe["surface"].get(path, False), f"{path} must match the surface")
    outside_paths = (
        "README.md",
        "docs/index.md",
        "Editor/Foo.cs",
        "Tests/Editor/Foo.cs",
        "sub/package.json",
        "docs/images/logo.png",
        "",
    )
    probe = invoke_scope_probe(python, ",".join(outside_paths))
    # The probe's path splitter drops empty entries, so a missing key reads
    # as "not on the surface" the same way the PowerShell original read $null.
    for path in outside_paths:
        assert_true(not probe["surface"].get(path, False), f"'{path}' must not match the surface")


# The selection maps a changed surface path onto the test files that verify
# it. Unmapped, shared, or stale names fail safe to the full suite.
def case_selection_selects_the_mapped_subject_tests(python: str) -> None:
    probe = invoke_scope_probe(python, "scripts/fast_check.py")
    assert_true(not probe["selection"]["all"], "a mapped subject must not select the full suite")
    assert_true(
        ",".join(probe["selection"]["names"]) == "test_fast_check.py",
        f"the fast-check subject must select its test file, got: {','.join(probe['selection']['names'])}",
    )


def case_selection_unions_subjects_across_paths(python: str) -> None:
    probe = invoke_scope_probe(python, "scripts/release/verify-release.mjs,scripts/release/validate-unitypackage.mjs")
    assert_true(not probe["selection"]["all"], "mapped subjects must not select the full suite")
    assert_true(
        ",".join(probe["selection"]["names"]) == "test_validate_unitypackage.py,test_verify_release.py",
        f"the release subjects must union their test files sorted, got: {','.join(probe['selection']['names'])}",
    )


def case_selection_prefers_the_specific_mcp_subject_over_the_coarse_tree(python: str) -> None:
    probe = invoke_scope_probe(python, ".llm/mcp/configure.mjs")
    assert_true(not probe["selection"]["all"], "the mcp subject must not select the full suite")
    assert_true(
        ",".join(probe["selection"]["names"]) == "test_mcp_credentials.py,test_mcp_sync.py",
        f"the mcp subject must select the mcp test files, got: {','.join(probe['selection']['names'])}",
    )


def case_selection_maps_a_test_file_to_itself(python: str) -> None:
    probe = invoke_scope_probe(python, "scripts/tests/test_harness_scope.py")
    assert_true(not probe["selection"]["all"], "a test file edit must not select the full suite")
    assert_true(
        ",".join(probe["selection"]["names"]) == "test_harness_scope.py",
        "a test file edit must select itself for immediate feedback",
    )


def case_selection_maps_the_gate_surface(python: str) -> None:
    probe = invoke_scope_probe(python, "scripts/gate.py,pyproject.toml,uv.lock")
    assert_true(not probe["selection"]["all"], "the gate surface must not select the full suite")
    assert_true(
        ",".join(probe["selection"]["names"]) == "test_gate.py",
        f"the gate surface must select its test file, got: {','.join(probe['selection']['names'])}",
    )


def case_selection_maps_the_migrated_trio_surface(python: str) -> None:
    probe = invoke_scope_probe(python, "scripts/harness_scope.py")
    assert_true(not probe["selection"]["all"], "the scope module must not select the full suite")
    assert_true(
        ",".join(probe["selection"]["names"]) == "test_fast_check.py,test_harness_scope.py",
        f"the scope module must select both trio test files, got: {','.join(probe['selection']['names'])}",
    )
    probe = invoke_scope_probe(python, "scripts/should_run_self_tests.py")
    assert_true(
        ",".join(probe["selection"]["names"]) == "test_harness_scope.py",
        f"the verdict wrapper must select its test file, got: {','.join(probe['selection']['names'])}",
    )


def case_selection_falls_back_to_all_for_shared_suite_paths(python: str) -> None:
    for path in ("scripts/tests/_test_helpers.py", "scripts/tests/run_all.py", "package.json", ".config/dotnet-tools.json"):
        probe = invoke_scope_probe(python, path)
        assert_true(probe["selection"]["all"], f"the shared path {path} must select the full suite")


def case_selection_falls_back_to_all_for_unmapped_subjects(python: str) -> None:
    probe = invoke_scope_probe(python, "scripts/build_docs.py")
    assert_true(probe["selection"]["all"], "an unmapped surface path must select the full suite")


def case_selection_ignores_paths_outside_the_surface(python: str) -> None:
    probe = invoke_scope_probe(python, "scripts/fast_check.py,docs/index.md,node_modules.meta")
    assert_true(not probe["selection"]["all"], "non-surface paths must not broaden the selection")
    assert_true(
        ",".join(probe["selection"]["names"]) == "test_fast_check.py",
        f"the selection must keep only the subject test, got: {','.join(probe['selection']['names'])}",
    )


def case_selection_fails_safe_when_a_mapped_name_is_unavailable(python: str) -> None:
    probe = invoke_scope_probe(python, "scripts/fast_check.py", "test-other.ps1")
    assert_true(probe["selection"]["all"], "a mapped name missing from the suite must select the full suite")


def case_selection_accepts_the_available_names(python: str) -> None:
    probe = invoke_scope_probe(python, "scripts/fast_check.py", "test_fast_check.py,test-zulu.ps1")
    assert_true(not probe["selection"]["all"], "a mapped name present in the suite must not select the full suite")
    assert_true(
        ",".join(probe["selection"]["names"]) == "test_fast_check.py",
        "the selection must keep the mapped name only",
    )


# The --selection output feeds CI's scoped self-test run: a mapped subject
# prints its test file names, everything else prints 'all'.
def case_selection_output_names_the_subject_for_a_mapped_commit(python: str) -> None:
    repo = new_scope_repo()
    try:
        base = _git_output(repo, ["rev-parse", "HEAD"])
        scope_commit(repo, {"scripts/fast_check.py": "# changed\n"})
        output = invoke_should_run(repo, python, base, selection=True)
        assert_true(output == "test_fast_check.py", f"the selection must name the subject test, got: {output}")
    finally:
        remove_temp_root(repo)


def case_selection_output_falls_back_to_all_for_an_unmapped_commit(python: str) -> None:
    repo = new_scope_repo()
    try:
        base = _git_output(repo, ["rev-parse", "HEAD"])
        scope_commit(repo, {"scripts/build_docs.py": "# changed\n"})
        output = invoke_should_run(repo, python, base, selection=True)
        assert_true(output == "all", f"an unmapped subject must select the full suite, got: {output}")
    finally:
        remove_temp_root(repo)


def main() -> int:
    print("== Harness scope self-tests ==")

    # The scope module and the verdict wrapper are typed Python; the
    # self-tests only need a working interpreter because the probes execute
    # nothing.
    python = find_python()

    run_case("Skips_ADocsOnlyCommit", lambda: case_skips_a_docs_only_commit(python))
    run_case("Skips_ACSharpOnlyCommit", lambda: case_skips_a_csharp_only_commit(python))
    run_case("Runs_ForAScriptsCommit", lambda: case_runs_for_a_scripts_commit(python))
    run_case("Runs_ForHarnessDirectoryCommits", lambda: case_runs_for_harness_directory_commits(python))
    run_case("Runs_ForARootManifestCommit", lambda: case_runs_for_a_root_manifest_commit(python))
    run_case("Runs_ForAnUntrackedSurfaceFile", lambda: case_runs_for_an_untracked_surface_file(python))
    run_case("Runs_ForAWorkingTreeSurfaceEdit", lambda: case_runs_for_a_working_tree_surface_edit(python))
    run_case("Runs_WhenTheBaseCannotBeResolved", lambda: case_runs_when_the_base_cannot_be_resolved(python))
    run_case(
        "SurfacePath_MatchesTheDeclaredSurfaceOnly",
        lambda: case_surface_path_matches_the_declared_surface_only(python),
    )
    run_case("Selection_SelectsTheMappedSubjectTests", lambda: case_selection_selects_the_mapped_subject_tests(python))
    run_case("Selection_UnionsSubjectsAcrossPaths", lambda: case_selection_unions_subjects_across_paths(python))
    run_case(
        "Selection_PrefersTheSpecificMcpSubjectOverTheCoarseTree",
        lambda: case_selection_prefers_the_specific_mcp_subject_over_the_coarse_tree(python),
    )
    run_case("Selection_MapsATestFileToItself", lambda: case_selection_maps_a_test_file_to_itself(python))
    run_case("Selection_MapsTheGateSurface", lambda: case_selection_maps_the_gate_surface(python))
    run_case("Selection_MapsTheMigratedTrioSurface", lambda: case_selection_maps_the_migrated_trio_surface(python))
    run_case(
        "Selection_FallsBackToAllForSharedSuitePaths",
        lambda: case_selection_falls_back_to_all_for_shared_suite_paths(python),
    )
    run_case(
        "Selection_FallsBackToAllForUnmappedSubjects",
        lambda: case_selection_falls_back_to_all_for_unmapped_subjects(python),
    )
    run_case("Selection_IgnoresPathsOutsideTheSurface", lambda: case_selection_ignores_paths_outside_the_surface(python))
    run_case(
        "Selection_FailsSafeWhenAMappedNameIsUnavailable",
        lambda: case_selection_fails_safe_when_a_mapped_name_is_unavailable(python),
    )
    run_case("Selection_AcceptsTheAvailableNames", lambda: case_selection_accepts_the_available_names(python))
    run_case(
        "SelectionOutput_NamesTheSubjectForAMappedCommit",
        lambda: case_selection_output_names_the_subject_for_a_mapped_commit(python),
    )
    run_case(
        "SelectionOutput_FallsBackToAllForAnUnmappedCommit",
        lambda: case_selection_output_falls_back_to_all_for_an_unmapped_commit(python),
    )

    failures = failure_count()
    if failures > 0:
        print(f"harness-scope self-tests: {failures} failed")
        return 1
    print("harness-scope self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
