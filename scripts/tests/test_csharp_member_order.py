#!/usr/bin/env python3
"""C# member-order self-tests (typed Python port of ``test-csharp-member-order.ps1``, issue #171).

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
LINT_SCRIPT = SCRIPTS_DIR / "lint_csharp_member_order.py"


def invoke_member_order_lint(root: Path, lint_arguments: Sequence[str] = ()) -> Tuple[str, int]:
    """Run the lint with the roots override env var set, then restore it."""
    had_previous = "CSHARP_MEMBER_ORDER_ROOTS" in os.environ
    previous = os.environ.get("CSHARP_MEMBER_ORDER_ROOTS")
    try:
        os.environ["CSHARP_MEMBER_ORDER_ROOTS"] = str(root)
        return run_tool_output([find_python(), str(LINT_SCRIPT), *lint_arguments])
    finally:
        if had_previous:
            os.environ["CSHARP_MEMBER_ORDER_ROOTS"] = previous or ""
        else:
            os.environ.pop("CSHARP_MEMBER_ORDER_ROOTS", None)


ORDERED_FIXTURE = """using System;
public sealed class Ordered
{
    public const int Limit = 1;
    public event Action Changed;
    public delegate void Callback();
    public static int SharedValue { get; set; }
    public static int SharedField;
    public int Value { get; set; }
    public int Field;
    public Ordered() { }
    public static void Reset() { }
    public void Run() { }
    private sealed class Nested { }
}
"""


def case_passes_canonical_order() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(root, "Ordered.cs", ORDERED_FIXTURE)
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(0, f"ordered fixture should pass: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_on_tier_and_access_inversions() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "Bad.cs",
            """public sealed class Bad
{
    private int _field;
    public int Value { get; set; }
    public int PublicField;
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(1, "misordered fixture should fail", exit_code)
        assert_true(
            re.search("#672", output, re.IGNORECASE) is not None,
            f"failure should identify the ordering rule: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_on_underscored_method_name() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "BadMethodName.cs",
            """public sealed class BadMethodName
{
    public void Should_Run() { }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(1, "underscored method name should fail", exit_code)
        assert_true(
            re.search("#47", output, re.IGNORECASE) is not None,
            f"failure should identify the naming rule: {output}",
        )
        assert_true(
            re.search("Should_Run", output, re.IGNORECASE) is not None,
            f"failure should identify the method: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_on_underscored_generic_method_name() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "BadGenericMethodName.cs",
            """public sealed class BadGenericMethodName
{
    public void Should_Run<T>() { }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(1, "underscored generic method name should fail", exit_code)
        assert_true(
            re.search("Should_Run", output, re.IGNORECASE) is not None,
            f"failure should identify the generic method: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_on_consecutive_ordinary_line_comments() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "BadComment.cs",
            """public sealed class BadComment
{
    // First line of one explanation.
    // Second line of the same explanation.
    public void Run() { }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(1, "consecutive ordinary line comments should fail", exit_code)
        assert_true(
            re.search("#57", output, re.IGNORECASE) is not None,
            f"failure should identify the comment rule: {output}",
        )
        assert_true(
            re.search("BadComment.cs:3", output, re.IGNORECASE) is not None,
            f"failure should identify the first line: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_on_leading_bom_consecutive_ordinary_line_comments() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        content = "\ufeff" + (
            "// First line of one explanation.\n"
            "// Second line of the same explanation.\n"
            "public sealed class BadBomComment { }\n"
        )
        write_fixture_file(root, "BadBomComment.cs", content)
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(1, "a BOM must not hide leading consecutive comments", exit_code)
        assert_true(
            re.search("BadBomComment.cs:1", output, re.IGNORECASE) is not None,
            f"failure should identify the first line: {output}",
        )
    finally:
        remove_temp_root(root)


def case_passes_supported_comment_forms() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "SupportedComments.cs",
            """// ReSharper disable FirstInspection
// ReSharper disable SecondInspection
public sealed class SupportedComments
{
    /// <summary>
    /// XML documentation remains line-oriented.
    /// </summary>
    public int Value { get; set; }

    public string Example { get; } = @"
// Content inside a multi-line string is not a comment.
// Consecutive content lines must remain valid.
";

    /*
        Ordinary explanations spanning lines use an indented block.
        Single-line comments remain valid too.
    */
    public void Run() { }

    /*
// Content inside a block comment is not a line-comment block.
// Its leading markers must remain valid too.
    */
    // One standalone note is valid.
    private void Stop() { }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(0, f"supported comment forms should pass: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_on_runtime_linq_imports_and_qualified_calls() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "Runtime/UsesLinq.cs",
            """namespace Fixture
{
    using System.Linq;
    using Query = global::System.Linq.Enumerable;
    using static System.Linq.Enumerable;

    public sealed class UsesLinq
    {
        public int Count(int[] values) { return System.Linq.Enumerable.Count(values); }
    }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(1, "Runtime LINQ dependencies should fail", exit_code)
        assert_true(
            re.search("#61", output, re.IGNORECASE) is not None,
            f"failure should identify the LINQ rule: {output}",
        )
        assert_true(
            re.search("Runtime/UsesLinq.cs:3", output, re.IGNORECASE) is not None,
            f"failure should identify the first import: {output}",
        )
        assert_true(
            re.search("Runtime/UsesLinq.cs:4", output, re.IGNORECASE) is not None,
            f"failure should identify the aliased import: {output}",
        )
        assert_true(
            re.search("Runtime/UsesLinq.cs:5", output, re.IGNORECASE) is not None,
            f"failure should identify the static import: {output}",
        )
        assert_true(
            re.search("Runtime/UsesLinq.cs:9", output, re.IGNORECASE) is not None,
            f"failure should identify the qualified call: {output}",
        )
    finally:
        remove_temp_root(root)


def case_passes_runtime_linq_text_in_comments_and_strings() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "Runtime/NoLinqDependency.cs",
            """public sealed class NoLinqDependency
{
    public string Example { get; } = "System.Linq.Enumerable";

    // System.Linq is named only as documentation.
    public void Run() { }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(0, f"comments and strings should not create LINQ violations: {output}", exit_code)
    finally:
        remove_temp_root(root)


def _lints_linq_fixture(relative_path: str) -> None:
    """Assert one persisted-state LINQ fixture fails with the shared rule id."""
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            relative_path,
            """namespace Fixture
{
    using System.Linq;

    public sealed class UsesLinq { }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(1, f"{relative_path} LINQ dependencies should fail", exit_code)
        assert_true(
            re.search("#61", output, re.IGNORECASE) is not None,
            f"failure should identify the LINQ rule: {output}",
        )
        assert_true(
            re.search(re.escape(relative_path) + ":3", output, re.IGNORECASE) is not None,
            f"failure should identify the import: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_on_persisted_state_model_linq_dependency() -> None:
    _lints_linq_fixture("Editor/DataVisualizer/Data/UsesLinq.cs")


def case_fails_on_search_model_linq_dependency() -> None:
    _lints_linq_fixture("Editor/DataVisualizer/Search/UsesLinq.cs")


def case_fails_on_asset_postprocessing_linq_dependency() -> None:
    _lints_linq_fixture("Editor/DataVisualizer/Unity/UsesLinq.cs")


def case_fails_on_namespace_discovery_linq_dependency() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "Editor/DataVisualizer/NamespaceController.cs",
            """namespace Fixture
{
    using System.Linq;

    public sealed class UsesLinq { }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(1, "namespace discovery LINQ dependencies should fail", exit_code)
        assert_true(
            re.search("#61", output, re.IGNORECASE) is not None,
            f"failure should identify the LINQ rule: {output}",
        )
        assert_true(
            re.search(r"Editor/DataVisualizer/NamespaceController\.cs:3", output, re.IGNORECASE) is not None,
            f"failure should identify the import: {output}",
        )
    finally:
        remove_temp_root(root)


def case_passes_other_editor_linq_dependency() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "Editor/UsesLinq.cs",
            """namespace Fixture
{
    using System.Linq;

    public sealed class UsesLinq { }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(0, f"the bounded LINQ rule should allow other Editor LINQ: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_fails_when_nested_type_is_interspersed() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "Nested.cs",
            """public sealed class NestedFixture
{
    public void First() { }
    private sealed class Nested { }
    public void Second() { }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(1, "interspersed nested type should fail", exit_code)
        assert_true(
            re.search("nested class", output, re.IGNORECASE) is not None,
            f"failure should identify the nested type: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fixes_tier_order_by_exact_permutation() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "Fixable.cs",
            """public sealed class Fixable
{
    private int _field;
    public int Value { get; set; }
    public void Run() { string marker = "body-preserved"; }
}
""",
        )
        fixed_output, fixed_exit = invoke_member_order_lint(root, ["--fix"])
        assert_exit_code(0, f"fixable fixture should be repaired: {fixed_output}", fixed_exit)
        verified_output, verified_exit = invoke_member_order_lint(root)
        assert_exit_code(0, f"repaired fixture should pass: {verified_output}", verified_exit)
        content = fixture_path(root, "Fixable.cs").read_text(encoding="utf-8")
        assert_true(content.index("Value") < content.index("_field"), "property should precede field")
        assert_true(
            re.search("body-preserved", content, re.IGNORECASE) is not None,
            "fix must preserve member bodies",
        )
    finally:
        remove_temp_root(root)


def case_fails_on_counted_array_loop_with_unused_index() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "Loops.cs",
            """using System.Collections.Generic;

public sealed class Loops
{
    public int Sum(string[] items)
    {
        int total = 0;
        for (int i = 0; i < items.Length; ++i)
        {
            total += items[i].Length;
        }

        return total;
    }

    public void Keep(IReadOnlyList<string> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            System.Console.WriteLine(list[i]);
        }
    }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(1, "counted array loop with unused index should fail", exit_code)
        assert_true(
            re.search("rule 21", output, re.IGNORECASE) is not None,
            f"failure should identify the loop rule: {output}",
        )
    finally:
        remove_temp_root(root)


def case_passes_counted_loops_when_the_index_is_needed() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(
            root,
            "KeepLoops.cs",
            """using System.Collections.Generic;

public sealed class KeepLoops
{
    public int Parallel(string[] values, int[] weights)
    {
        int total = 0;
        for (int i = 0; i < values.Length; ++i)
        {
            total += values[i].Length * weights[i];
        }

        return total;
    }

    public void WriteByIndex(string[] items)
    {
        for (int i = 0; i < items.Length; i += 1)
        {
            items[i] = items[i].Trim();
        }
    }

    public int Offset(string[] items, int start)
    {
        int total = 0;
        for (int i = start; i < items.Length; ++i)
        {
            total += items[i].Length;
        }

        return total;
    }

    public int InterfaceList(IReadOnlyList<string> list)
    {
        int total = 0;
        for (int i = 0; i < list.Count; i++)
        {
            total += list[i].Length;
        }

        return total;
    }

    public int LastElement(string[] items)
    {
        int total = 0;
        for (int i = 0; i < items.Length; ++i)
        {
            if (i == items.Length - 1)
            {
                total += 1;
            }

            total += items[i].Length;
        }

        return total;
    }
}
""",
        )
        output, exit_code = invoke_member_order_lint(root)
        assert_exit_code(0, f"index-needed and interface-typed loops must stay legal: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_paths_scans_only_the_named_files() -> None:
    root = new_temp_root(prefix="member-order-")
    try:
        write_fixture_file(root, "Ordered.cs", ORDERED_FIXTURE)
        # The scoped verdict is the whole-tree verdict restricted to the
        # named files: a clean named file passes even when the tree holds a
        # violation elsewhere, and the violation names its file and line.
        output, exit_code = invoke_member_order_lint(root, ("--paths", str(root / "Ordered.cs"), "--verbose"))
        assert_exit_code(0, f"the named ordered file must pass: {output}", exit_code)
        assert_true(
            re.search(r"1 file\(s\) clean", output) is not None,
            f"the scoped scan must cover only the named file: {output}",
        )
        write_fixture_file(
            root,
            "Unordered.cs",
            """public sealed class UnorderedFixture
{
    private int hidden;

    public int surface;
}
""",
        )
        output, exit_code = invoke_member_order_lint(root, ("--paths", str(root / "Unordered.cs")))
        assert_exit_code(1, f"the named violating file must fail: {output}", exit_code)
        assert_true(
            re.search(r"Unordered\.cs:\d+", output, re.IGNORECASE) is not None,
            f"the scoped scan must report the violation: {output}",
        )
        # A name that is not a readable C# file fails closed: the scan can
        # never quietly verify nothing.
        output, exit_code = invoke_member_order_lint(root, ("--paths", str(root / "Missing.cs")))
        assert_exit_code(2, f"a missing --paths entry must fail closed: {output}", exit_code)
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== C# member-order self-tests ==")

    run_case("Passes_CanonicalOrder", case_passes_canonical_order)
    run_case("Fails_OnTierAndAccessInversions", case_fails_on_tier_and_access_inversions)
    run_case("Fails_OnUnderscoredMethodName", case_fails_on_underscored_method_name)
    run_case("Fails_OnUnderscoredGenericMethodName", case_fails_on_underscored_generic_method_name)
    run_case("Fails_OnConsecutiveOrdinaryLineComments", case_fails_on_consecutive_ordinary_line_comments)
    run_case(
        "Fails_OnLeadingBomConsecutiveOrdinaryLineComments",
        case_fails_on_leading_bom_consecutive_ordinary_line_comments,
    )
    run_case("Passes_SupportedCommentForms", case_passes_supported_comment_forms)
    run_case("Fails_OnRuntimeLinqImportsAndQualifiedCalls", case_fails_on_runtime_linq_imports_and_qualified_calls)
    run_case("Passes_RuntimeLinqTextInCommentsAndStrings", case_passes_runtime_linq_text_in_comments_and_strings)
    run_case("Fails_OnPersistedStateModelLinqDependency", case_fails_on_persisted_state_model_linq_dependency)
    run_case("Fails_OnSearchModelLinqDependency", case_fails_on_search_model_linq_dependency)
    run_case("Fails_OnAssetPostprocessingLinqDependency", case_fails_on_asset_postprocessing_linq_dependency)
    run_case("Fails_OnNamespaceDiscoveryLinqDependency", case_fails_on_namespace_discovery_linq_dependency)
    run_case("Passes_OtherEditorLinqDependency", case_passes_other_editor_linq_dependency)
    run_case("Fails_WhenNestedTypeIsInterspersed", case_fails_when_nested_type_is_interspersed)
    run_case("FixesTierOrderByExactPermutation", case_fixes_tier_order_by_exact_permutation)
    run_case("Fails_OnCountedArrayLoopWithUnusedIndex", case_fails_on_counted_array_loop_with_unused_index)
    run_case("Passes_CountedLoopsWhenTheIndexIsNeeded", case_passes_counted_loops_when_the_index_is_needed)
    run_case("Paths_ScansOnlyTheNamedFiles", case_paths_scans_only_the_named_files)

    if failure_count() > 0:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
