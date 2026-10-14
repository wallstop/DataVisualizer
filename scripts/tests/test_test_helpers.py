#!/usr/bin/env python3
"""Interpreter identity and failure contracts for the shared test helper."""
from __future__ import annotations

import contextlib
import io
import sys
import threading
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
import _test_helpers as helper


class InterpreterTests(unittest.TestCase):
    def test_active_interpreter_is_reused_without_path_probes(self) -> None:
        with patch.object(helper.shutil, "which", side_effect=AssertionError("unexpected PATH lookup")), patch.object(helper.subprocess, "run", side_effect=AssertionError("unexpected probe process")):
            self.assertEqual(helper.find_python(), sys.executable)

    def test_missing_active_executable_fails_explicitly(self) -> None:
        with patch.object(helper.sys, "executable", ""):
            with self.assertRaisesRegex(RuntimeError, "executable Python interpreter"):
                helper.find_python()


class RunCasesTests(unittest.TestCase):
    def test_cases_run_concurrently_and_report_in_submitted_order(self) -> None:
        sibling_started = threading.Event()

        def blocking_case() -> None:
            # Proves concurrency: the sibling must start while this case
            # still blocks, or the wait times out and the case fails.
            self.assertTrue(sibling_started.wait(timeout=10), "the sibling case never started")

        def sibling_case() -> None:
            sibling_started.set()

        captured = io.StringIO()
        with contextlib.redirect_stdout(captured):
            failed = helper.run_cases(
                (
                    ("Blocks_Until_Sibling_Starts", blocking_case),
                    ("Starts_While_Sibling_Blocks", sibling_case),
                )
            )
        self.assertEqual(failed, 0)
        self.assertEqual(
            captured.getvalue().splitlines(),
            ["  PASS: Blocks_Until_Sibling_Starts", "  PASS: Starts_While_Sibling_Blocks"],
        )

    def test_failure_is_counted_and_replayed_in_submitted_order(self) -> None:
        def failing() -> None:
            raise AssertionError("boom")

        captured = io.StringIO()
        before = helper.failure_count()
        with contextlib.redirect_stdout(captured):
            failed = helper.run_cases((("Passes_Around_A_Failure", lambda: None), ("Fails_With_Message", failing)))
        self.assertEqual(failed, 1)
        self.assertEqual(helper.failure_count(), before + 1)
        self.assertEqual(captured.getvalue().splitlines()[1], "  FAIL: Fails_With_Message :: boom")

    def test_single_case_runs_without_a_pool(self) -> None:
        captured = io.StringIO()
        with contextlib.redirect_stdout(captured):
            self.assertEqual(helper.run_cases((("Only_Case", lambda: None),)), 0)
        self.assertEqual(captured.getvalue().splitlines(), ["  PASS: Only_Case"])

    def test_escaping_system_exit_replays_settled_cases_before_leaving(self) -> None:
        def killer() -> None:
            raise SystemExit(3)

        captured = io.StringIO()
        before = helper.failure_count()
        with self.assertRaises(SystemExit) as raised:
            with contextlib.redirect_stdout(captured):
                helper.run_cases((("Settles_First", lambda: None), ("Exits_The_Child", killer)))
        self.assertEqual(raised.exception.code, 3)
        self.assertEqual(helper.failure_count(), before)
        self.assertEqual(captured.getvalue().splitlines(), ["  PASS: Settles_First"])


if __name__ == "__main__":
    unittest.main()
