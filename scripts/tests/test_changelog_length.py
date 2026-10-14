#!/usr/bin/env python3
"""Changelog entry-length self-tests (typed Python port of ``test-lint-changelog-length.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions,
including the data-driven rendered-length table; only the driver language
changed.
"""

from __future__ import annotations

import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import List, Sequence, Tuple

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    failure_count,
    find_python,
    new_temp_root,
    remove_temp_root,
    run_cases,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
REPO_ROOT = SCRIPTS_DIR.parent
LINT_SCRIPT = SCRIPTS_DIR / "lint_changelog_length.py"

DEFAULT_RELEASED_ENTRY = "Released entry that stays unmeasured."


def invoke_changelog_length_lint(changelog_path: Path, lint_arguments: Sequence[str] = ()) -> Tuple[str, int]:
    """Run the lint with private child-environment overrides."""
    env = dict(os.environ)
    env["CHANGELOG_LENGTH_CHANGELOG"] = str(changelog_path)
    return run_tool_output([find_python(), str(LINT_SCRIPT), *lint_arguments], env=env)


def invoke_fixture_lint(root: Path, lint_arguments: Sequence[str] = ()) -> Tuple[str, int]:
    """Lint a fixture changelog.

    The lint resolves its override against the repository root, so fixtures
    pass absolute paths.
    """
    return invoke_changelog_length_lint(root / "CHANGELOG.md", lint_arguments)


def new_changelog_fixture(unreleased_body: str, released_entry: str = DEFAULT_RELEASED_ENTRY) -> Path:
    """Build the standard fixture: an Unreleased section over an immutable released one.

    A released entry far over the cap sits behind every case: released
    sections are immutable history and must never be measured.
    """
    root = new_temp_root(prefix="changelog-length-")
    content = "\n".join(
        [
            "# Changelog",
            "",
            "## [Unreleased]",
            unreleased_body,
            "## [0.1.0] - 2026-10-07",
            "",
            "### Added",
            "",
            f"- {released_entry}",
            "",
        ]
    )
    write_fixture_file(root, "CHANGELOG.md", content)
    return root


# The padded entries pin the cap boundary by construction: 'Add ' is four
# characters, so 296 'a' characters render at exactly 300 and 297 at 301.
AT_CAP_ENTRY = "Add " + ("a" * 296)
OVER_CAP_ENTRY = "Add " + ("a" * 297)


@dataclass(frozen=True)
class ChangelogLengthCase:
    """One changelog shape and the outcome the guard owes it.

    The verbose patterns pin the rendered lengths by hand count, so the
    exclusions are verified against known values instead of the
    implementation. ``released_entry`` and ``extra_pattern`` stay optional;
    a missing value reads as its default the way the PowerShell original
    read an absent property.
    """

    name: str
    unreleased_body: str
    expect_pass: bool
    expect_failure: str = ""
    verbose_pattern: str = ""
    extra_pattern: str = ""
    released_entry: str = DEFAULT_RELEASED_ENTRY


CASES: List[ChangelogLengthCase] = [
    ChangelogLengthCase(
        "Passes_WhenAnEntryIsExactlyAtTheCap",
        f"- {AT_CAP_ENTRY}",
        True,
        verbose_pattern="entry 1: 300",
    ),
    ChangelogLengthCase(
        "Fails_WhenAnEntryExceedsTheCapByOne",
        f"- {OVER_CAP_ENTRY}",
        False,
        expect_failure="measures 301 rendered characters, over the 300 cap",
    ),
    ChangelogLengthCase(
        "Fails_WhenAnOverCapEntryReportsItsRenderedText",
        f"- {OVER_CAP_ENTRY} (#123).",
        False,
        expect_failure="over the 300 cap",
        extra_pattern=r"Add a{297}\.",
    ),
    ChangelogLengthCase(
        # The parenthesized reference drops together with the space that
        # separates it, so 'save (#148).' renders as 'save.' and counts 40.
        "ExcludesParenthesizedIssueReferences",
        "- Fix the dirty-state drift on every save (#148).",
        True,
        verbose_pattern="entry 1: 40",
    ),
    ChangelogLengthCase(
        "ExcludesBareIssueReferences",
        "- Fix the dirty-state drift reported in #148 today.",
        True,
        verbose_pattern="entry 1: 44",
    ),
    ChangelogLengthCase(
        # The long target must not count: only the link text 'docs' renders.
        "ExcludesInlineLinkTargets",
        "- See the [docs](https://example.com/very/long/target/path/that/is/much/longer/than/the/text) page.",
        True,
        verbose_pattern="entry 1: 18",
    ),
    ChangelogLengthCase(
        # The markers delimit rendered text; their contents count, 38 here.
        "CountsEmphasisAndCodeMarkersAsRenderedText",
        "- **Add** the `theme` picker and `style` tokens.",
        True,
        verbose_pattern="entry 1: 38",
    ),
    ChangelogLengthCase(
        # The soft wrap joins into one rendered line: 45 characters, not two
        # entries or a longer count.
        "JoinsSoftWrappedContinuationLines",
        "- Add the theme picker with\n  hot reload support.",
        True,
        verbose_pattern="entry 1: 45",
    ),
    ChangelogLengthCase(
        # Every Markdown list marker starts a measured entry; a marker the
        # lint did not recognize would sit outside the cap instead of
        # failing it. Dash and star render at 19, digit at 20 ('digit' has
        # one more letter).
        "MeasuresEveryBulletMarkerShape",
        "- Add the dash entry.\n* Add the star entry.\n1. Add the digit entry.",
        True,
        verbose_pattern=r"3 entry\(ies\)",
        extra_pattern="entry 3: 20",
    ),
    ChangelogLengthCase(
        # The vacuous-pass shape: an ordered entry as the only content must
        # still be measured and fail, not be ignored.
        "Fails_WhenAnOrderedEntryIsTheOnlyContent",
        f"1. {OVER_CAP_ENTRY}",
        False,
        expect_failure="measures 301 rendered characters, over the 300 cap",
    ),
    ChangelogLengthCase(
        # Every exclusion together, the shape the shipped entries use.
        "Passes_WhenARealisticEntryUsesEveryExclusion",
        # The backticks stay literal in the fixture.
        "- **Add** the `theme` picker: an open window reloads the selected theme\n"
        "  immediately ([docs](https://example.com/guide)) (#148).",
        True,
        verbose_pattern="entry 1: 83",
    ),
    ChangelogLengthCase(
        # Only '[Unreleased]' is measured, so the released over-cap entry
        # stays invisible to the count.
        "IgnoresReleasedSections",
        f"- {AT_CAP_ENTRY}",
        True,
        verbose_pattern=r"1 entry\(ies\)",
        released_entry="Released " + ("b" * 400),
    ),
    ChangelogLengthCase(
        # The shape prepare-release.mjs leaves right after a rotation.
        "Passes_WhenUnreleasedIsEmpty",
        "",
        True,
        verbose_pattern=r"0 entry\(ies\)",
    ),
    ChangelogLengthCase(
        # The report must name the offending entry, not just the file.
        "Fails_AndNamesTheSubsectionAndEntryNumber",
        "### Added\n\n- Add the short first entry.\n\n### Changed\n\n" + f"- {OVER_CAP_ENTRY}",
        False,
        expect_failure="### Changed entry 2 measures 301 rendered characters",
    ),
]


def run_table_case(case: ChangelogLengthCase) -> None:
    root = new_changelog_fixture(case.unreleased_body, case.released_entry)
    try:
        arguments: List[str] = []
        if case.verbose_pattern != "":
            arguments.append("--verbose")
        output, exit_code = invoke_fixture_lint(root, arguments)
        if case.expect_pass:
            assert_exit_code(0, f"expected the cap to pass: {output}", exit_code)
        else:
            assert_exit_code(1, f"expected the cap to fail: {output}", exit_code)
        if case.expect_failure != "":
            assert_true(
                re.search(case.expect_failure, output, re.IGNORECASE) is not None,
                f"expected '{case.expect_failure}' in: {output}",
            )
        if case.verbose_pattern != "":
            assert_true(
                re.search(case.verbose_pattern, output, re.IGNORECASE) is not None,
                f"expected '{case.verbose_pattern}' in: {output}",
            )
        if case.extra_pattern != "":
            assert_true(
                re.search(case.extra_pattern, output, re.IGNORECASE) is not None,
                f"expected '{case.extra_pattern}' in: {output}",
            )
    finally:
        remove_temp_root(root)


def case_fails_when_unreleased_is_missing() -> None:
    root = new_temp_root(prefix="changelog-length-")
    try:
        content = "\n".join(["# Changelog", "", "## [0.1.0] - 2026-10-07", "", "- Released entry.", ""])
        write_fixture_file(root, "CHANGELOG.md", content)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, f"a missing section must fail closed: {output}", exit_code)
        assert_true(
            re.search(r"has no '## \[Unreleased\]' section", output, re.IGNORECASE) is not None,
            f"must explain the missing section: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_when_unreleased_is_duplicated() -> None:
    root = new_temp_root(prefix="changelog-length-")
    try:
        content = "\n".join(
            [
                "# Changelog",
                "",
                "## [Unreleased]",
                "",
                "- Add the first entry.",
                "",
                "## [Unreleased]",
                "",
                "- Add the second entry.",
                "",
            ]
        )
        write_fixture_file(root, "CHANGELOG.md", content)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, f"a duplicated section must fail closed: {output}", exit_code)
        assert_true(
            re.search(r"carries 2 '## \[Unreleased\]' sections", output, re.IGNORECASE) is not None,
            f"must explain the duplicated section: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_when_the_changelog_is_missing() -> None:
    root = new_temp_root(prefix="changelog-length-")
    try:
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, f"a missing file must fail closed: {output}", exit_code)
        assert_true(
            re.search("missing or unreadable", output, re.IGNORECASE) is not None,
            f"must explain the unreadable input: {output}",
        )
    finally:
        remove_temp_root(root)


def case_passes_against_the_repository_itself() -> None:
    output, exit_code = invoke_changelog_length_lint(REPO_ROOT / "CHANGELOG.md", ["--verbose"])
    assert_exit_code(0, f"the repository must satisfy its own cap: {output}", exit_code)
    assert_true(
        re.search(r"longest \d+ of 300 characters", output, re.IGNORECASE) is not None,
        f"verbose output must report the longest entry: {output}",
    )


def main() -> int:
    print("== Changelog entry length self-tests ==")

    run_cases([
        *[(case.name, lambda bound_case=case: run_table_case(bound_case)) for case in CASES],
        ("Fails_WhenUnreleasedIsMissing", case_fails_when_unreleased_is_missing),
        ("Fails_WhenUnreleasedIsDuplicated", case_fails_when_unreleased_is_duplicated),
        ("Fails_WhenTheChangelogIsMissing", case_fails_when_the_changelog_is_missing),
        ("Passes_AgainstTheRepositoryItself", case_passes_against_the_repository_itself),
    ])

    failures = failure_count()
    if failures > 0:
        print(f"changelog-length self-tests: {failures} failed")
        return 1
    print("changelog-length self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
