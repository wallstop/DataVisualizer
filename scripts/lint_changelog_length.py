#!/usr/bin/env python3
"""Changelog entry length: every entry in the CHANGELOG.md '[Unreleased]'
section stays within 300 rendered characters.

Typed Python port of ``lint-changelog-length.js`` (issue #171): the same
rendered-length rules, fail-closed section checks, and exit codes, driven
by the standard library.

The entry style (one or two sentences, at most 300 rendered characters,
issue references and link targets excluded) lives in the agent guidance,
but nothing checked it, so the cap was remembered rather than enforced.
This lint makes the cap a gate. Released sections are immutable history
and stay unmeasured, so the check reads '[Unreleased]' only.

The measured length is the rendered text: soft-wrapped lines join into one
line, emphasis and inline-code markers ('**', '*', '`') strip away, and an
inline link '[text](target)' keeps only its text. Issue references are
excluded entirely, both the bare '#123' and the parenthesized '(#123)' the
changelog appends, together with the space that separates them from the
sentence. Only these shapes are excluded; any other link syntax, such as a
bare URL in prose, counts, so an unknown shape can only over-count, never
under-count, and the cap fails closed.

The check also fails closed when the '[Unreleased]' section is missing or
duplicated: a changelog the check cannot locate has no measured state, and
a renamed heading would otherwise silence the cap instead of tripping it.
A section without entries passes; that is the normal shape right after a
release rotation.

'--verbose' prints the rendered length of every entry when clean. Exit
codes: 0 = within the cap, 1 = at least one over-cap entry, a missing or
duplicated section, or an unreadable input.

'CHANGELOG_LENGTH_CHANGELOG' overrides the input so the self-test can point
at a fixture file. Nothing in CI sets it.
"""

from __future__ import annotations

import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional

REPO_ROOT = Path(__file__).resolve().parent.parent

CAP = 300

# The heading prepare_release.py rotates and Keep a Changelog defines. The
# exact match keeps a renamed or typo'd heading a failure instead of a
# vacuous pass.
UNRELEASED_HEADING = re.compile(r"^## \[Unreleased\]\s*$")
SECTION_HEADING = re.compile(r"^## ")
SUBSECTION_HEADING = re.compile(r"^### ")
# Every Markdown list marker: an entry the lint did not recognize would sit
# outside the cap instead of failing it, so all three unordered markers and
# both ordered forms count as entries.
BULLET = re.compile(r"^(?:[-*+]|\d+[.)]) ")

# An inline link renders as its text; the target does not count.
INLINE_LINK = re.compile(r"\[([^[\]]*)\]\([^()]*\)")
# A parenthesized issue reference also drops the space that separates it
# from the sentence, so '... the package style (#148).' counts as rendered.
PARENTHESIZED_ISSUE_REFERENCE = re.compile(r"\s*\(#\d+\)")
# A bare issue reference drops with its separating space; the trailing word
# boundary keeps '#123abc' whole, so unknown shapes over-count, not under.
BARE_ISSUE_REFERENCE = re.compile(r"\s*#\d+\b")
# Emphasis and inline-code markers delimit rendered text and do not count.
INLINE_MARKER = re.compile(r"[*`]")


def read_file_or_throw(file_path: Path, purpose: str) -> str:
    """Read a file, failing closed with the shared message shape."""
    try:
        return file_path.read_text(encoding="utf-8")
    except OSError:
        raise ValueError(f"Cannot {purpose}: '{file_path}' is missing or unreadable.") from None


def display_path(file_path: Path) -> str:
    """Show a repo-relative path when inside the repository, else the absolute path."""
    try:
        relative = os.path.relpath(file_path, REPO_ROOT).replace(os.sep, "/")
    except ValueError:
        # A different drive on Windows cannot be relativized; show the
        # absolute path the way the original did.
        return str(file_path)
    return str(file_path) if relative.startswith("../") else relative


@dataclass(frozen=True)
class Entry:
    """One changelog entry: its naming subsection and its wrapped lines."""

    subsection: str
    lines: List[str]


@dataclass(frozen=True)
class MeasuredEntry:
    """One entry with its rendered text, rendered length, and 1-based number."""

    entry: Entry
    rendered: str
    length: int
    number: int = 0  # assigned after collection, below


def collect_measured_entries(section_lines: List[str]) -> List[MeasuredEntry]:
    """Measure every entry and assign its 1-based position."""
    measured: List[MeasuredEntry] = []
    for entry in extract_entries(section_lines):
        rendered = render_entry(entry)
        measured.append(MeasuredEntry(entry=entry, rendered=rendered, length=len(rendered), number=len(measured) + 1))
    return measured


def extract_unreleased(lines: List[str], source: Path) -> List[str]:
    """Return the '[Unreleased]' body, failing closed on a missing or duplicated section."""
    heading_indexes = [index for index, line in enumerate(lines) if UNRELEASED_HEADING.search(line)]
    if len(heading_indexes) == 0:
        raise ValueError(
            f"'{display_path(source)}' has no '## [Unreleased]' section, so the entry cap has "
            "nothing to measure."
        )
    if len(heading_indexes) > 1:
        raise ValueError(f"'{display_path(source)}' carries {len(heading_indexes)} '## [Unreleased]' sections.")
    start = heading_indexes[0] + 1
    end = len(lines)
    for index in range(start, len(lines)):
        if SECTION_HEADING.search(lines[index]):
            end = index
            break
    return lines[start:end]


def extract_entries(section_lines: List[str]) -> List[Entry]:
    """Collect one entry per top-level list item, joining soft-wrapped lines."""
    entries: List[Entry] = []
    current: Optional[Entry] = None
    subsection = ""
    for raw_line in section_lines:
        line = raw_line.strip()
        if line == "":
            current = None
            continue
        if SUBSECTION_HEADING.search(line):
            subsection = line
            current = None
            continue
        bullet = BULLET.search(line)
        if bullet is not None:
            current = Entry(subsection=subsection, lines=[line[bullet.end() :]])
            entries.append(current)
            continue
        if current is not None:
            current.lines.append(line)
    return entries


def render_entry(entry: Entry) -> str:
    """Return the rendered text an entry counts toward the cap.

    The order matters: the link rule runs first so a target can never leak
    into the issue-reference rules, and the whitespace collapse runs last so
    excluded references leave no stray gap behind.
    """
    text = re.sub(r"\s+", " ", " ".join(entry.lines)).strip()
    text = INLINE_LINK.sub(lambda match: match.group(1), text)
    text = PARENTHESIZED_ISSUE_REFERENCE.sub("", text)
    text = BARE_ISSUE_REFERENCE.sub("", text)
    text = INLINE_MARKER.sub("", text)
    return re.sub(r"\s+", " ", text).strip()


def main() -> int:
    """Measure every '[Unreleased]' entry against the cap."""
    verbose = "--verbose" in sys.argv
    changelog = Path(
        os.path.abspath(os.environ["CHANGELOG_LENGTH_CHANGELOG"])
        if os.environ.get("CHANGELOG_LENGTH_CHANGELOG")
        else REPO_ROOT / "CHANGELOG.md"
    )
    text = read_file_or_throw(changelog, "read the changelog")
    section = extract_unreleased(re.split(r"\r?\n", text), changelog)
    entries = collect_measured_entries(section)

    failures: List[str] = []
    for measured in entries:
        if measured.length > CAP:
            origin = f" {measured.entry.subsection}" if measured.entry.subsection else ""
            failures.append(
                f"{display_path(changelog)} '[Unreleased]'{origin} entry {measured.number} "
                f"measures {measured.length} rendered characters, over the {CAP} cap:\n  {measured.rendered}"
            )

    if failures:
        print(
            "The changelog entry cap is broken. Entries measure as rendered text: emphasis and "
            "code markers stripped, soft-wrapped lines joined, issue references and inline "
            "link targets excluded.",
            file=sys.stderr,
        )
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        return 1

    if verbose:
        longest = max((measured.length for measured in entries), default=0)
        print(
            f"Changelog entry lengths in range: {len(entries)} entry(ies) in '[Unreleased]', "
            f"longest {longest} of {CAP} characters."
        )
        for measured in entries:
            origin = f" {measured.entry.subsection}" if measured.entry.subsection else ""
            print(f"  '[Unreleased]'{origin} entry {measured.number}: {measured.length}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
