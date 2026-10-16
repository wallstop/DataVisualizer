#!/usr/bin/env python3
"""Markdown check scope self-tests.

The Markdown check script must keep its contract: ``all`` (or nothing)
fails safe to the full sweep, the comma-joined token selects exactly the
named paths, and a missing npm launcher fails closed. The command-building
cases run in-process; the integration cases drive real Prettier and skip
with a notice where no local npm install provides it (CI skips the npm
steps when Markdown is untouched).
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import unittest
from pathlib import Path

sys.dont_write_bytecode = True

from _test_helpers import assert_exit_code, new_temp_root, remove_temp_root

TESTS_DIR = Path(__file__).resolve().parent
SCRIPT_PATH = TESTS_DIR.parent / "markdown_check.py"
REPO_ROOT = TESTS_DIR.parent.parent
FULL_SWEEP = ["npm", "run", "format:md:check"]


def prettier_available() -> bool:
    """Return whether the local npm install provides Prettier."""
    if shutil.which("npx") is None:
        return False
    return any((REPO_ROOT / "node_modules" / ".bin").glob("prettier*"))


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

    def test_an_empty_path_list_fails_safe_to_the_full_sweep(self) -> None:
        self.assertEqual(invoke([",,"]), FULL_SWEEP)

    def test_a_missing_launcher_fails_closed(self) -> None:
        completed = subprocess.run(
            [sys.executable, str(SCRIPT_PATH), "README.md"],
            env={**os.environ, "PATH": ""},
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        self.assertEqual(completed.returncode, 2, "a missing launcher must fail closed with exit two")
        self.assertIn("is not on PATH", completed.stderr)

    def test_a_formatted_file_passes_through_real_prettier(self) -> None:
        if not prettier_available():
            print("    SKIP: prettier is required to drive the check.")
            return
        repo = new_temp_root(prefix="markdown-check-")
        try:
            target = repo / "formatted.md"
            # Bytes, not text mode: Windows text-mode writes translate \n to
            # \r\n, and prettier's default end-of-line is lf, so the fixture
            # would fail its own formatting check only on Windows.
            target.write_bytes(b"# Title\n\nA short paragraph.\n")
            completed = subprocess.run(
                [sys.executable, str(SCRIPT_PATH), str(target)],
                capture_output=True, text=True, encoding="utf-8", errors="replace", check=False,
            )
            assert_exit_code(0, f"formatted file must pass: {completed.stdout}{completed.stderr}", completed.returncode)
        finally:
            remove_temp_root(repo)

    def test_an_unformatted_file_fails_through_real_prettier(self) -> None:
        if not prettier_available():
            print("    SKIP: prettier is required to drive the check.")
            return
        repo = new_temp_root(prefix="markdown-check-")
        try:
            target = repo / "unformatted.md"
            # Byte-explicit for the same Windows reason as the formatted case:
            # the failure must come from the missing blank line, not from
            # platform newline translation.
            target.write_bytes(b"# Title\nText without a blank separator.\n")
            completed = subprocess.run(
                [sys.executable, str(SCRIPT_PATH), str(target)],
                capture_output=True, text=True, encoding="utf-8", errors="replace", check=False,
            )
            self.assertNotEqual(completed.returncode, 0, "an unformatted file must fail the check")
        finally:
            remove_temp_root(repo)


if __name__ == "__main__":
    unittest.main()
