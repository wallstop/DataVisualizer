#!/usr/bin/env python3
"""Markdown check scope self-tests.

The Markdown check script must keep its contract: ``all`` (or nothing)
fails safe to the full sweep, the comma-joined token selects exactly the
named paths, and a missing npm launcher fails closed. The command-building
cases run in-process; the two end-to-end cases drive real Prettier through
the local npm install, so a formatting regression cannot hide behind a
rewritten command.
"""

from __future__ import annotations

import subprocess
import sys
import unittest
from pathlib import Path

sys.dont_write_bytecode = True

from _test_helpers import assert_exit_code, new_temp_root, remove_temp_root

TESTS_DIR = Path(__file__).resolve().parent
SCRIPT_PATH = TESTS_DIR.parent / "markdown_check.py"
FULL_SWEEP = ["npm", "run", "format:md:check"]


sys.path.insert(0, str(SCRIPT_PATH.parent))
from markdown_check import build_command  # noqa: E402


def invoke(arguments: list[str]) -> list[str]:
    """Build the command in-process and return it (or the raised SystemExit)."""
    return build_command(arguments)


class MarkdownCheckScopeTests(unittest.TestCase):
    def test_all_and_missing_scopes_fail_safe_to_the_full_sweep(self) -> None:
        self.assertEqual(invoke(["all"]), FULL_SWEEP)
        self.assertEqual(invoke([]), FULL_SWEEP)
        self.assertEqual(invoke([""]), FULL_SWEEP)
        self.assertEqual(invoke(["all", "README.md"]), FULL_SWEEP)

    def test_the_comma_joined_token_selects_exactly_its_paths(self) -> None:
        self.assertEqual(
            invoke(["README.md,docs/guide.md"]),
            ["npx", "--no-install", "prettier", "--check", "README.md", "docs/guide.md"],
        )

    def test_multiple_tokens_are_rejected(self) -> None:
        with self.assertRaises(SystemExit):
            invoke(["README.md", "docs/guide.md"])

    def test_a_missing_launcher_fails_closed(self) -> None:
        from markdown_check import resolve_executable

        self.assertIsNone(resolve_executable("definitely-not-an-npm-launcher"))

    def test_a_formatted_file_passes_through_real_prettier(self) -> None:
        repo = new_temp_root(prefix="markdown-check-")
        try:
            target = repo / "formatted.md"
            target.write_text("# Title\n\nA short paragraph.\n", encoding="utf-8")
            completed = subprocess.run(
                [sys.executable, str(SCRIPT_PATH), str(target)],
                capture_output=True, text=True, encoding="utf-8", errors="replace", check=False,
            )
            assert_exit_code(0, f"formatted file must pass: {completed.stdout}{completed.stderr}", completed.returncode)
        finally:
            remove_temp_root(repo)

    def test_an_unformatted_file_fails_through_real_prettier(self) -> None:
        repo = new_temp_root(prefix="markdown-check-")
        try:
            target = repo / "unformatted.md"
            target.write_text("# Title\nText without a blank separator.\n", encoding="utf-8")
            completed = subprocess.run(
                [sys.executable, str(SCRIPT_PATH), str(target)],
                capture_output=True, text=True, encoding="utf-8", errors="replace", check=False,
            )
            self.assertNotEqual(completed.returncode, 0, "an unformatted file must fail the check")
        finally:
            remove_temp_root(repo)


if __name__ == "__main__":
    unittest.main()
