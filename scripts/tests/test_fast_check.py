#!/usr/bin/env python3
"""Fast-check plan self-tests (typed Python port of ``test-fast-check.ps1``, issue #171).

The plan drives every group the fast check can run, so each case pins one
classification against a fixture tree. Every case from the PowerShell
original is kept with the same assertions; only the driver language and the
mapped self-test subject's name (the ported ``test_fast_check.py``)
changed.
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
FAST_CHECK_PATH = REPO_ROOT / "scripts" / "fast_check.py"


def new_plan_repo() -> Path:
    """Create a committed fixture tree the plan can diff against."""
    repo = new_temp_root(prefix="fast-check-")
    _run_git(repo, ["init", "-q"])
    _run_git(repo, ["config", "user.email", "agent@example.com"])
    _run_git(repo, ["config", "user.name", "Agent"])
    write_fixture_file(repo, "README.md", "base\n")
    write_fixture_file(repo, "scripts/keep.js", "// base\n")
    _run_git(repo, ["add", "-A"])
    _run_git(repo, ["commit", "-q", "-m", "base"])
    return repo


def _run_git(repo: Path, arguments: Sequence[str]) -> None:
    completed = subprocess.run(
        ["git", "-C", str(repo), *arguments],
        capture_output=True,
        text=True,
        check=False,
    )
    assert_exit_code(0, f"git {' '.join(arguments)} failed: {completed.stderr}", completed.returncode)


def invoke_fast_check_plan(repo: Path, python: str, base: str = "") -> Dict[str, Any]:
    """Run the fast check's --plan against a fixture repo and parse the JSON."""
    arguments = [str(FAST_CHECK_PATH), "--plan", "--repo-root", str(repo)]
    if base:
        arguments.extend(["--base", base])
    completed = subprocess.run(
        [python, *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    output = (completed.stdout or "") + (completed.stderr or "")
    assert_exit_code(0, f"the plan must exit 0 (got {completed.returncode}): {output}", completed.returncode)
    return json.loads(output)


def case_plan_splits_groups_by_changed_path(python: str) -> None:
    repo = new_plan_repo()
    try:
        write_fixture_file(repo, "Editor/Foo.cs", "class Foo { }\n")
        write_fixture_file(repo, "docs/guide.md", "guide\n")
        write_fixture_file(repo, "scripts/new.js", "// new\n")
        write_fixture_file(repo, "package.json", "{}\n")
        plan = invoke_fast_check_plan(repo, python)
        assert_true(plan["schema"] == 1, f"the plan must carry schema 1, got {plan['schema']}")
        assert_true(
            "Editor/Foo.cs" in plan["csharp"],
            f"the plan must list the C# file: {','.join(plan['csharp'])}",
        )
        assert_true(
            "docs/guide.md" in plan["markdown"],
            f"the plan must list the Markdown file: {','.join(plan['markdown'])}",
        )
        assert_true(plan["harness"], "a scripts change must plan the harness group")
        assert_true(plan["packaging"], "an Editor change must plan the packaging group")
        assert_true(not plan["assembly_config"], "an unrelated tree must not plan the assembly group")
        # An unmapped scripts path and the shared manifest fail safe to the
        # full suite.
        assert_true(plan["self_tests"]["all"], "an unmapped surface change must select the full suite")
    finally:
        remove_temp_root(repo)


def case_plan_ignores_docs_only_trees(python: str) -> None:
    repo = new_plan_repo()
    try:
        write_fixture_file(repo, "docs/guide.md", "guide\n")
        write_fixture_file(repo, "README.md", "changed\n")
        plan = invoke_fast_check_plan(repo, python)
        assert_true(not plan["harness"], "a docs-only tree must skip the harness group")
        assert_true(not plan["packaging"], "a docs-only tree must skip the packaging group")
        assert_true(len(plan["csharp"]) == 0, "a docs-only tree must list no C# files")
    finally:
        remove_temp_root(repo)


def case_plan_covers_untracked_and_working_tree_files(python: str) -> None:
    repo = new_plan_repo()
    try:
        write_fixture_file(repo, "Runtime/Thing.cs", "class Thing { }\n")
        plan = invoke_fast_check_plan(repo, python)
        assert_true(
            "Runtime/Thing.cs" in plan["csharp"],
            f"an untracked C# file must be planned: {','.join(plan['csharp'])}",
        )
        assert_true(plan["packaging"], "an untracked Runtime file must plan packaging")
    finally:
        remove_temp_root(repo)


def case_plan_falls_back_to_head_when_no_base_exists(python: str) -> None:
    repo = new_plan_repo()
    try:
        write_fixture_file(repo, "CHANGELOG.md", "## [Unreleased]\n\n### Added\n\n- Something. (#1)\n")
        plan = invoke_fast_check_plan(repo, python)
        assert_true(
            "CHANGELOG.md" in plan["markdown"],
            "a fixture without origin/main must still plan the working tree",
        )
    finally:
        remove_temp_root(repo)


# Deletions and rename old-paths show up in a git diff but no longer exist on
# disk; the plan must hand formatters only files that are actually there.
def case_plan_excludes_working_tree_deletions_from_format_groups(python: str) -> None:
    repo = new_plan_repo()
    try:
        write_fixture_file(repo, "Editor/Foo.cs", "class Foo { }\n")
        write_fixture_file(repo, "docs/guide.md", "guide\n")
        _run_git(repo, ["add", "-A"])
        _run_git(repo, ["commit", "-q", "-m", "add fixtures"])
        (repo / "Editor/Foo.cs").unlink()
        (repo / "docs/guide.md").unlink()
        plan = invoke_fast_check_plan(repo, python)
        assert_true(len(plan["csharp"]) == 0, "a deleted C# file must not be a format target")
        assert_true(len(plan["markdown"]) == 0, "a deleted Markdown file must not be a format target")
        assert_true(plan["packaging"], "a deleted Editor file must still plan packaging")
    finally:
        remove_temp_root(repo)


def case_plan_excludes_committed_deletions_from_format_groups(python: str) -> None:
    repo = new_plan_repo()
    try:
        write_fixture_file(repo, "Editor/Foo.cs", "class Foo { }\n")
        write_fixture_file(repo, "docs/guide.md", "guide\n")
        _run_git(repo, ["add", "-A"])
        _run_git(repo, ["commit", "-q", "-m", "add fixtures"])
        (repo / "Editor/Foo.cs").unlink()
        (repo / "docs/guide.md").unlink()
        _run_git(repo, ["add", "-A"])
        _run_git(repo, ["commit", "-q", "-m", "delete fixtures"])
        base = _git_output(repo, ["rev-parse", "HEAD~1"])
        plan = invoke_fast_check_plan(repo, python, base)
        assert_true(len(plan["csharp"]) == 0, "a committed C# deletion must not be a format target")
        assert_true(len(plan["markdown"]) == 0, "a committed Markdown deletion must not be a format target")
        assert_true(plan["packaging"], "a committed Editor deletion must still plan packaging")
    finally:
        remove_temp_root(repo)


def case_plan_formats_only_the_existing_side_of_a_rename(python: str) -> None:
    repo = new_plan_repo()
    try:
        write_fixture_file(repo, "docs/old.md", "guide\n")
        _run_git(repo, ["add", "-A"])
        _run_git(repo, ["commit", "-q", "-m", "add doc"])
        _run_git(repo, ["mv", "docs/old.md", "docs/new.md"])
        plan = invoke_fast_check_plan(repo, python)
        assert_true(
            "docs/new.md" in plan["markdown"],
            f"the new path of a rename must be a format target: {','.join(plan['markdown'])}",
        )
        assert_true(
            "docs/old.md" not in plan["markdown"],
            "the old path of a rename must not be a format target",
        )
    finally:
        remove_temp_root(repo)


# The migrated subject map must select the fast-check test for its own driver
# and fail safe whenever a mapped name is not part of the suite.
def case_plan_selects_the_mapped_self_test_subject(python: str) -> None:
    repo = new_plan_repo()
    try:
        write_fixture_file(repo, "scripts/tests/test_fast_check.py", "# changed\n")
        write_fixture_file(repo, "scripts/fast_check.py", "# changed\n")
        plan = invoke_fast_check_plan(repo, python)
        assert_true(not plan["self_tests"]["all"], "a mapped subject must not select the full suite")
        assert_true(
            ",".join(plan["self_tests"]["names"]) == "test_fast_check.py",
            f"the fast-check subject must select its test file, got: {','.join(plan['self_tests']['names'])}",
        )
    finally:
        remove_temp_root(repo)


def case_plan_pins_the_base_sha_in_the_contract(python: str) -> None:
    repo = new_plan_repo()
    try:
        _run_git(repo, ["commit", "-q", "--allow-empty", "-m", "second"])
        base = _git_output(repo, ["rev-parse", "HEAD~1"])
        plan = invoke_fast_check_plan(repo, python, base)
        assert_true(plan["base_sha"] == base, f"the plan must carry the resolved base, got {plan['base_sha']}")
    finally:
        remove_temp_root(repo)


def _git_output(repo: Path, arguments: Sequence[str]) -> str:
    completed = subprocess.run(
        ["git", "-C", str(repo), *arguments],
        capture_output=True,
        text=True,
        check=False,
    )
    assert_exit_code(0, f"git {' '.join(arguments)} failed: {completed.stderr}", completed.returncode)
    return (completed.stdout or "").strip()


def main() -> int:
    print("== Fast-check plan self-tests ==")

    # The fast check is typed Python executed through uv locally; the
    # self-tests only need a working interpreter because --plan executes
    # nothing.
    python = find_python()

    run_case("Plan_SplitsGroupsByChangedPath", lambda: case_plan_splits_groups_by_changed_path(python))
    run_case("Plan_IgnoresDocsOnlyTrees", lambda: case_plan_ignores_docs_only_trees(python))
    run_case(
        "Plan_CoversUntrackedAndWorkingTreeFiles",
        lambda: case_plan_covers_untracked_and_working_tree_files(python),
    )
    run_case("Plan_FallsBackToHeadWhenNoBaseExists", lambda: case_plan_falls_back_to_head_when_no_base_exists(python))
    run_case(
        "Plan_ExcludesWorkingTreeDeletionsFromFormatGroups",
        lambda: case_plan_excludes_working_tree_deletions_from_format_groups(python),
    )
    run_case(
        "Plan_ExcludesCommittedDeletionsFromFormatGroups",
        lambda: case_plan_excludes_committed_deletions_from_format_groups(python),
    )
    run_case(
        "Plan_FormatsOnlyTheExistingSideOfARename",
        lambda: case_plan_formats_only_the_existing_side_of_a_rename(python),
    )
    run_case("Plan_SelectsTheMappedSelfTestSubject", lambda: case_plan_selects_the_mapped_self_test_subject(python))
    run_case("Plan_PinsTheBaseShaInTheContract", lambda: case_plan_pins_the_base_sha_in_the_contract(python))

    failures = failure_count()
    if failures > 0:
        print(f"fast-check self-tests: {failures} failed")
        return 1
    print("fast-check self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
