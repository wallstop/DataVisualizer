#!/usr/bin/env python3
"""Line-ending contract self-tests (typed Python port of ``test-lint-line-endings.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions,
including the data-driven configuration table; only the driver language
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
    find_node,
    new_temp_root,
    remove_temp_root,
    run_case,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
REPO_ROOT = SCRIPTS_DIR.parent
LINT_SCRIPT = SCRIPTS_DIR / "lint-line-endings.js"


def invoke_line_ending_lint(
    editor_config: str,
    git_attributes: str,
    lint_arguments: Sequence[str] = (),
) -> Tuple[str, int]:
    """Run the lint with both overrides set, then restore them."""
    had_editor_config = "LINE_ENDINGS_EDITORCONFIG" in os.environ
    previous_editor_config = os.environ.get("LINE_ENDINGS_EDITORCONFIG")
    had_git_attributes = "LINE_ENDINGS_GITATTRIBUTES" in os.environ
    previous_git_attributes = os.environ.get("LINE_ENDINGS_GITATTRIBUTES")
    try:
        os.environ["LINE_ENDINGS_EDITORCONFIG"] = editor_config
        os.environ["LINE_ENDINGS_GITATTRIBUTES"] = git_attributes
        return run_tool_output([find_node(), str(LINT_SCRIPT), *lint_arguments])
    finally:
        if had_editor_config:
            os.environ["LINE_ENDINGS_EDITORCONFIG"] = previous_editor_config or ""
        else:
            os.environ.pop("LINE_ENDINGS_EDITORCONFIG", None)
        if had_git_attributes:
            os.environ["LINE_ENDINGS_GITATTRIBUTES"] = previous_git_attributes or ""
        else:
            os.environ.pop("LINE_ENDINGS_GITATTRIBUTES", None)


def invoke_fixture_lint(root: Path, lint_arguments: Sequence[str] = ()) -> Tuple[str, int]:
    """Lint a fixture pair.

    The lint resolves its overrides against the repository root, so fixtures
    pass absolute paths.
    """
    return invoke_line_ending_lint(
        str(root / ".editorconfig"),
        str(root / ".gitattributes"),
        lint_arguments,
    )


EDITOR_CONFIG_LF = "root = true\n\n[*]\nend_of_line = lf"

# The configuration that shipped the failure: the editor asks for CRLF for every
# file and git checks out the platform default.
EDITOR_CONFIG_CRLF = "root = true\n\n[*]\nend_of_line = crlf"

GIT_ATTRIBUTES_PLATFORM_DEFAULT = "* text=auto"

GIT_ATTRIBUTES_LF = "* text=auto eol=lf"

GIT_ATTRIBUTES_CRLF = "* text=auto eol=crlf"


@dataclass(frozen=True)
class LineEndingsCase:
    """One configuration pair and the outcome the guard owes it.

    The drifted rows reproduce the shapes this repository has shipped or
    could ship.
    """

    name: str
    editor_config: str
    git_attributes: str
    expect_pass: bool
    expect: str = ""


CASES: List[LineEndingsCase] = [
    LineEndingsCase("Passes_WhenBothConfigurationsUseLf", EDITOR_CONFIG_LF, GIT_ATTRIBUTES_LF, True),
    LineEndingsCase("Passes_WhenBothConfigurationsUseCrlf", EDITOR_CONFIG_CRLF, GIT_ATTRIBUTES_CRLF, True),
    LineEndingsCase(
        "Fails_WhenTheEditorAsksForCrlfAndGitChecksOutLf",
        EDITOR_CONFIG_CRLF,
        GIT_ATTRIBUTES_LF,
        False,
        r"git checks out lf.+requires crlf",
    ),
    LineEndingsCase(
        "Fails_WhenGitAttributesLeavesTheEndingToThePlatform",
        EDITOR_CONFIG_LF,
        GIT_ATTRIBUTES_PLATFORM_DEFAULT,
        False,
        r"git leaves the checkout ending to the platform.+requires lf",
    ),
    LineEndingsCase(
        # The same defect narrowed to one extension: a guard that only compared
        # each rule with the `[*]` section would read this as in sync, because the
        # broad rule's sample path carries no extension.
        "Fails_WhenOnlyOneExtensionAsksForCrlf",
        EDITOR_CONFIG_LF + "\n\n[*.cs]\nend_of_line = crlf\n",
        GIT_ATTRIBUTES_LF,
        False,
        r"'sample\.cs': git checks out lf.+requires crlf",
    ),
    LineEndingsCase(
        "Fails_WhenTheDefaultRuleIsMissing",
        EDITOR_CONFIG_LF,
        "*.cs text eol=lf\n",
        False,
        r"has no '\*' rule",
    ),
    LineEndingsCase(
        "Fails_WhenTheDefaultRuleMarksEveryPathBinary",
        EDITOR_CONFIG_LF,
        "* -text\n",
        False,
        "marks every path binary",
    ),
    LineEndingsCase(
        "Fails_WhenASpecificRuleDisagreesWithTheEditor",
        EDITOR_CONFIG_LF,
        "* text=auto eol=lf\n*.ps1 text eol=crlf\n",
        False,
        r"'sample\.ps1': git checks out crlf.+requires lf",
    ),
    LineEndingsCase(
        "Fails_WhenAGitAttributesEndingHasNoTextAttribute",
        EDITOR_CONFIG_LF,
        "* text=auto eol=lf\n*.unitypackage eol=lf\n",
        False,
        r"sets an ending for '\*\.unitypackage' without 'text'",
    ),
    LineEndingsCase(
        "Fails_WhenABinaryRuleAlsoPinsAnEnding",
        EDITOR_CONFIG_LF,
        "* text=auto eol=lf\n*.unitypackage -text eol=lf\n",
        False,
        r"marks '\*\.unitypackage' binary and also sets an ending",
    ),
    LineEndingsCase(
        "Fails_WhenTheEndingNameIsUnknown",
        EDITOR_CONFIG_LF,
        "* text=auto eol=cr\n",
        False,
        "unknown line ending 'cr'",
    ),
    LineEndingsCase(
        "Fails_WhenNoEditorConfigSectionDeclaresTheDefault",
        "root = true\n\n[*.cs]\nindent_size = 4\n",
        GIT_ATTRIBUTES_LF,
        False,
        r"must carry a '\[\*\]' section",
    ),
    LineEndingsCase(
        "Fails_WhenTheDefaultSectionOmitsEndOfLine",
        "root = true\n\n[*]\nindent_size = 4\n",
        GIT_ATTRIBUTES_LF,
        False,
        r"sets no 'end_of_line' for '\[\*\]'",
    ),
    LineEndingsCase(
        "Fails_WhenAnEditorConfigLineIsNotAProperty",
        "root = true\n\n[*]\nend_of_line\n",
        GIT_ATTRIBUTES_LF,
        False,
        "is not a 'key = value' line",
    ),
    LineEndingsCase(
        "IgnoresBinaryRulesThatPinNoEnding",
        EDITOR_CONFIG_LF,
        "* text=auto eol=lf\n*.png binary\n*.unitypackage -text\n",
        True,
    ),
    LineEndingsCase(
        # A later section wins, so a path-specific ending must be honoured on both
        # sides instead of compared against the default alone.
        "Passes_WhenALaterEditorConfigSectionNarrowsTheEnding",
        EDITOR_CONFIG_LF + "\n\n[*.ps1]\nend_of_line = crlf\n",
        "* text=auto eol=lf\n*.ps1 text eol=crlf\n",
        True,
    ),
    LineEndingsCase(
        # The repository groups extensions in one brace list, so every alternative
        # has to be compared on both sides: a matcher that ignored the braces would
        # resolve those sections as unmatched and report a matching ending as a
        # mismatch, and one that compared only the first would leave `*.ps1` out.
        "Passes_WhenAnEditorConfigSectionGroupsExtensionsInBraces",
        EDITOR_CONFIG_LF + "\n\n[{*.cs,*.ps1}]\nindent_size = 4\nend_of_line = crlf\n",
        "* text=auto eol=lf\n*.cs text eol=crlf\n*.ps1 text eol=crlf\n",
        True,
    ),
    LineEndingsCase(
        # Git takes each attribute from the last matching rule, so a binary rule
        # after the text rule must win even though the text rule pins an ending.
        "Passes_WhenALaterBinaryRuleOverridesTheTextRule",
        EDITOR_CONFIG_LF,
        "* text=auto eol=lf\ndocs/images/*.png -text\n",
        True,
    ),
    LineEndingsCase(
        # `binary` is git's macro for `-text`, so a path it covers carries no
        # ending and a narrower editor section must not be reported against it.
        "Passes_WhenTheBinaryMacroCoversAPathTheEditorNarrows",
        EDITOR_CONFIG_LF + "\n\n[*.png]\nend_of_line = crlf\n",
        "* text=auto eol=lf\n*.png binary\n",
        True,
    ),
    LineEndingsCase(
        # A `**` pattern is refused rather than read as two `*`: a matcher that
        # guessed would stop matching deeper paths and hide the drift behind them.
        "Fails_WhenAGitAttributesRuleUsesARecursiveGlob",
        EDITOR_CONFIG_LF,
        "* text=auto eol=lf\nEditor/** text eol=crlf\n",
        False,
        r"recursive '\*\*' segment in 'Editor/\*\*'",
    ),
    LineEndingsCase(
        "Fails_WhenAnEditorConfigSectionUsesARecursiveGlob",
        EDITOR_CONFIG_LF + "\n\n[Editor/**]\nend_of_line = crlf\n",
        GIT_ATTRIBUTES_LF,
        False,
        r"recursive '\*\*' segment in 'Editor/\*\*'",
    ),
    LineEndingsCase(
        # Comparing only the first alternative left `*.ps1` unchecked, so a real
        # disagreement passed. Git pins `*.md` back to the default; `*.ps1` still
        # checks out LF while the editor asks for CRLF.
        "Fails_WhenALaterBraceAlternativeDriftsOnTheGitSide",
        EDITOR_CONFIG_CRLF,
        "* text=auto eol=crlf\n{*.md,*.ps1} text eol=lf\n*.md text eol=crlf\n",
        False,
        r"'sample\.ps1': git checks out lf.+requires crlf",
    ),
    LineEndingsCase(
        # The mirror image: the narrowed section carries the brace group and git
        # leaves the second extension on the broad rule.
        "Fails_WhenALaterBraceAlternativeDriftsOnTheEditorSide",
        EDITOR_CONFIG_LF + "\n\n[{*.md,*.ps1}]\nend_of_line = crlf\n",
        "* text=auto eol=lf\n*.md text eol=crlf\n",
        False,
        r"'sample\.ps1': git checks out lf.+requires crlf",
    ),
    LineEndingsCase(
        # Every alternative of a brace group compared on both sides and every one
        # in sync, so comparing the later ones must not start reporting the
        # alternatives that agree.
        "Passes_WhenEveryBraceAlternativeAgrees",
        EDITOR_CONFIG_LF + "\n\n[{*.md,*.ps1}]\nend_of_line = crlf\n",
        "* text=auto eol=lf\n{*.md,*.ps1} text eol=crlf\n",
        True,
    ),
    LineEndingsCase(
        # `*.ps1` comes from the inner group, so a split that stops at the first
        # `}` or that ignores nesting depth never compares it and its drift goes
        # unreported.
        "Fails_WhenBraceGroupsNest",
        EDITOR_CONFIG_CRLF + "\n\n[*.{cs,{ps1,bat}}]\nend_of_line = lf\n",
        "* text=auto eol=crlf\n*.ps1 text eol=crlf\n",
        False,
        r"'sample\.ps1': git checks out crlf.+requires lf",
    ),
    LineEndingsCase(
        "Passes_WhenNestedBraceGroupsAgree",
        EDITOR_CONFIG_LF + "\n\n[*.{cs,{ps1,bat}}]\nend_of_line = crlf\n",
        "* text=auto eol=lf\n*.cs text eol=crlf\n*.ps1 text eol=crlf\n*.bat text eol=crlf\n",
        True,
    ),
    LineEndingsCase(
        # The other constructs the matcher cannot resolve exactly. Each would
        # otherwise be read as literal text and silently stop matching.
        "Fails_WhenAGitAttributesRuleIsAnchored",
        EDITOR_CONFIG_LF,
        "* text=auto eol=lf\n/docs/*.md text eol=crlf\n",
        False,
        r"anchored or directory-only '/' in '/docs/\*\.md'",
    ),
    LineEndingsCase(
        "Fails_WhenAGitAttributesRuleIsDirectoryOnly",
        EDITOR_CONFIG_LF,
        "* text=auto eol=lf\nbuild/ text eol=crlf\n",
        False,
        r"anchored or directory-only '/' in 'build/'",
    ),
    LineEndingsCase(
        "Fails_WhenAGitAttributesRuleUsesACharacterClass",
        EDITOR_CONFIG_LF,
        "* text=auto eol=lf\n*[0-9].cs text eol=crlf\n",
        False,
        r"character class in '\*\[0-9\]\.cs'",
    ),
    LineEndingsCase(
        "Fails_WhenAGitAttributesRuleUsesABackslashEscape",
        EDITOR_CONFIG_LF,
        "* text=auto eol=lf\ndocs/\\*.md text eol=crlf\n",
        False,
        r"backslash escape in 'docs/\\\*\.md'",
    ),
    LineEndingsCase(
        "Fails_WhenAnEditorConfigSectionIsAnchored",
        EDITOR_CONFIG_LF + "\n\n[/docs/*.md]\nend_of_line = crlf\n",
        GIT_ATTRIBUTES_LF,
        False,
        r"anchored or directory-only '/' in '/docs/\*\.md'",
    ),
]


def run_table_case(case: LineEndingsCase) -> None:
    root = new_temp_root(prefix="line-endings-")
    try:
        write_fixture_file(root, ".editorconfig", case.editor_config)
        write_fixture_file(root, ".gitattributes", case.git_attributes)
        output, exit_code = invoke_fixture_lint(root)
        if case.expect_pass:
            assert_exit_code(0, f"expected a clean contract: {output}", exit_code)
        else:
            assert_exit_code(1, f"expected drift to fail: {output}", exit_code)
            assert_true(
                re.search(case.expect, output, re.IGNORECASE) is not None,
                f"expected '{case.expect}' in: {output}",
            )
    finally:
        remove_temp_root(root)


def case_fails_when_a_git_attributes_file_is_missing() -> None:
    root = new_temp_root(prefix="line-endings-")
    try:
        write_fixture_file(root, ".editorconfig", EDITOR_CONFIG_LF)
        output, exit_code = invoke_fixture_lint(root)
        assert_exit_code(1, f"a missing file must fail closed: {output}", exit_code)
        assert_true(
            re.search("git attributes", output, re.IGNORECASE) is not None,
            f"must explain the unreadable input: {output}",
        )
    finally:
        remove_temp_root(root)


def case_passes_against_the_repository_itself() -> None:
    output, exit_code = invoke_line_ending_lint(
        str(REPO_ROOT / ".editorconfig"),
        str(REPO_ROOT / ".gitattributes"),
        ["--verbose"],
    )
    assert_exit_code(0, f"the repository must satisfy its own contract: {output}", exit_code)
    assert_true(
        re.search(r"Line-ending contract in sync: lf across \d+ path", output, re.IGNORECASE) is not None,
        f"verbose output must report the resolved ending: {output}",
    )


def main() -> int:
    print("== Line-ending contract self-tests ==")

    for case in CASES:
        run_case(case.name, lambda bound_case=case: run_table_case(bound_case))
    run_case("Fails_WhenAGitAttributesFileIsMissing", case_fails_when_a_git_attributes_file_is_missing)
    run_case("Passes_AgainstTheRepositoryItself", case_passes_against_the_repository_itself)

    failures = failure_count()
    if failures > 0:
        print(f"line-ending self-tests: {failures} failed")
        return 1
    print("line-ending self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
