#!/usr/bin/env python3
"""Assembly warning-policy self-tests (typed Python port of ``test-assembly-warnings.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions,
including the data-driven response-file table; only the driver language
changed.
"""

from __future__ import annotations

import re
import subprocess
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
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
LINT_SCRIPT = SCRIPTS_DIR / "lint_assembly_warnings.py"

ASSEMBLY_DEFINITION = '{"name":"Fixture.Assembly"}'
WARNINGS_AS_ERRORS_RULESET = """<?xml version="1.0" encoding="utf-8"?>
<RuleSet Name="Warnings as errors" Description="Test fixture" ToolsVersion="10.0">
  <IncludeAll Action="Error" />
</RuleSet>
"""
MAXIMUM_WARNINGS_RESPONSE_FILE = "-warn:5"


@dataclass(frozen=True)
class ResponseFileCase:
    """One response file and the outcome the guard owes it.

    The suppression rows are the shapes a prefix list accepts by not naming
    them: a switch it never reads turns off the policy it claims to enforce.
    The last rows are constructs the guard does not model at all, so it
    refuses them instead of guessing what they do.
    """

    name: str
    content: str
    expect_pass: bool
    expect: str


RESPONSE_FILE_CASES: List[ResponseFileCase] = [
    ResponseFileCase("Passes_WhenTheResponseFileCarriesOnlyTheMaximumWarningLevel", "-warn:5", True, ""),
    ResponseFileCase("Passes_WhenTheResponseFileUsesTheShortWarningLevelForm", "-w:5", True, ""),
    ResponseFileCase("Passes_WhenTheResponseFileUsesTheSlashSpelling", "/warn:5", True, ""),
    # Blank lines are not arguments, and most committed files end with one.
    ResponseFileCase("Passes_WhenTheResponseFileEndsWithBlankLines", "-warn:5\n\n", True, ""),
    ResponseFileCase("Fails_WhenTheWarningLevelIsLowerThanMaximum", "-warn:3", False, "found: -warn:3"),
    ResponseFileCase("Fails_WhenTheWarningLevelIsMalformed", "-warn:all", False, "found: -warn:all"),
    ResponseFileCase(
        "Fails_WhenTheResponseFileSuppressesNamedWarnings",
        "-warn:5\n-nowarn:1701,CS0162\n",
        False,
        "found: -warn:5 -nowarn:1701,CS0162",
    ),
    ResponseFileCase(
        "Fails_WhenTheResponseFileSuppressesEveryWarning",
        "-warn:5\n-nowarn\n",
        False,
        "found: -warn:5 -nowarn",
    ),
    ResponseFileCase(
        "Fails_WhenTheResponseFileUsesTheSlashSuppressionForm",
        "-warn:5\n/nowarn\n",
        False,
        "found: -warn:5 /nowarn",
    ),
    ResponseFileCase(
        "Fails_WhenTheResponseFileDisablesWarningsAsErrors",
        "-warn:5\n-warnaserror-\n",
        False,
        "found: -warn:5 -warnaserror-",
    ),
    ResponseFileCase(
        "Fails_WhenTheResponseFileDisablesWarningsAsErrorsWithTheSlashForm",
        "/warn:5\n/warnaserror-\n",
        False,
        "found: /warn:5 /warnaserror-",
    ),
    # The ruleset already promotes every warning, so a second switch either
    # way gives the one policy a second source of truth.
    ResponseFileCase(
        "Fails_WhenTheResponseFileEnablesWarningsAsErrorsSeparately",
        "-warn:5\n-warnaserror+\n",
        False,
        r"found: -warn:5 -warnaserror\+",
    ),
    ResponseFileCase(
        "Fails_WhenTheResponseFileLoadsAPackageAnalyzer",
        '-warn:5\n-analyzer:"Runtime/Analyzer.dll"\n',
        False,
        r'found: -warn:5 -analyzer:"Runtime/Analyzer\.dll"',
    ),
    ResponseFileCase(
        "Fails_WhenTheResponseFileHidesASuppressionBehindAComment",
        "-warn:5\n# -nowarn:1701\n",
        False,
        "found: -warn:5 # -nowarn:1701",
    ),
    ResponseFileCase(
        "Fails_WhenTheResponseFilePacksTwoArgumentsOnOneLine",
        "-warn:5 -nowarn:1701",
        False,
        "must contain exactly one argument",
    ),
    ResponseFileCase(
        "Fails_WhenTheResponseFileRepeatsTheWarningLevel",
        "-warn:5\n-warn:5\n",
        False,
        "found: -warn:5 -warn:5",
    ),
    ResponseFileCase("Fails_WhenTheResponseFileIsEmpty", "", False, "found: no arguments"),
]


def run_lint(python: str, arguments: Sequence[str]) -> Tuple[str, int]:
    """Run the lint and return (merged output, exit code)."""
    completed = subprocess.run(
        [python, str(LINT_SCRIPT), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return (completed.stdout or "") + (completed.stderr or ""), completed.returncode


def write_assembly_fixture(
    root: Path,
    directory: str,
    ruleset: str = WARNINGS_AS_ERRORS_RULESET,
    response_file: str = MAXIMUM_WARNINGS_RESPONSE_FILE,
) -> None:
    """Write the standard fixture assembly: asmdef, ruleset, and csc.rsp."""
    write_fixture_file(root, f"{directory}/Fixture.asmdef", ASSEMBLY_DEFINITION)
    write_fixture_file(root, f"{directory}/WarningsAsErrors.ruleset", ruleset)
    write_fixture_file(root, f"{directory}/csc.rsp", response_file)


def case_passes_for_repository_assemblies(python: str) -> None:
    repo_root = TESTS_DIR.parent.parent
    _, exit_code = run_lint(python, ["--root", str(repo_root)])
    assert_exit_code(0, "repository assemblies should satisfy the warning policy", exit_code)


def case_passes_when_every_assembly_has_warnings_as_errors(python: str) -> None:
    root = new_temp_root()
    try:
        write_assembly_fixture(root, "Runtime")
        write_assembly_fixture(root, "Editor/Nested")
        _, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(0, "complete warnings-as-errors coverage should pass", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_when_assembly_has_no_ruleset(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, "Runtime/Fixture.asmdef", ASSEMBLY_DEFINITION)
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "an assembly without a ruleset should fail", exit_code)
        assert_true("Runtime/Fixture.asmdef" in output, f"output should name the assembly, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_assembly_has_no_compiler_response_file(python: str) -> None:
    root = new_temp_root()
    try:
        write_fixture_file(root, "Runtime/Fixture.asmdef", ASSEMBLY_DEFINITION)
        write_fixture_file(root, "Runtime/WarningsAsErrors.ruleset", WARNINGS_AS_ERRORS_RULESET)
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "an assembly without csc.rsp should fail", exit_code)
        assert_true(
            "expected exactly one named csc.rsp" in output,
            f"output should require csc.rsp, got: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_when_assembly_has_ambiguous_compiler_response_files(python: str) -> None:
    root = new_temp_root()
    try:
        write_assembly_fixture(root, "Runtime")
        write_fixture_file(root, "Runtime/mcs.rsp", "-warn:4")
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "an assembly with ambiguous response files should fail", exit_code)
        assert_true("2 compiler response files" in output, f"output should report the ambiguity, got: {output}")
    finally:
        remove_temp_root(root)


def run_response_file_case(python: str, case: ResponseFileCase) -> None:
    root = new_temp_root(prefix="assembly-warnings-")
    try:
        write_assembly_fixture(root, "Runtime", response_file=case.content)
        output, exit_code = run_lint(python, ["--root", str(root)])
        if case.expect_pass:
            assert_exit_code(0, f"{case.name} should pass", exit_code)
        else:
            assert_exit_code(1, f"{case.name} should fail", exit_code)
            assert_true("Runtime/csc.rsp" in output, f"the response file should be named, got: {output}")
            # The PowerShell original matched with -match, whose patterns are
            # regexes; the expectations keep that semantics (the escaped
            # rows rely on it).
            assert_true(
                re.search(case.expect, output) is not None,
                f"expected '{case.expect}' in: {output}",
            )
    finally:
        remove_temp_root(root)


def case_fails_when_assembly_has_multiple_rulesets(python: str) -> None:
    root = new_temp_root()
    try:
        write_assembly_fixture(root, "Runtime")
        write_fixture_file(root, "Runtime/Override.ruleset", WARNINGS_AS_ERRORS_RULESET)
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "ambiguous assembly rulesets should fail", exit_code)
        assert_true("2 rulesets" in output, f"output should report the ambiguity, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_ruleset_is_malformed(python: str) -> None:
    root = new_temp_root()
    try:
        write_assembly_fixture(root, "Runtime", ruleset="<RuleSet><IncludeAll")
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "malformed ruleset XML should fail", exit_code)
        assert_true("valid XML" in output, f"output should explain malformed XML, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_include_all_does_not_promote_warnings(python: str) -> None:
    root = new_temp_root()
    try:
        warning_ruleset = (
            '<RuleSet Name="Warnings only" Description="Test fixture" ToolsVersion="10.0">\n'
            '  <IncludeAll Action="Warning" />\n'
            "</RuleSet>\n"
        )
        write_assembly_fixture(root, "Runtime", ruleset=warning_ruleset)
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "a non-error IncludeAll action should fail", exit_code)
        assert_true('IncludeAll Action="Error"' in output, f"output should name the policy, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_when_repository_contains_labeled_analyzer(python: str) -> None:
    root = new_temp_root()
    try:
        write_assembly_fixture(root, "Runtime")
        write_fixture_file(root, "Tools/Analyzer.dll.meta", "labels:\n- RoslynAnalyzer")
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "a repository-local labeled analyzer should fail", exit_code)
        assert_true(
            "repository-local analyzer payload" in output,
            f"output should reject the analyzer payload, got: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_when_repository_contains_known_analyzer_payload(python: str) -> None:
    root = new_temp_root()
    try:
        write_assembly_fixture(root, "Runtime")
        write_fixture_file(root, "Tools/ErrorProne.NET.NOTICE.md", "development analyzer notice")
        output, exit_code = run_lint(python, ["--root", str(root)])
        assert_exit_code(1, "a known repository-local analyzer payload should fail", exit_code)
        assert_true(
            "repository-local analyzer payload" in output,
            f"output should reject the known analyzer payload, got: {output}",
        )
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== assembly warnings policy self-tests ==")

    # The lint is typed Python (issue #171); the tests only need a working
    # interpreter because the fixtures never touch a toolchain.
    python = find_python()

    run_cases([
        ("Passes_ForRepositoryAssemblies", lambda: case_passes_for_repository_assemblies(python)),
        ("Passes_When_EveryAssemblyHasWarningsAsErrors", lambda: case_passes_when_every_assembly_has_warnings_as_errors(python)),
        ("Fails_WhenAssemblyHasNoRuleset", lambda: case_fails_when_assembly_has_no_ruleset(python)),
        ("Fails_WhenAssemblyHasNoCompilerResponseFile", lambda: case_fails_when_assembly_has_no_compiler_response_file(python)),
        ("Fails_WhenAssemblyHasAmbiguousCompilerResponseFiles", lambda: case_fails_when_assembly_has_ambiguous_compiler_response_files(python)),
        *[(response_file_case.name, lambda case=response_file_case: run_response_file_case(python, case)) for response_file_case in RESPONSE_FILE_CASES],
        ("Fails_WhenAssemblyHasMultipleRulesets", lambda: case_fails_when_assembly_has_multiple_rulesets(python)),
        ("Fails_WhenRulesetIsMalformed", lambda: case_fails_when_ruleset_is_malformed(python)),
        ("Fails_WhenIncludeAllDoesNotPromoteWarnings", lambda: case_fails_when_include_all_does_not_promote_warnings(python)),
        ("Fails_WhenRepositoryContainsLabeledAnalyzer", lambda: case_fails_when_repository_contains_labeled_analyzer(python)),
        ("Fails_WhenRepositoryContainsKnownAnalyzerPayload", lambda: case_fails_when_repository_contains_known_analyzer_payload(python)),
    ])

    failures = failure_count()
    print(f"== assembly warnings policy: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
