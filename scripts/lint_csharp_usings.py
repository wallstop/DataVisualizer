#!/usr/bin/env python3
"""C# source policy enforcement: ``using`` directives live inside the
namespace (issue #124).

Typed Python port of ``lint-csharp-usings.js`` (issue #171): the same
sanctioned shapes, masking, ``--fix`` behavior, and report lines, driven by
the standard library.

Guidance rule 1 mandates block-scoped namespaces with ``using`` directives
inside them, but nothing failed CI when a file drifted. This lint reports
every ``using`` directive that appears before the ``namespace`` declaration.
Two file-level shapes stay sanctioned because the language requires them:

- Files without any namespace (assembly-attribute files such as
  Runtime/AnalyzerSuppressions.cs) keep their file-level usings.
- Files whose pre-namespace region carries an ``[assembly: ...]`` attribute
  (the ``InternalsVisibleTo`` preamble in Runtime/DataVisualizer/BaseDataObject.cs)
  need those usings at file level; the region is sanctioned as a whole.
  ``global using`` directives are likewise legal only at file level and are
  never reported.

Comments and string literals are masked before scanning, so prose or
fixture text that mentions a misplaced ``using`` cannot fail the scan.

'--fix' moves reportable usings inside the namespace's opening brace,
preserving order. It declines files whose pre-namespace region holds
anything besides comments, usings, and blank lines (for example ``#if``
blocks), leaving those as manual barriers. '--verbose' also prints the
scanned file count when clean. Exit codes: 0 = clean (or fixed clean),
1 = at least one misplaced using remains.
"""

from __future__ import annotations

import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional

REPO_ROOT = Path(__file__).resolve().parent.parent

# Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it.
SCAN_ROOTS = (
    [root for root in os.environ["CSHARP_USINGS_ROOTS"].split(os.pathsep) if root]
    if os.environ.get("CSHARP_USINGS_ROOTS")
    else ["Runtime", "Editor", "Tests", "Generator~"]
)

# Vendored upstream verbatim; the other C# lint excludes it for the same reason.
EXCLUDED_PREFIXES = ("Runtime/Utils/SevenZip",)

NAMESPACE_LINE = re.compile(r"^\s*namespace\s")
USING_LINE = re.compile(r"^\s*using\s(?!.*\bglobal\b)")
ASSEMBLY_ATTRIBUTE = re.compile(r"\[\s*assembly\s*:")
OPEN_BRACE_LINE = re.compile(r"^\s*\{")


@dataclass(frozen=True)
class ScannedFile:
    """One scanned C# file with the display path its violations report."""

    full_path: Path
    display: str


def mask_noise(text: str) -> str:
    """Replace every comment and string/char literal with spaces.

    Length and newlines are preserved, so prose or fixture text that
    mentions a misplaced ``using`` cannot fail the scan. Interpolated
    strings are masked whole.
    """
    out = list(text)

    def blank(start: int, end: int) -> None:
        for index in range(start, min(end, len(out))):
            if out[index] != "\n":
                out[index] = " "

    index = 0
    while index < len(text):
        char = text[index]
        next = text[index + 1] if index + 1 < len(text) else ""

        if char == "/" and next == "/":
            end = text.find("\n", index)
            end = len(text) if end < 0 else end
            blank(index, end)
            index = end
            continue
        if char == "/" and next == "*":
            end = text.find("*/", index + 2)
            close = len(text) if end < 0 else end + 2
            blank(index, close)
            index = close
            continue
        if char == '"' or (char == "@" and next == '"') or (char == "$" and next == '"'):
            verbatim = char == "@"
            quote = index + 1 if (verbatim or char == "$") else index
            cursor = quote + 1
            while cursor < len(text):
                if verbatim:
                    if text[cursor] == '"' and cursor + 1 < len(text) and text[cursor + 1] == '"':
                        cursor += 2
                        continue
                    if text[cursor] == '"':
                        break
                    cursor += 1
                    continue
                if text[cursor] == "\\":
                    cursor += 2
                    continue
                if text[cursor] == '"':
                    break
                cursor += 1
            blank(index, min(cursor + 1, len(text)))
            index = min(cursor + 1, len(text))
            continue
        index += 1
    return "".join(out)


def display_for(full_path: Path) -> str:
    """Show a repo-relative path, or an absolute one when it cannot be relativized.

    A self-test fixture root can live on another Windows drive; relpath then
    raises where the original returned a drive-absolute path, which is kept
    so the display never shows a fabricated relative chain.
    """
    try:
        relative = os.path.relpath(full_path, REPO_ROOT).replace(os.sep, "/")
    except ValueError:
        return str(full_path).replace(os.sep, "/")
    return str(full_path).replace(os.sep, "/") if relative.startswith("..") else relative


def list_csharp_files(root: str) -> List[ScannedFile]:
    """Walk one scan root and return its C# files in display order."""
    files: List[ScannedFile] = []
    absolute_root = Path(root) if os.path.isabs(root) else REPO_ROOT / root
    if not absolute_root.exists():
        return files
    stack = [absolute_root]
    while stack:
        current = stack.pop()
        for entry in sorted(current.iterdir()):
            full_path = current / entry.name
            if entry.is_dir():
                stack.append(full_path)
                continue
            if entry.is_file() and entry.name.endswith(".cs"):
                files.append(ScannedFile(full_path=full_path, display=display_for(full_path)))
    return sorted(files, key=lambda file: str(file.display))


def find_misplaced_using_lines(masked_lines: List[str]) -> Optional[List[int]]:
    """Return the indexes of misplaced usings, or None when the shape is sanctioned.

    A file with no namespace at all, or an assembly attribute in its
    pre-namespace region, keeps its file-level usings.
    """
    namespace_index = next((index for index, line in enumerate(masked_lines) if NAMESPACE_LINE.search(line)), -1)
    if namespace_index < 0:
        return None

    region = masked_lines[:namespace_index]
    misplaced: List[int] = []
    for index, line in enumerate(region):
        if ASSEMBLY_ATTRIBUTE.search(line):
            return None
        if USING_LINE.search(line):
            misplaced.append(index)
    return misplaced


def fix_file(text: str, masked_lines: List[str]) -> Optional[str]:
    """Move the misplaced usings after the namespace's opening brace.

    Returns None when the region holds a manual barrier (anything besides
    comments, usings, and blank lines) or the namespace brace cannot be
    located.
    """
    namespace_index = next((index for index, line in enumerate(masked_lines) if NAMESPACE_LINE.search(line)), -1)
    if namespace_index < 0:
        return None

    lines = text.split("\n")
    using_indexes: List[int] = []
    for index in range(namespace_index):
        masked = masked_lines[index]
        if USING_LINE.search(masked):
            using_indexes.append(index)
            continue
        # Masked-blank lines are comments, blank lines, or strings: safe to leave in place.
        if masked.strip() == "":
            continue
        return None
    if len(using_indexes) == 0:
        return None

    brace_index = -1
    for index in range(namespace_index, len(lines)):
        if OPEN_BRACE_LINE.search(masked_lines[index]):
            brace_index = index
            break
    if brace_index < 0:
        return None

    usings = [f"    {lines[index].strip()}" for index in using_indexes]
    kept = [line for index, line in enumerate(lines) if index not in using_indexes]
    brace_shift = brace_index - sum(1 for index in using_indexes if index < brace_index)
    kept[brace_shift + 1 : brace_shift + 1] = usings
    return "\n".join(kept)


def main() -> int:
    """Scan the roots for misplaced usings, optionally fixing them."""
    fix = "--fix" in sys.argv
    verbose = "--verbose" in sys.argv
    violations: List[str] = []
    fixes: List[str] = []
    scanned = 0
    for root in SCAN_ROOTS:
        for file in list_csharp_files(root):
            if any(file.display.startswith(prefix) for prefix in EXCLUDED_PREFIXES):
                continue
            scanned += 1
            text = file.full_path.read_text(encoding="utf-8")
            masked_lines = mask_noise(text).split("\n")
            misplaced = find_misplaced_using_lines(masked_lines)
            if misplaced is None or len(misplaced) == 0:
                continue
            if fix:
                fixed = fix_file(text, masked_lines)
                if fixed is not None:
                    file.full_path.write_text(fixed, encoding="utf-8", newline="")
                    fixes.append(str(file.display))
                    continue
                violations.append(f"{file.display}:{misplaced[0] + 1} (manual barrier; fix declined)")
                continue
            for index in misplaced:
                violations.append(f"{file.display}:{index + 1}")

    if fix and len(fixes) > 0:
        for fixed in fixes:
            print(f"Moved usings inside the namespace: {fixed}")
    if violations:
        print(
            "Usings found outside the namespace. Move every `using` directive inside the "
            "namespace block (guidance rule 1). File-level usings stay sanctioned only for "
            "namespaceless assembly-attribute files and for `[assembly: ...]` preambles such "
            "as InternalsVisibleTo (issue #124):",
            file=sys.stderr,
        )
        for violation in violations:
            print(f"  {violation}", file=sys.stderr)
        print(f"\n{len(violations)} misplaced using(s) in {', '.join(SCAN_ROOTS)}.", file=sys.stderr)
        return 1
    if verbose:
        suffix = f"; {len(fixes)} file(s) fixed" if len(fixes) > 0 else ""
        print(f"Using-placement policy clean: {scanned} file(s) scanned{suffix}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
