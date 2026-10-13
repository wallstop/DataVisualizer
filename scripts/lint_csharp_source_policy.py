#!/usr/bin/env python3
"""Runs the three C# source policy lints in one interpreter (issue #171).

The pre-commit hook used to spawn one ``uv run`` child per lint, so every C#
commit paid three interpreter resolutions plus a per-hook wrapper overhead.
This runner imports the member-order, null-assertion, and using-placement
lints in-process, keeps their verdicts and messages untouched, and reports
each lint's result separately so the combined hook stays reviewable.
``--fix`` is refused: the composite is a read-only gate, and fixing stays
with the individual lints.

Launch with ``uv run scripts/lint_csharp_source_policy.py``; any Python
3.9+ interpreter works.
"""

from __future__ import annotations

import importlib
import sys
from pathlib import Path
from typing import List, Optional, Sequence, Tuple

sys.dont_write_bytecode = True

SCRIPTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPTS_DIR.parent

# Same order the individual pre-commit hooks ran in, so a combined failure
# reads the way reviewers are used to.
LINTS: Tuple[Tuple[str, str], ...] = (
    ("member-order", "lint_csharp_member_order"),
    ("null-assertions", "lint_csharp_null_assertions"),
    ("usings", "lint_csharp_usings"),
)


def parse_arguments(argv: Sequence[str]) -> Tuple[Optional[List[str]], bool]:
    """Resolve ``--paths`` and ``--verbose`` the way the individual lints do.

    Every argument after ``--paths`` belongs to the file list until the next
    flag, matching the individual lints' own parsing, so pre-commit can keep
    appending the staged files to the entry line.
    """
    verbose = "--verbose" in argv
    paths: Optional[List[str]] = None
    if "--paths" in argv:
        names: List[str] = []
        for item in argv[argv.index("--paths") + 1 :]:
            if item.startswith("--"):
                break
            names.append(item)
        paths = names
    return paths, verbose


def is_readable_csharp(name: str) -> bool:
    """Return whether a ``--paths`` entry resolves to a readable C# file."""
    full_path = Path(name) if Path(name).is_absolute() else REPO_ROOT / name
    return full_path.suffix == ".cs" and full_path.is_file()


def run_lint(label: str, module_name: str, argv_tail: Sequence[str]) -> bool:
    """Run one lint in-process and report its verdict.

    The lint modules read ``sys.argv`` for their flags, so the tail is
    staged there for the call; ``main`` returns an exit code, and a lint
    that raises ``SystemExit`` or crashes still fails closed instead of
    counting as a pass.
    """
    if str(SCRIPTS_DIR) not in sys.path:
        sys.path.insert(0, str(SCRIPTS_DIR))
    module = importlib.import_module(module_name)
    previous_argv = sys.argv
    sys.argv = [module_name + ".py", *argv_tail]
    try:
        if module_name == "lint_csharp_member_order":
            exit_code = module.main(list(sys.argv[1:]))
        else:
            exit_code = module.main()
    except SystemExit as exit_error:
        code = exit_error.code
        exit_code = 0 if code is None else code if isinstance(code, int) else 1
    except Exception as error:  # a crashed lint must fail the hook, never pass silently
        print(f"[csharp-source-policy] {label}: crashed: {error!r}", file=sys.stderr)
        return False
    finally:
        sys.argv = previous_argv
    if exit_code != 0:
        print(f"[csharp-source-policy] {label}: FAIL", file=sys.stderr)
        return False
    print(f"[csharp-source-policy] {label}: ok")
    return True


def main(argv: Optional[Sequence[str]] = None) -> int:
    """Run every C# source lint once and aggregate their verdicts."""
    arguments = list(sys.argv[1:] if argv is None else argv)
    if "--fix" in arguments:
        print(
            "[csharp-source-policy] --fix is not supported by the composite runner; "
            "run the individual lints to fix.",
            file=sys.stderr,
        )
        return 2

    paths, verbose = parse_arguments(arguments)
    if paths is not None:
        invalid = [name for name in paths if not is_readable_csharp(name)]
        if invalid:
            print(
                "[csharp-source-policy] --paths entry is not a readable C# file: "
                + ", ".join(invalid),
                file=sys.stderr,
            )
            return 2

    argv_tail: List[str] = []
    if paths is not None:
        argv_tail.extend(("--paths", *paths))
    if verbose:
        argv_tail.append("--verbose")

    outcomes = [run_lint(label, module_name, argv_tail) for label, module_name in LINTS]
    failed = [label for (label, _), ok in zip(LINTS, outcomes) if not ok]
    scope = f"{len(paths)} file(s)" if paths is not None else "the whole tree"
    if failed:
        print(
            f"[csharp-source-policy] {len(failed)} of {len(LINTS)} lint(s) failed "
            f"({', '.join(failed)}) over {scope}.",
            file=sys.stderr,
        )
        return 1
    print(f"[csharp-source-policy] all {len(LINTS)} lints clean over {scope}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
