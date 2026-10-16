#!/usr/bin/env python3
"""C# source policy composite runner self-tests (issue #171).

Covers the pre-commit composite that runs the member-order, null-assertion,
and using-placement lints in one interpreter: verdict aggregation, per-lint
attribution, the fail-closed ``--paths`` validation, and the ``--fix``
refusal. The individual lints keep their own suites; these cases only pin
the composite's contract.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path
from typing import Dict, Optional, Sequence, Tuple

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
COMPOSITE_SCRIPT = SCRIPTS_DIR / "lint_csharp_source_policy.py"

ROOT_ENV_VARS = (
    "CSHARP_MEMBER_ORDER_ROOTS",
    "CSHARP_NULL_ASSERTION_ROOTS",
    "CSHARP_USINGS_ROOTS",
)

CLEAN_FIXTURE = """namespace Demo
{
    internal sealed class Compliant
    {
        public int Count { get; private set; }

        private readonly int count;
    }
}
"""

MISPLACED_USINGS_FIXTURE = """using System;

namespace Demo
{
    internal sealed class Misplaced
    {
        private readonly int count;
    }
}
"""

BANNED_ASSERTION_FIXTURE = """namespace Demo
{
    internal sealed class Checker
    {
        private void Check(object value)
        {
            if (Assert.IsNotNull(value))
            {
                return;
            }
        }
    }
}
"""

MEMBER_ORDER_FIXTURE = """namespace Demo
{
    internal sealed class Disordered
    {
        private int count;

        public int Value { get; set; }
    }
}
"""


def invoke_composite(root: Path, arguments: Sequence[str] = ()) -> Tuple[str, int]:
    """Run the composite with every lint root pointed at the fixture root."""
    previous: Dict[str, Optional[str]] = {}
    for env_var in ROOT_ENV_VARS:
        previous[env_var] = os.environ.get(env_var)
        os.environ[env_var] = str(root)
    try:
        return run_tool_output([find_python(), str(COMPOSITE_SCRIPT), *arguments])
    finally:
        for env_var in ROOT_ENV_VARS:
            if previous[env_var] is None:
                os.environ.pop(env_var, None)
            else:
                os.environ[env_var] = previous[env_var] or ""


def main() -> int:
    print("== C# source policy composite runner tests ==")

    clean_root = new_temp_root("csharp-source-policy-clean-")
    try:
        write_fixture_file(clean_root, "Compliant.cs", CLEAN_FIXTURE)

        def clean_fixture_passes_every_lint() -> None:
            output, exit_code = invoke_composite(clean_root)
            assert_exit_code(0, "a clean fixture should pass the composite", exit_code)
            assert_true(
                "[csharp-source-policy] all 3 lints clean" in output,
                f"the summary should report every lint clean, got: {output}",
            )
            for label in ("member-order", "null-assertions", "usings"):
                assert_true(
                    f"[csharp-source-policy] {label}: ok" in output,
                    f"the {label} result should be reported, got: {output}",
                )

        run_case("clean fixture passes every lint", clean_fixture_passes_every_lint)
    finally:
        remove_temp_root(clean_root)

    usings_root = new_temp_root("csharp-source-policy-usings-")
    try:
        write_fixture_file(usings_root, "Misplaced.cs", MISPLACED_USINGS_FIXTURE)

        def misplaced_usings_fail_with_attribution() -> None:
            output, exit_code = invoke_composite(usings_root)
            assert_exit_code(1, "misplaced usings should fail the composite", exit_code)
            assert_true(
                "[csharp-source-policy] usings: FAIL" in output,
                f"the usings lint should be reported as failed, got: {output}",
            )
            assert_true(
                "[csharp-source-policy] member-order: ok" in output
                and "[csharp-source-policy] null-assertions: ok" in output,
                f"the passing lints should still be reported, got: {output}",
            )
            assert_true(
                "misplaced using(s)" in output,
                f"the usings diagnostics should reach the hook output, got: {output}",
            )

        run_case("misplaced usings fail with attribution", misplaced_usings_fail_with_attribution)
    finally:
        remove_temp_root(usings_root)

    assertion_root = new_temp_root("csharp-source-policy-assertions-")
    try:
        write_fixture_file(assertion_root, "Checker.cs", BANNED_ASSERTION_FIXTURE)

        def banned_assertion_fails_with_attribution() -> None:
            output, exit_code = invoke_composite(assertion_root)
            assert_exit_code(1, "a banned null assertion should fail the composite", exit_code)
            assert_true(
                "[csharp-source-policy] null-assertions: FAIL" in output,
                f"the null-assertions lint should be reported as failed, got: {output}",
            )
            assert_true(
                "banned assertion(s)" in output,
                f"the null-assertion diagnostics should reach the hook output, got: {output}",
            )

        run_case("banned null assertion fails with attribution", banned_assertion_fails_with_attribution)
    finally:
        remove_temp_root(assertion_root)

    order_root = new_temp_root("csharp-source-policy-order-")
    try:
        write_fixture_file(order_root, "Disordered.cs", MEMBER_ORDER_FIXTURE)

        def member_order_violation_fails_with_attribution() -> None:
            output, exit_code = invoke_composite(order_root)
            assert_exit_code(1, "a member-order violation should fail the composite", exit_code)
            assert_true(
                "[csharp-source-policy] member-order: FAIL" in output,
                f"the member-order lint should be reported as failed, got: {output}",
            )

        run_case("member-order violation fails with attribution", member_order_violation_fails_with_attribution)
    finally:
        remove_temp_root(order_root)

    combined_root = new_temp_root("csharp-source-policy-combined-")
    try:
        combined_fixture = MISPLACED_USINGS_FIXTURE.replace(
            "        private readonly int count;",
            "        private void Check(object value)\n        {\n            if (Assert.IsNull(value))\n            {\n                return;\n            }\n        }",
        )
        write_fixture_file(combined_root, "Combined.cs", combined_fixture)

        def several_failures_are_all_reported() -> None:
            output, exit_code = invoke_composite(combined_root)
            assert_exit_code(1, "two violating lints should fail the composite", exit_code)
            assert_true(
                "[csharp-source-policy] null-assertions: FAIL" in output
                and "[csharp-source-policy] usings: FAIL" in output,
                f"both failing lints should be reported, got: {output}",
            )
            assert_true(
                "2 of 3 lint(s) failed (null-assertions, usings)" in output,
                f"the summary should name every failing lint, got: {output}",
            )

        run_case("several failures are all reported", several_failures_are_all_reported)
    finally:
        remove_temp_root(combined_root)

    closed_root = new_temp_root("csharp-source-policy-closed-")
    try:
        write_fixture_file(closed_root, "Notes.txt", "not C#")

        def non_csharp_path_fails_closed() -> None:
            output, exit_code = invoke_composite(closed_root, ("--paths", str(closed_root / "Notes.txt")))
            assert_exit_code(2, "a non-C# --paths entry should fail closed", exit_code)
            assert_true(
                "not a readable C# file" in output,
                f"the fail-closed message should be printed, got: {output}",
            )

        run_case("non-C# --paths entry fails closed", non_csharp_path_fails_closed)

        def fix_flag_is_refused() -> None:
            output, exit_code = invoke_composite(closed_root, ("--fix",))
            assert_exit_code(2, "--fix should be refused by the composite", exit_code)
            assert_true(
                "--fix is not supported" in output,
                f"the refusal message should be printed, got: {output}",
            )

        run_case("--fix is refused by the composite", fix_flag_is_refused)
    finally:
        remove_temp_root(closed_root)

    failures = failure_count()
    if failures > 0:
        print(f"== csharp-source-policy: {failures} failure(s) ==")
        return 1
    print("== csharp-source-policy: all cases passed ==")
    return 0


if __name__ == "__main__":
    sys.exit(main())
