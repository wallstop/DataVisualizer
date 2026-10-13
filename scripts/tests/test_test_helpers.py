#!/usr/bin/env python3
"""Interpreter identity and failure contracts for the shared test helper."""
from __future__ import annotations

import sys
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


if __name__ == "__main__":
    unittest.main()
