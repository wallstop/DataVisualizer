#!/usr/bin/env python3
"""opencode-service-recycle contract tests (typed Python port of ``test-opencode-service-recycle.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions;
only the driver language changed.
"""

from __future__ import annotations

import re
import shutil
import sys
from pathlib import Path

sys.dont_write_bytecode = True

from _test_helpers import assert_exit_code, assert_true, failure_count, run_case, run_tool_output

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent


def case_runs_when_bash_is_available() -> None:
    bash = shutil.which("bash")
    if bash is None:
        print("    SKIP: bash not found on PATH; opencode-service-recycle tests require a POSIX shell.")
        return

    test_script = REPO_ROOT / ".devcontainer/tests/test-opencode-service-recycle.sh"
    assert_true(test_script.is_file(), f"recycle test script missing: {test_script}")

    output, exit_code = run_tool_output([bash, str(test_script)])
    print(output)
    assert_exit_code(0, "opencode-service-recycle suite should pass", exit_code)
    assert_true(
        re.search(r"opencode-service-recycle tests: \d+ passed, 0 failed", output) is not None,
        f"suite summary should report zero failures, got: {output}",
    )


def main() -> int:
    print("== opencode-service-recycle contract tests ==")

    run_case("Runs_WhenBashIsAvailable", case_runs_when_bash_is_available)

    failures = failure_count()
    print(f"== opencode-service-recycle: {failures} failure(s) ==")
    if failures > 0:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
