#!/usr/bin/env python3
"""Release credential self-tests (typed Python port of ``test-lint-release-secrets.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions;
only the driver language changed.
"""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path
from typing import Sequence, Tuple

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
REPO_ROOT = SCRIPTS_DIR.parent
LINT_SCRIPT = SCRIPTS_DIR / "lint_release_secrets.py"


def invoke_release_lint(workflow_dir: str, document: str, lint_arguments: Sequence[str] = ()) -> Tuple[str, int]:
    """Run the lint with both overrides set, then restore them."""
    had_workflow_dir = "RELEASE_SECRETS_WORKFLOW_DIR" in os.environ
    previous_workflow_dir = os.environ.get("RELEASE_SECRETS_WORKFLOW_DIR")
    had_document = "RELEASE_SECRETS_DOC" in os.environ
    previous_document = os.environ.get("RELEASE_SECRETS_DOC")
    try:
        os.environ["RELEASE_SECRETS_WORKFLOW_DIR"] = workflow_dir
        os.environ["RELEASE_SECRETS_DOC"] = document
        return run_tool_output([find_python(), str(LINT_SCRIPT), *lint_arguments])
    finally:
        if had_workflow_dir:
            os.environ["RELEASE_SECRETS_WORKFLOW_DIR"] = previous_workflow_dir or ""
        else:
            os.environ.pop("RELEASE_SECRETS_WORKFLOW_DIR", None)
        if had_document:
            os.environ["RELEASE_SECRETS_DOC"] = previous_document or ""
        else:
            os.environ.pop("RELEASE_SECRETS_DOC", None)


def write_release_workflows(root: Path, prep: str, tag: str, publish: str) -> None:
    """Write the combined release workflow fixture.

    Fixture blocks model credentials from preparation, tagging, and
    publication. The scanner reads their combined credential references from
    one release file.
    """
    write_fixture_file(root, "workflows/release.yml", prep + "\n" + tag + "\n" + publish)


def invoke_fixture_lint(root: Path, lint_arguments: Sequence[str] = ()) -> Tuple[str, int]:
    """Lint a fixture pair.

    The lint resolves its overrides against the repository root, so fixtures
    pass absolute paths.
    """
    return invoke_release_lint(
        str(root / "workflows"),
        str(root / "RELEASING.md"),
        lint_arguments,
    )


IN_SYNC_PREP = """name: Prepare release
jobs:
  prepare:
    steps:
      - uses: actions/checkout@v7
        with:
          token: ${{ secrets.RELEASE_TOKEN || github.token }}
"""

IN_SYNC_TAG = """name: Tag release
jobs:
  tag:
    steps:
      - uses: actions/checkout@v7
        with:
          token: ${{ secrets.RELEASE_TOKEN || github.token }}
"""

IN_SYNC_PUBLISH = """name: Publish to NPM
jobs:
  publish:
    steps:
      - run: gh release view "$TAG"
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
"""

IN_SYNC_DOCUMENT = """# Releasing

Prose may mention NPM_TOKEN because no token is involved.

<!-- release-secrets:begin -->

| Secret | Scope |
| --- | --- |
| `RELEASE_TOKEN` | `contents: write` |

<!-- release-secrets:end -->

More prose.
"""


def case_passes_when_workflow_secrets_are_documented() -> None:
    root = new_temp_root(prefix="release-secrets-")
    try:
        write_release_workflows(root, IN_SYNC_PREP, IN_SYNC_TAG, IN_SYNC_PUBLISH)
        write_fixture_file(root, "RELEASING.md", IN_SYNC_DOCUMENT)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(0, f"in-sync fixture should pass: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_when_workflow_secret_is_undocumented() -> None:
    root = new_temp_root(prefix="release-secrets-")
    try:
        prep = IN_SYNC_PREP + '\n      - run: echo "${{ secrets.DRY_RUN_SIGNING_KEY }}"\n'
        write_release_workflows(root, prep, IN_SYNC_TAG, IN_SYNC_PUBLISH)
        write_fixture_file(root, "RELEASING.md", IN_SYNC_DOCUMENT)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, f"undocumented secret must fail: {output}", exit_code)
        assert_true(
            re.search("DRY_RUN_SIGNING_KEY", output, re.IGNORECASE) is not None,
            f"must name the undocumented secret: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_when_documented_secret_is_read_by_no_workflow() -> None:
    root = new_temp_root(prefix="release-secrets-")
    try:
        document = IN_SYNC_DOCUMENT.replace("`RELEASE_TOKEN`", "`RELEASE_TOKEN` / `AUTO_COMMIT_APP_ID`")
        write_release_workflows(root, IN_SYNC_PREP, IN_SYNC_TAG, IN_SYNC_PUBLISH)
        write_fixture_file(root, "RELEASING.md", document)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, f"unused documented secret must fail: {output}", exit_code)
        assert_true(
            re.search("AUTO_COMMIT_APP_ID", output, re.IGNORECASE) is not None,
            f"must name the phantom credential: {output}",
        )
    finally:
        remove_temp_root(root)


def case_ignores_commented_secrets_and_built_in_tokens() -> None:
    root = new_temp_root(prefix="release-secrets-")
    try:
        tag = """name: Tag release
# A commented-out reference: ${{ secrets.REMOVED_SIGNING_KEY }}
jobs:
  tag:
    steps:
      - run: echo "# ${{ secrets.RELEASE_TOKEN }} stays data, not a comment"
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
"""
        write_release_workflows(root, IN_SYNC_PREP, tag, IN_SYNC_PUBLISH)
        write_fixture_file(root, "RELEASING.md", IN_SYNC_DOCUMENT)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(0, f"comments must be masked and built-ins excluded: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_detects_bracket_form_secret_references() -> None:
    root = new_temp_root(prefix="release-secrets-")
    try:
        # GitHub Actions accepts secrets['NAME'] and secrets["NAME"]; a guard that
        # only matched the dot form would pass a workflow holding an undocumented
        # credential, which is the exact failure this check exists to prevent.
        publish = """name: Publish to NPM
jobs:
  publish:
    steps:
      - env:
          SINGLE: ${{ secrets['SINGLE_QUOTED_SECRET'] }}
          DOUBLE: ${{ secrets["DOUBLE_QUOTED_SECRET"] }}
"""
        write_release_workflows(root, IN_SYNC_PREP, IN_SYNC_TAG, publish)
        write_fixture_file(root, "RELEASING.md", IN_SYNC_DOCUMENT)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, f"bracket-form secrets must be reported: {output}", exit_code)
        assert_true(
            re.search("SINGLE_QUOTED_SECRET", output, re.IGNORECASE) is not None,
            f"must name the single-quoted secret: {output}",
        )
        assert_true(
            re.search("DOUBLE_QUOTED_SECRET", output, re.IGNORECASE) is not None,
            f"must name the double-quoted secret: {output}",
        )
    finally:
        remove_temp_root(root)


def case_finds_secrets_in_any_release_workflow() -> None:
    root = new_temp_root(prefix="release-secrets-")
    try:
        # Guards against per-file matcher state leaking between workflows: a secret
        # reachable only from the last workflow read must still be found.
        no_secret_prep = "name: Prepare release\njobs:\n  prepare:\n    steps:\n      - run: echo ok\n"
        no_secret_tag = "name: Tag release\njobs:\n  tag:\n    steps:\n      - run: echo ok\n"
        publish = """name: Publish to NPM
jobs:
  publish:
    steps:
      - env:
          K: ${{ secrets.LAST_WORKFLOW_ONLY }}
"""
        write_release_workflows(root, no_secret_prep, no_secret_tag, publish)
        document = IN_SYNC_DOCUMENT.replace("`RELEASE_TOKEN`", "`LAST_WORKFLOW_ONLY`")
        write_fixture_file(root, "RELEASING.md", document)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(0, f"the last workflow's secrets must be read: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_when_documented_block_is_missing() -> None:
    root = new_temp_root(prefix="release-secrets-")
    try:
        write_release_workflows(root, IN_SYNC_PREP, IN_SYNC_TAG, IN_SYNC_PUBLISH)
        write_fixture_file(root, "RELEASING.md", "# Releasing\n\nNo block here.\n")
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, f"a missing block must fail closed: {output}", exit_code)
        assert_true(
            re.search("release-secrets:begin", output, re.IGNORECASE) is not None,
            f"must explain the required block: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_when_documented_block_is_empty() -> None:
    root = new_temp_root(prefix="release-secrets-")
    try:
        document = "# Releasing\n\n<!-- release-secrets:begin -->\n\n<!-- release-secrets:end -->\n"
        write_release_workflows(root, IN_SYNC_PREP, IN_SYNC_TAG, IN_SYNC_PUBLISH)
        write_fixture_file(root, "RELEASING.md", document)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, f"an empty block must fail closed: {output}", exit_code)
        assert_true(
            re.search("empty release-secrets block", output, re.IGNORECASE) is not None,
            f"must explain the empty block: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_when_workflow_is_missing() -> None:
    root = new_temp_root(prefix="release-secrets-")
    try:
        write_release_workflows(root, IN_SYNC_PREP, IN_SYNC_TAG, IN_SYNC_PUBLISH)
        (root / "workflows/release.yml").unlink()
        write_fixture_file(root, "RELEASING.md", IN_SYNC_DOCUMENT)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, f"a missing release workflow must fail closed: {output}", exit_code)
        assert_true(
            re.search(r"release\.yml", output, re.IGNORECASE) is not None,
            f"must name the missing workflow: {output}",
        )
    finally:
        remove_temp_root(root)


def case_passes_when_no_custom_secrets_are_required() -> None:
    root = new_temp_root(prefix="release-secrets-")
    try:
        workflow = "name: Built-in authentication\njobs:\n  release:\n    steps:\n      - env:\n          GH_TOKEN: ${{ github.token }}\n"
        write_release_workflows(root, workflow, workflow, IN_SYNC_PUBLISH)
        document = "<!-- release-secrets:begin -->\nNo custom secrets are required.\n<!-- release-secrets:end -->"
        write_fixture_file(root, "RELEASING.md", document)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(0, f"explicit no-secret contract should pass: {output}", exit_code)
        write_release_workflows(root, IN_SYNC_PREP, workflow, IN_SYNC_PUBLISH)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, "No-secret documentation must still reject a custom workflow secret.", exit_code)
    finally:
        remove_temp_root(root)


def case_passes_against_the_repository_itself() -> None:
    output, exit_code = invoke_release_lint(
        str(REPO_ROOT / ".github/workflows"),
        str(REPO_ROOT / ".llm/references/RELEASING.md"),
        ["--verbose"],
    )
    assert_exit_code(0, f"the repository must satisfy its own contract: {output}", exit_code)
    assert_true(
        re.search(r"Release credential contract in sync: 0 secret", output, re.IGNORECASE) is not None,
        f"verbose output must report the compared set: {output}",
    )


def main() -> int:
    print("== Release credential self-tests ==")

    run_case("Passes_WhenWorkflowSecretsAreDocumented", case_passes_when_workflow_secrets_are_documented)
    run_case("Fails_WhenWorkflowSecretIsUndocumented", case_fails_when_workflow_secret_is_undocumented)
    run_case("Fails_WhenDocumentedSecretIsReadByNoWorkflow", case_fails_when_documented_secret_is_read_by_no_workflow)
    run_case("Ignores_CommentedSecretsAndBuiltInTokens", case_ignores_commented_secrets_and_built_in_tokens)
    run_case("Detects_BracketFormSecretReferences", case_detects_bracket_form_secret_references)
    run_case("Finds_SecretsInAnyReleaseWorkflow", case_finds_secrets_in_any_release_workflow)
    run_case("Fails_WhenDocumentedBlockIsMissing", case_fails_when_documented_block_is_missing)
    run_case("Fails_WhenDocumentedBlockIsEmpty", case_fails_when_documented_block_is_empty)
    run_case("Fails_WhenWorkflowIsMissing", case_fails_when_workflow_is_missing)
    run_case("Passes_WhenNoCustomSecretsAreRequired", case_passes_when_no_custom_secrets_are_required)
    run_case("Passes_AgainstTheRepositoryItself", case_passes_against_the_repository_itself)

    failures = failure_count()
    if failures > 0:
        print(f"release credential self-tests: {failures} failed")
        return 1
    print("release credential self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
