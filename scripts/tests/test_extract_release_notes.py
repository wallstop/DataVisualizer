#!/usr/bin/env python3
"""Release-notes extraction self-tests (typed Python port of ``test-extract-release-notes.ps1``, issue #171).

Every case from the PowerShell original is kept with the same name and
assertions; only the driver language changed. The cases drive
``scripts/release/extract-release-notes.mjs`` through node the same way.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path
from typing import Sequence, Tuple

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    failure_count,
    find_node,
    new_temp_root,
    remove_temp_root,
    run_case,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
EXTRACT_SCRIPT = SCRIPTS_DIR / "release" / "extract-release-notes.mjs"

VALID_CHANGELOG = (
    "# Changelog\n"
    "\n"
    "## [Unreleased]\n"
    "\n"
    "### Added\n"
    "\n"
    "- Pending work.\n"
    "\n"
    "## [0.0.38] - 2026-10-02\n"
    "\n"
    "### Added\n"
    "\n"
    "- The released feature.\n"
    "\n"
    "### Fixed\n"
    "\n"
    "- A repair.\n"
    "\n"
    "## [0.0.37] - 2026-09-10\n"
    "\n"
    "### Changed\n"
    "\n"
    "- Older entry."
)


def new_notes_fixture(changelog: str) -> Path:
    root = new_temp_root(prefix="notes-fixture-")
    write_fixture_file(root, "CHANGELOG.md", changelog)
    return root


def invoke_extract(node: str, root: Path, arguments: Sequence[str]) -> Tuple[str, int]:
    return run_tool_output([node, str(EXTRACT_SCRIPT), "--root", str(root), *arguments])


def case_extracts_exact_version_body(node: str) -> None:
    root = new_notes_fixture(VALID_CHANGELOG)
    try:
        output, exit_code = invoke_extract(node, root, ["--version", "0.0.38"])
        assert_exit_code(0, "a present version section should extract", exit_code)
        assert_true("The released feature." in output, f"should include the added bullet, got: {output}")
        assert_true("A repair." in output, f"should include the fixed bullet, got: {output}")
        assert_true("Older entry." not in output, f"should stop at the next section, got: {output}")
        assert_true("Unreleased" not in output, f"should not bleed into Unreleased, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_closed_on_missing_section(node: str) -> None:
    root = new_notes_fixture(VALID_CHANGELOG)
    try:
        output, exit_code = invoke_extract(node, root, ["--version", "9.9.9"])
        assert_exit_code(1, "a missing section should fail", exit_code)
        assert_true("no '## [9.9.9]' section" in output, f"should name the missing section, got: {output}")
    finally:
        remove_temp_root(root)


def case_fails_closed_on_duplicate_section(node: str) -> None:
    root = new_notes_fixture(VALID_CHANGELOG + "\n## [0.0.38] - 2026-10-03\n\nDuplicate.\n")
    try:
        output, exit_code = invoke_extract(node, root, ["--version", "0.0.38"])
        assert_exit_code(1, "a duplicated section should fail", exit_code)
        assert_true(
            re.search(r"2 .## \[0\.0\.38\]. sections", output) is not None,
            f"should report the duplicate count, got: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_closed_on_empty_section(node: str) -> None:
    root = new_notes_fixture("## [0.0.38] - 2026-10-02\n\n## [0.0.37] - 2026-09-10\n")
    try:
        output, exit_code = invoke_extract(node, root, ["--version", "0.0.38"])
        assert_exit_code(1, "an empty section should fail", exit_code)
        assert_true("is empty" in output, f"should report the empty section, got: {output}")
    finally:
        remove_temp_root(root)


def case_rejects_leading_v(node: str) -> None:
    root = new_notes_fixture(VALID_CHANGELOG)
    try:
        output, exit_code = invoke_extract(node, root, ["--version", "v0.0.38"])
        assert_exit_code(1, "a v-prefixed version should fail", exit_code)
        assert_true("bare package version" in output, f"should explain the expected shape, got: {output}")
    finally:
        remove_temp_root(root)


def case_writes_notes_to_output_file(node: str) -> None:
    root = new_notes_fixture(VALID_CHANGELOG)
    try:
        _, exit_code = invoke_extract(node, root, ["--version", "0.0.38", "--output", "release-notes.md"])
        assert_exit_code(0, "an output path should write successfully", exit_code)
        notes_path = root / "release-notes.md"
        assert_true(notes_path.exists(), "the notes file should exist")
        content = notes_path.read_text(encoding="utf-8")
        assert_true("The released feature." in content, f"the notes file should carry the body, got: {content}")
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== extract-release-notes self-tests ==")

    node = find_node()

    run_case("Extracts_ExactVersionBody", lambda: case_extracts_exact_version_body(node))
    run_case("FailsClosed_OnMissingSection", lambda: case_fails_closed_on_missing_section(node))
    run_case("FailsClosed_OnDuplicateSection", lambda: case_fails_closed_on_duplicate_section(node))
    run_case("FailsClosed_OnEmptySection", lambda: case_fails_closed_on_empty_section(node))
    run_case("Rejects_LeadingV", lambda: case_rejects_leading_v(node))
    run_case("WritesNotesToOutputFile", lambda: case_writes_notes_to_output_file(node))
    return failure_count()


if __name__ == "__main__":
    sys.exit(main())
