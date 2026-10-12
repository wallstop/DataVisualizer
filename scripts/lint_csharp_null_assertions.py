#!/usr/bin/env python3
"""C# source policy enforcement: null comparisons on UnityEngine.Object must
use the explicit ``== null`` / ``!= null`` operators (issue #117).

Typed Python port of ``lint-csharp-null-assertions.js`` (issue #171): the
same masking, banned forms, and report lines, driven by the standard
library.

NUnit's Assert.IsNull / Assert.IsNotNull and the equivalent ``Is.Null`` /
``Is.Not.Null`` constraints compare with object reference semantics, so a
destroyed UnityEngine.Object wrapper (Unity's "fake null") is reported as
not-null. The same applies to ``?.``, ``??``, and implicit bool tests on
UnityEngine.Object. This lint bans the mechanically detectable forms - the
Assert.Is(Not)Null calls and the Is.Null / Is.Not.Null constraints - so
every null assertion in the repository is written as
``Assert.That(value == null)`` or ``Assert.That(value != null)``, which
binds Unity's overloaded operators and matches the lifetime the test means.

Comments and string literals are masked before scanning, so prose or
fixture text that mentions the banned forms cannot fail the scan.
Interpolated strings are masked whole, which can only hide a violation
inside a hole, never fabricate one.

'--verbose' also prints the scanned file count when clean. '--paths'
scans only the named C# files (the fast check's per-iteration loop and
the pre-commit hook's staged files; whole-tree stays the default for
CI). Exit codes: 0 = clean, 1 = at least one banned assertion remains,
2 = usage error.
"""

from __future__ import annotations

import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import List

REPO_ROOT = Path(__file__).resolve().parent.parent

# Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it.
SCAN_ROOTS = (
    [root for root in os.environ["CSHARP_NULL_ASSERTION_ROOTS"].split(os.pathsep) if root]
    if os.environ.get("CSHARP_NULL_ASSERTION_ROOTS")
    else ["Runtime", "Editor", "Tests", "Generator~"]
)

# Vendored upstream verbatim; the other C# lint excludes it for the same reason.
EXCLUDED_PREFIXES = ("Runtime/Utils/SevenZip",)

BANNED_ASSERTION = re.compile(r"\bAssert\.Is(?:Not)?Null\s*\(")
BANNED_CONSTRAINT = re.compile(r"\bIs\.Not\.Null\b|\bIs\.Null\b")


@dataclass(frozen=True)
class ScannedFile:
    """One scanned C# file with the display path its violations report."""

    full_path: Path
    display: str


def mask_noise(text: str) -> str:
    """Replace every comment and string/char literal with spaces.

    Length and newlines are preserved, so prose or fixture text that
    mentions a banned form cannot fail the scan. Interpolated strings are
    masked whole, which can only hide a violation inside a hole, never
    fabricate one.
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


def in_scan_roots(display: str) -> bool:
    """Whether a display path sits under a scan root.

    The whole-tree walk only ever sees files under the roots, so scoped
    mode applies the same membership or its verdict could diverge from the
    walk's on an out-of-root file (a code sample, for example) that CI
    never checks.
    """
    for root in SCAN_ROOTS:
        root_display = display_for(Path(root) if os.path.isabs(root) else REPO_ROOT / root)
        if display == root_display or display.startswith(root_display + "/"):
            return True
    return False


def scoped_files(argv: List[str]) -> Optional[List[ScannedFile]]:
    """Resolve an explicit ``--paths`` scan list, or None for the whole-tree walk.

    The fast check passes the changed C# files so a two-file edit scans two
    files instead of the tree; whole-tree stays the default for CI and the
    pre-commit hook. Names are repository-relative or absolute. Root
    membership and the exclusion prefixes still apply, so a scoped verdict
    is exactly the whole-tree verdict restricted to the named files, and a
    name that is not a readable C# file fails closed: the scan can never
    quietly verify nothing.
    """
    if "--paths" not in argv:
        return None
    names: List[str] = []
    for item in argv[argv.index("--paths") + 1 :]:
        if item.startswith("--"):
            break
        names.append(item)
    if not names:
        print("[csharp-null-assertions] --paths needs at least one file.", file=sys.stderr)
        raise SystemExit(2)
    files: List[ScannedFile] = []
    for name in names:
        full_path = Path(name) if os.path.isabs(name) else REPO_ROOT / name
        if full_path.suffix != ".cs" or not full_path.is_file():
            print(f"[csharp-null-assertions] --paths entry is not a readable C# file: {name}", file=sys.stderr)
            raise SystemExit(2)
        display = display_for(full_path)
        if not in_scan_roots(display):
            continue
        if any(display.startswith(prefix) for prefix in EXCLUDED_PREFIXES):
            continue
        files.append(ScannedFile(full_path=full_path, display=display))
    return sorted(files, key=lambda file: str(file.display))


def main() -> int:
    """Scan the roots for banned null assertions."""
    verbose = "--verbose" in sys.argv
    violations: List[str] = []
    scanned = 0
    explicit = scoped_files(sys.argv)
    scannable: List[ScannedFile] = (
        explicit
        if explicit is not None
        else [
            file
            for root in SCAN_ROOTS
            for file in list_csharp_files(root)
            if not any(file.display.startswith(prefix) for prefix in EXCLUDED_PREFIXES)
        ]
    )
    for file in scannable:
        scanned += 1
        text = file.full_path.read_text(encoding="utf-8")
        masked = mask_noise(text)
        for line_index, line in enumerate(masked.split("\n")):
            if BANNED_ASSERTION.search(line) or BANNED_CONSTRAINT.search(line):
                violations.append(f"{file.display}:{line_index + 1}")

    if violations:
        print(
            "Banned null assertions found. Use Assert.That(value == null) or "
            "Assert.That(value != null) so destroyed UnityEngine.Object wrappers compare "
            "with Unity's overloaded operators; Assert.IsNull/IsNotNull and the Is.Null/"
            "Is.Not.Null constraints compare with object reference semantics and miss that "
            "lifetime (issue #117):",
            file=sys.stderr,
        )
        for violation in violations:
            print(f"  {violation}", file=sys.stderr)
        scope = "the --paths selection" if explicit is not None else ", ".join(SCAN_ROOTS)
        print(f"\n{len(violations)} banned assertion(s) in {scope}.", file=sys.stderr)
        return 1
    if verbose:
        print(f"Null-assertion policy clean: {scanned} file(s) scanned.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
