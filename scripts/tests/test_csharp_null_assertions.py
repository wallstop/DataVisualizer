#!/usr/bin/env python3
"""C# null-assertion self-tests (typed Python port of ``test-lint-csharp-null-assertions.ps1``, issue #171).

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
LINT_SCRIPT = SCRIPTS_DIR / "lint_csharp_null_assertions.py"


def invoke_null_assertion_lint(root: Path, lint_arguments: Sequence[str] = ()) -> Tuple[str, int]:
    """Run the lint with the roots override env var set, then restore it."""
    had_previous = "CSHARP_NULL_ASSERTION_ROOTS" in os.environ
    previous = os.environ.get("CSHARP_NULL_ASSERTION_ROOTS")
    try:
        os.environ["CSHARP_NULL_ASSERTION_ROOTS"] = str(root)
        return run_tool_output([find_python(), str(LINT_SCRIPT), *lint_arguments])
    finally:
        if had_previous:
            os.environ["CSHARP_NULL_ASSERTION_ROOTS"] = previous or ""
        else:
            os.environ.pop("CSHARP_NULL_ASSERTION_ROOTS", None)


CLEAN_FIXTURE = """using NUnit.Framework;
using UnityEngine;

public sealed class NullComparisonTests
{
    [Test]
    public void ShouldUseExplicitUnityNullComparisons()
    {
        ScriptableObject asset = null;
        Assert.That(asset == null);
        asset = ScriptableObject.CreateInstance<ScriptableObject>();
        Assert.That(asset != null, "asset must be created");
        Object.DestroyImmediate(asset);
        Assert.That(asset == null, "destroyed wrapper must compare null");
        string identifier = asset.GetInstanceID().ToString();
        Assert.That(identifier, Is.Not.Empty);
    }
}
"""

VIOLATING_FIXTURE = """using NUnit.Framework;
using UnityEngine;

public sealed class BannedTests
{
    [Test]
    public void ShouldUseExplicitUnityNullComparisons()
    {
        ScriptableObject asset = null;
        Assert.IsNull(asset);
        Assert.IsNotNull(asset, "asset must be created");
    }

    [Test]
    public void ShouldUseExplicitUnityNullConstraints()
    {
        ScriptableObject asset = null;
        Assert.That(asset, Is.Null);
        string identifier = "id";
        Assert.That(identifier, Is.Not.Null.And.Not.Empty);
    }
}
"""


def case_passes_explicit_unity_null_comparisons() -> None:
    root = new_temp_root(prefix="null-assertions-")
    try:
        write_fixture_file(root, "Clean.cs", CLEAN_FIXTURE)
        output, exit_code = invoke_null_assertion_lint(root)
        assert_exit_code(0, f"clean fixture should pass: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_on_assert_is_null_and_is_not_null_with_locations() -> None:
    root = new_temp_root(prefix="null-assertions-")
    try:
        write_fixture_file(root, "Banned.cs", VIOLATING_FIXTURE)
        output, exit_code = invoke_null_assertion_lint(root)
        assert_exit_code(1, f"violating fixture should fail: {output}", exit_code)
        assert_true(
            re.search(r"Banned\.cs:10", output, re.IGNORECASE) is not None,
            f"must report Assert.IsNull line: {output}",
        )
        assert_true(
            re.search(r"Banned\.cs:11", output, re.IGNORECASE) is not None,
            f"must report Assert.IsNotNull line: {output}",
        )
        assert_true(
            re.search(r"Banned\.cs:18", output, re.IGNORECASE) is not None,
            f"must report Is.Null line: {output}",
        )
        assert_true(
            re.search(r"Banned\.cs:20", output, re.IGNORECASE) is not None,
            f"must report Is.Not.Null line: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_on_violations_in_nested_directories() -> None:
    root = new_temp_root(prefix="null-assertions-")
    try:
        write_fixture_file(root, "Tests/Editor/Nested/Banned.cs", VIOLATING_FIXTURE)
        output, exit_code = invoke_null_assertion_lint(root)
        assert_exit_code(1, f"nested violation should fail: {output}", exit_code)
        assert_true(
            re.search(r"Tests/Editor/Nested/Banned\.cs:10", output, re.IGNORECASE) is not None,
            f"must report the nested path: {output}",
        )
    finally:
        remove_temp_root(root)


def case_ignores_comments_strings_and_non_csharp_files() -> None:
    root = new_temp_root(prefix="null-assertions-")
    try:
        masked = """public sealed class Masked
{
    // Assert.IsNull(reference);
    /* Assert.IsNotNull(reference); */
    // Is.Null(reference);
    /* Is.Not.Null.And.Not.Empty */
    private const string Usage = "Assert.IsNull(reference);";
    private readonly string[] usages = { "Assert.IsNotNull(reference);", "Is.Null" };

    public void Run()
    {
        string message = $"Assert.IsNull(reference);";
        System.Console.WriteLine(message);
    }
}
"""
        write_fixture_file(root, "Masked.cs", masked)
        write_fixture_file(root, "notes.md", "Assert.IsNull(x);")
        output, exit_code = invoke_null_assertion_lint(root)
        assert_exit_code(0, f"masked occurrences and non-C# files must pass: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_paths_scans_only_the_named_files() -> None:
    root = new_temp_root(prefix="null-assertions-")
    try:
        write_fixture_file(root, "Clean.cs", CLEAN_FIXTURE)
        write_fixture_file(root, "Banned.cs", VIOLATING_FIXTURE)
        # The scoped verdict is the whole-tree verdict restricted to the
        # named files: a clean named file passes even when the tree holds a
        # violation elsewhere, and the scan count covers the name only.
        output, exit_code = invoke_null_assertion_lint(root, ("--paths", str(root / "Clean.cs"), "--verbose"))
        assert_exit_code(0, f"the named clean file must pass: {output}", exit_code)
        assert_true(
            "1 file(s) scanned" in output,
            f"the scoped scan must count only the named file: {output}",
        )
        output, exit_code = invoke_null_assertion_lint(root, ("--paths", str(root / "Banned.cs")))
        assert_exit_code(1, f"the named violating file must fail: {output}", exit_code)
        assert_true(
            re.search(r"Banned\.cs:\d+", output, re.IGNORECASE) is not None,
            f"the scoped scan must report the violation: {output}",
        )
        # A name that is not a readable C# file fails closed: the scan can
        # never quietly verify nothing.
        output, exit_code = invoke_null_assertion_lint(root, ("--paths", str(root / "Missing.cs")))
        assert_exit_code(2, f"a missing --paths entry must fail closed: {output}", exit_code)
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== C# null-assertion self-tests ==")

    run_case("Passes_ExplicitUnityNullComparisons", case_passes_explicit_unity_null_comparisons)
    run_case("Fails_OnAssertIsNullAndIsNotNullWithLocations", case_fails_on_assert_is_null_and_is_not_null_with_locations)
    run_case("Fails_OnViolationsInNestedDirectories", case_fails_on_violations_in_nested_directories)
    run_case("Ignores_CommentsStringsAndNonCSharpFiles", case_ignores_comments_strings_and_non_csharp_files)
    run_case("Paths_ScansOnlyTheNamedFiles", case_paths_scans_only_the_named_files)

    failures = failure_count()
    if failures > 0:
        print(f"null-assertion self-tests: {failures} failed")
        return 1
    print("null-assertion self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
