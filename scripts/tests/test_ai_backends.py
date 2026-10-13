#!/usr/bin/env python3
"""ai-backends launcher contract tests (typed Python port of ``test-ai-backends.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions;
only the driver language changed.
"""

from __future__ import annotations

import os
import re
import shutil
import sys
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True

from _test_helpers import assert_exit_code, assert_true, failure_count, run_case, run_tool_output

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent


def case_runs_when_bash_is_available() -> None:
    bash = shutil.which("bash")
    if bash is None:
        print("    SKIP: bash not found on PATH; ai-backends contract tests require a POSIX shell.")
        return

    test_script = REPO_ROOT / ".devcontainer/tests/test-ai-backends.sh"
    assert_true(test_script.is_file(), f"launcher test script missing: {test_script}")

    # The shell fixture deliberately strips PATH to system directories. Pass
    # this validated interpreter explicitly, including on Windows runners.
    with patch.dict(os.environ, {"AI_BACKENDS_TEST_PYTHON": Path(sys.executable).as_posix()}):
        output, exit_code = run_tool_output([bash, str(test_script)])
    print(output)
    assert_exit_code(0, "ai-backends contract suite should pass", exit_code)
    assert_true(
        re.search(r"ai-backends contract tests: \d+ passed, 0 failed", output) is not None,
        f"suite summary should report zero failures, got: {output}",
    )


def main() -> int:
    print("== ai-backends launcher contract tests ==")

    run_case("Runs_When_BashIsAvailable", case_runs_when_bash_is_available)

    failures = failure_count()
    print(f"== ai-backends: {failures} failure(s) ==")
    if failures > 0:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
