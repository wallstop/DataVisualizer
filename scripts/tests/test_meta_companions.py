#!/usr/bin/env python3
"""meta-companion lint contract tests (issue #274 review follow-up).

The lint must fail exactly when Unity would generate a companion on the host:
a tracked or untracked non-ignored non-dot file without its ``.meta``, or a
new non-dot folder without its folder companion. Everything Unity never
imports (dot paths, ``.meta`` files, ``__pycache__``) must pass silently.
Every case drives the real lint as a subprocess against its own throwaway
git repository, mirroring how the scan reads tracked plus untracked state.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from typing import Sequence

sys.dont_write_bytecode = True

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from _test_helpers import assert_exit_code, assert_true, new_temp_root, remove_temp_root, run_cases
from lint_meta_companions import missing_companions

TESTS_DIR = Path(__file__).resolve().parent
LINT = TESTS_DIR.parent / "lint_meta_companions.py"


def assert_contains(description: str, needle: str, haystack: str) -> None:
    assert_true(needle in haystack, f"{description} (missing: [{needle}])")


class LintSandbox:
    """A throwaway git repository the lint scans through --repo-root."""

    def __init__(self) -> None:
        self.root = new_temp_root(prefix="meta-companion-test-")
        self._run_git(["init", "-q"])
        self._run_git(["config", "user.email", "agent@example.com"])
        self._run_git(["config", "user.name", "Agent"])
        self.last_exit = -1
        self.output = ""

    def write(self, relative: str, content: str = "x") -> None:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def commit_all(self) -> None:
        self._run_git(["add", "-A"])
        self._run_git(["commit", "-q", "-m", "base"])

    def run(self) -> None:
        """Run the real lint against the sandbox and capture its verdict."""
        completed = subprocess.run(
            [sys.executable, str(LINT), "--repo-root", str(self.root)],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=120,
            check=False,
        )
        self.last_exit = completed.returncode
        self.output = (completed.stdout or "") + (completed.stderr or "")

    def destroy(self) -> None:
        remove_temp_root(self.root)

    def _run_git(self, arguments: Sequence[str]) -> None:
        completed = subprocess.run(
            ["git", "-C", str(self.root), *arguments],
            capture_output=True,
            text=True,
            check=False,
        )
        assert_exit_code(0, f"git {' '.join(arguments)} failed: {completed.stderr}", completed.returncode)


def seed_baseline(sandbox: LintSandbox) -> None:
    """A fully companioned tree: every file, file meta, and folder meta present."""
    sandbox.write("Scripts/Keep.cs")
    sandbox.write("Scripts/Keep.cs.meta", "guid: a\n")
    sandbox.write("Scripts.meta", "guid: b\n")
    sandbox.commit_all()


def case_committed_tree_with_companions_passes() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write("Scripts/Sub.meta", "guid: c\n")
        sandbox.write("Scripts/Sub/Deep.cs", "")
        sandbox.write("Scripts/Sub/Deep.cs.meta", "guid: d\n")
        seed_baseline(sandbox)
        sandbox.run()
        assert_exit_code(0, "a fully companioned tree must pass", sandbox.last_exit)
        assert_contains("the pass line reports the scan result", "[meta-companions] ok", sandbox.output)
    finally:
        sandbox.destroy()


def case_committed_file_without_companion_fails() -> None:
    sandbox = LintSandbox()
    try:
        seed_baseline(sandbox)
        sandbox.run()
        assert_exit_code(0, "the committed baseline passes", sandbox.last_exit)

        sandbox.write("Scripts/Leaked.cs")  # committed without its companion
        sandbox.commit_all()
        sandbox.run()
        assert_true(sandbox.last_exit == 1, "a file without its companion must fail")
        assert_contains("the failure names the offending path", "Scripts/Leaked.cs", sandbox.output)
        assert_true("Scripts/Keep.cs " not in sandbox.output, "companioned paths must not be reported")
    finally:
        sandbox.destroy()


def case_dot_folder_paths_are_exempt() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write(".llm/mcp/server.py", "print('x')\n")
        sandbox.commit_all()
        sandbox.run()
        assert_exit_code(0, "dot-folder content never needs a companion", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_untracked_non_ignored_file_fails() -> None:
    sandbox = LintSandbox()
    try:
        seed_baseline(sandbox)
        sandbox.write("Scratch.py", "print('x')\n")
        sandbox.run()
        assert_true(sandbox.last_exit == 1, "an untracked non-ignored file without a companion must fail")
        assert_contains("the failure names the untracked path", "Scratch.py", sandbox.output)
    finally:
        sandbox.destroy()


def case_ignored_file_is_exempt() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write(".gitignore", "Ignored.log\n")
        seed_baseline(sandbox)
        sandbox.write("Ignored.log", "scratch\n")
        sandbox.run()
        assert_exit_code(0, "ignored scratch files never need companions", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_new_folder_without_folder_companion_fails() -> None:
    sandbox = LintSandbox()
    try:
        seed_baseline(sandbox)
        # The file carries its companion but the new parent folder does not:
        # the ancestor scan must still catch the folder companion.
        sandbox.write("Scripts/NewDir/File.cs")
        sandbox.write("Scripts/NewDir/File.cs.meta", "guid: c\n")
        sandbox.commit_all()
        sandbox.run()
        assert_true(sandbox.last_exit == 1, "a new folder without its companion must fail")
        assert_contains("the failure names the folder companion", "Scripts/NewDir.meta", sandbox.output)
        assert_true("Scripts/NewDir/File.cs" not in sandbox.output, "the companioned file itself must not be reported")
    finally:
        sandbox.destroy()


def case_pycache_and_meta_files_are_exempt() -> None:
    sandbox = LintSandbox()
    try:
        seed_baseline(sandbox)
        sandbox.write("Scripts/__pycache__/keep.pyc", "bytes\n")
        sandbox.write("Scripts/__pycache__/keep.pyc.meta", "guid: c\n")
        # .meta files never need companions of their own, but an unpaired one
        # is an orphan the lint must still flag; pair this one with its asset.
        sandbox.write("Scripts/Loose.cs")
        sandbox.write("Scripts/Loose.cs.meta", "guid: d\n")
        sandbox.commit_all()
        sandbox.run()
        assert_exit_code(0, "__pycache__ content and paired .meta files are exempt", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_orphan_companion_fails() -> None:
    sandbox = LintSandbox()
    try:
        seed_baseline(sandbox)
        # An orphan companion: committed with no asset beside it. Unity
        # treats the asset as missing and deletes or regenerates the meta.
        sandbox.write("Scripts/Orphan.cs.meta", "guid: e\n")
        sandbox.commit_all()
        sandbox.run()
        assert_true(sandbox.last_exit == 1, "an orphan companion must fail")
        assert_contains("the failure names the orphan", "Scripts/Orphan.cs.meta", sandbox.output)
        assert_contains("the failure names the orphan class", "orphan", sandbox.output)
    finally:
        sandbox.destroy()


def case_folder_companion_for_ignored_only_folder_is_tolerated() -> None:
    sandbox = LintSandbox()
    try:
        seed_baseline(sandbox)
        # A committed folder meta whose folder holds only ignored content is
        # the tracked __pycache__ shape: the folder exists on disk, so the
        # companion is not an orphan.
        sandbox.write(".gitignore", "Cache/\n")
        sandbox.write("Scripts/Cache.meta", "guid: f\n")
        sandbox.commit_all()
        sandbox.write("Scripts/Cache/blob.bin", "bytes\n")
        sandbox.run()
        assert_exit_code(0, "a folder meta over ignored-only content is tolerated", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_missing_companions_reports_sorted_unique_paths() -> None:
    root = new_temp_root(prefix="meta-companion-pure-")
    try:
        # The A and B folder metas are only tolerated when their folders
        # exist (the __pycache__ shape); create them so the case isolates
        # the one genuinely missing companion.
        (root / "A").mkdir()
        (root / "B").mkdir()
        missing = missing_companions(["B/A.cs", "B.meta", "A/A.cs", "A/A.cs.meta", "A.meta", "A.cs", "A.cs.meta"], root)
        assert_true(missing == ["B/A.cs"], f"only B/A.cs lacks a companion, got: {missing}")
        missing = missing_companions(["X/Y/Z.cs"], root)
        assert_true(missing == ["X", "X/Y", "X/Y/Z.cs"], f"every ancestor companion is reported, got: {missing}")
        missing = missing_companions(["Gone.cs.meta", "Present.cs"], root)
        assert_true(
            missing == ["Gone.cs.meta", "Present.cs"],
            f"an orphan meta and a missing companion are both reported, got: {missing}",
        )
    finally:
        remove_temp_root(root)


CASES = (
    case_committed_tree_with_companions_passes,
    case_committed_file_without_companion_fails,
    case_dot_folder_paths_are_exempt,
    case_untracked_non_ignored_file_fails,
    case_ignored_file_is_exempt,
    case_new_folder_without_folder_companion_fails,
    case_pycache_and_meta_files_are_exempt,
    case_orphan_companion_fails,
    case_folder_companion_for_ignored_only_folder_is_tolerated,
    case_missing_companions_reports_sorted_unique_paths,
)


def main() -> int:
    assert_true(LINT.is_file(), f"lint not found at {LINT}")
    print("== meta-companion lint contract tests ==")
    failures = run_cases([(case.__name__, case) for case in CASES])
    print(f"== meta-companions lint: {failures} failure(s) ==")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
