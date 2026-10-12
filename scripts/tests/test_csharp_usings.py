#!/usr/bin/env python3
"""C# using-placement self-tests (typed Python port of ``test-lint-csharp-usings.ps1``, issue #171).

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
    fixture_path,
    new_temp_root,
    remove_temp_root,
    run_case,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
LINT_SCRIPT = SCRIPTS_DIR / "lint_csharp_usings.py"


def invoke_using_lint(root: Path, lint_arguments: Sequence[str] = ()) -> Tuple[str, int]:
    """Run the lint with the roots override env var set, then restore it."""
    had_previous = "CSHARP_USINGS_ROOTS" in os.environ
    previous = os.environ.get("CSHARP_USINGS_ROOTS")
    try:
        os.environ["CSHARP_USINGS_ROOTS"] = str(root)
        return run_tool_output([find_python(), str(LINT_SCRIPT), *lint_arguments])
    finally:
        if had_previous:
            os.environ["CSHARP_USINGS_ROOTS"] = previous or ""
        else:
            os.environ.pop("CSHARP_USINGS_ROOTS", None)


COMPLIANT_FIXTURE = """namespace Demo
{
    using System;
    using System.Collections.Generic;
    using Inspector = UnityEditor.Editor;

    internal sealed class Compliant
    {
        private readonly List<int> values = new List<int>();
    }
}
"""

MISPLACED_FIXTURE = """// Misplaced.cs carries a header comment above the misplaced usings.
using System;
using System.Collections.Generic;

namespace Demo
{
    internal sealed class Misplaced
    {
        private readonly List<int> values = new List<int>();
    }
}
"""

SANCTIONED_PREAMBLE_FIXTURE = """using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Other.Assembly")]

namespace Demo
{
    using System;

    internal sealed class Sanctioned { }
}
"""

NAMESPACELESS_FIXTURE = """using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Sonar Code Smell", "S101")]

"""

MASKED_FIXTURE = """namespace Demo
{
    // using System;
    /* using UnityEngine; */
    private const string Usage = "using System;";

    internal sealed class Masked { }
}
"""

BARRIER_FIXTURE = """using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Demo
{
    internal sealed class Barrier { }
}
"""


def case_passes_usings_inside_namespace() -> None:
    root = new_temp_root(prefix="usings-")
    try:
        write_fixture_file(root, "Compliant.cs", COMPLIANT_FIXTURE)
        output, exit_code = invoke_using_lint(root)
        assert_exit_code(0, f"compliant fixture should pass: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_on_usings_outside_namespace_with_locations() -> None:
    root = new_temp_root(prefix="usings-")
    try:
        write_fixture_file(root, "Misplaced.cs", MISPLACED_FIXTURE)
        output, exit_code = invoke_using_lint(root)
        assert_exit_code(1, f"misplaced fixture should fail: {output}", exit_code)
        assert_true(
            re.search(r"Misplaced\.cs:2", output, re.IGNORECASE) is not None,
            f"must report the first using line: {output}",
        )
        assert_true(
            re.search(r"Misplaced\.cs:3", output, re.IGNORECASE) is not None,
            f"must report the second using line: {output}",
        )
    finally:
        remove_temp_root(root)


def case_passes_sanctioned_assembly_attribute_shapes() -> None:
    root = new_temp_root(prefix="usings-")
    try:
        write_fixture_file(root, "Sanctioned.cs", SANCTIONED_PREAMBLE_FIXTURE)
        write_fixture_file(root, "Namespaceless.cs", NAMESPACELESS_FIXTURE)
        output, exit_code = invoke_using_lint(root)
        assert_exit_code(0, f"assembly-attribute preambles and namespaceless files must pass: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_ignores_comments_strings_and_non_csharp_files() -> None:
    root = new_temp_root(prefix="usings-")
    try:
        write_fixture_file(root, "Masked.cs", MASKED_FIXTURE)
        write_fixture_file(root, "notes.md", "using System;")
        output, exit_code = invoke_using_lint(root)
        assert_exit_code(0, f"masked occurrences and non-C# files must pass: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_fix_moves_usings_inside_namespace_and_repairs_exit_code() -> None:
    root = new_temp_root(prefix="usings-")
    try:
        relative_path = "Misplaced.cs"
        write_fixture_file(root, relative_path, MISPLACED_FIXTURE)
        misplacement = fixture_path(root, relative_path)
        before = misplacement.read_text(encoding="utf-8")
        output, exit_code = invoke_using_lint(root, ["--fix"])
        assert_exit_code(0, f"fix should leave the tree clean: {output}", exit_code)
        assert_true(
            re.search(r"Misplaced\.cs", output, re.IGNORECASE) is not None,
            f"fix should name the repaired file: {output}",
        )

        after = misplacement.read_text(encoding="utf-8")
        assert_true(after != before, "fix must rewrite the fixture")
        assert_true(
            re.search("header comment above the misplaced usings", after, re.IGNORECASE) is not None,
            "fix must preserve header comments",
        )
        assert_true(
            re.search(r"namespace Demo\s*\{\s*\r?\n\s*using System;", after) is not None,
            f"usings must move inside the namespace: {after}",
        )

        verification_output, verification_exit = invoke_using_lint(root)
        assert_exit_code(0, f"post-fix scan must pass: {verification_output}", verification_exit)
    finally:
        remove_temp_root(root)


def case_fix_declines_barrier_regions_without_rewriting() -> None:
    root = new_temp_root(prefix="usings-")
    try:
        write_fixture_file(root, "Barrier.cs", BARRIER_FIXTURE)
        barrier = fixture_path(root, "Barrier.cs")
        before = barrier.read_text(encoding="utf-8")
        output, exit_code = invoke_using_lint(root, ["--fix"])
        assert_exit_code(1, f"barrier region must keep the failure: {output}", exit_code)
        assert_true(
            re.search("manual barrier", output, re.IGNORECASE) is not None,
            f"fix must report the declined barrier: {output}",
        )
        after = barrier.read_text(encoding="utf-8")
        assert_true(after == before, "fix must not rewrite a barrier region")
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== C# using-placement self-tests ==")

    run_case("Passes_UsingsInsideNamespace", case_passes_usings_inside_namespace)
    run_case("Fails_OnUsingsOutsideNamespaceWithLocations", case_fails_on_usings_outside_namespace_with_locations)
    run_case("Passes_SanctionedAssemblyAttributeShapes", case_passes_sanctioned_assembly_attribute_shapes)
    run_case("Ignores_CommentsStringsAndNonCSharpFiles", case_ignores_comments_strings_and_non_csharp_files)
    run_case("Fix_MovesUsingsInsideNamespaceAndRepairsExitCode", case_fix_moves_usings_inside_namespace_and_repairs_exit_code)
    run_case("Fix_DeclinesBarrierRegionsWithoutRewriting", case_fix_declines_barrier_regions_without_rewriting)

    failures = failure_count()
    if failures > 0:
        print(f"using-placement self-tests: {failures} failed")
        return 1
    print("using-placement self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
