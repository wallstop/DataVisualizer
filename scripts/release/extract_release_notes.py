#!/usr/bin/env python3
"""Release-notes extraction for com.wallstop-studios.data-visualizer.

Typed Python port of ``extract-release-notes.mjs`` (issue #171): the same
section grammar, fail-closed errors, and output modes, driven by the
standard library.

The tag-driven publish workflow generates GitHub Release notes from the
exact rotated CHANGELOG section for the released version. This script is
the tested mechanism behind that step: it prints the body of the
``## [X.Y.Z] - YYYY-MM-DD`` section (everything between its heading line
and the next ``## `` heading) and fails closed when the section is
missing or duplicated, so a release can never go out with empty or
ambiguous notes.

Usage:
    python scripts/release/extract_release_notes.py --version X.Y.Z [--root <path>] [--output <path>]

Options:
    --version <X.Y.Z>  Version section to extract (required, no leading 'v').
    --root <path>      Repository root to operate on (default: this repository).
    --output <path>    Write the notes to this file instead of stdout.
    --help             Show this help.
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional, Sequence

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIRECTORY.parent.parent

USAGE = "\n".join(
    [
        "Extract the CHANGELOG release notes for a released version.",
        "",
        "Options:",
        "  --version <X.Y.Z>  Version section to extract (required, no leading 'v').",
        "  --root <path>      Repository root to operate on (default: this repository).",
        "  --output <path>    Write the notes to this file instead of stdout.",
        "  --help             Show this help.",
    ]
)


@dataclass(frozen=True)
class ExtractOptions:
    """The parsed extract command line."""

    version: str
    root: Optional[str]
    output: Optional[str]


def parse_arguments(argv: Sequence[str]) -> ExtractOptions:
    """Parse the command line exactly like the original, failing closed."""
    version: Optional[str] = None
    root: Optional[str] = None
    output: Optional[str] = None
    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument == "--version":
            version = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--root":
            root = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--output":
            output = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--help":
            print(USAGE)
            sys.exit(0)
        else:
            raise ValueError(f"Unknown argument '{argument}'.\n\n{USAGE}")
    if version is None:
        raise ValueError(f"--version <X.Y.Z> is required.\n\n{USAGE}")
    return ExtractOptions(version=version, root=root, output=output)


def read_value(argv: Sequence[str], index: int, flag: str) -> str:
    """Read the value after a flag, failing closed when it is missing."""
    if index >= len(argv):
        raise ValueError(f"Missing value for {flag}.")
    return argv[index]


def escape_reg_exp(text: str) -> str:
    """Escape a version for an exact heading match."""
    return re.sub(r"[.*+?^${}()|[\]\\]", r"\\\g<0>", text)


def extract_section(changelog: str, version: str) -> str:
    """Extract one version section's body, failing closed on ambiguity."""
    lines = re.split(r"\r?\n", changelog)
    heading_pattern = re.compile(rf"^## \[{escape_reg_exp(version)}\](?:\s|$)")
    heading_indexes: List[int] = [index for index, line in enumerate(lines) if heading_pattern.search(line)]
    if len(heading_indexes) == 0:
        raise ValueError(f"CHANGELOG.md has no '## [{version}]' section.")
    if len(heading_indexes) > 1:
        raise ValueError(f"CHANGELOG.md carries {len(heading_indexes)} '## [{version}]' sections.")
    start = heading_indexes[0] + 1
    end = len(lines)
    for index in range(start, len(lines)):
        if re.match(r"^## ", lines[index]):
            end = index
            break
    body = "\n".join(lines[start:end]).strip()
    if body == "":
        raise ValueError(f"CHANGELOG.md section '## [{version}]' is empty.")
    return f"{body}\n"


def main() -> None:
    """Extract the notes to stdout or to the requested output file."""
    options = parse_arguments(sys.argv[1:])
    root = Path(options.root).resolve() if options.root is not None else REPOSITORY_ROOT
    version = options.version
    if re.match(r"^v", version, re.IGNORECASE):
        bare = re.sub(r"^v", "", version, count=1, flags=re.IGNORECASE)
        raise ValueError(
            f"--version expects the bare package version '{bare}', not the tag '{version}'."
        )
    changelog_path = root / "CHANGELOG.md"
    try:
        changelog = changelog_path.read_text(encoding="utf-8")
    except OSError:
        raise ValueError(f"Cannot read '{changelog_path}'.") from None
    notes = extract_section(changelog, version)
    if options.output is not None:
        output_path = Path(options.output) if Path(options.output).is_absolute() else root / options.output
        output_path = output_path.resolve()
        output_path.write_text(notes, encoding="utf-8", newline="")
        print(f"notes: {options.output}")
    else:
        sys.stdout.write(notes)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
