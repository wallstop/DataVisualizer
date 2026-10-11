#!/usr/bin/env python3
"""Enforces the assembly warning policy (issue #171).

Typed Python port of ``scripts/lint-assembly-warnings.ps1``: every assembly
definition directory carries exactly one ``csc.rsp`` holding the maximum
warning level and nothing else, exactly one strict warnings-as-errors
ruleset, and the repository ships no local analyzer payloads. The one
argument a response file may carry is ``-warn:5`` (any spelling), because
the ruleset already promotes every warning and a second switch would give
one policy two sources of truth.

Launch with ``uv run scripts/lint_assembly_warnings.py``; any Python 3.9+
interpreter works.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
import xml.etree.ElementTree as ElementTree
from pathlib import Path
from typing import List, Optional, Sequence, Tuple

# PowerShell -match is case-insensitive by default, so every pattern below
# keeps that behavior; the response-file name check stays case-sensitive
# because the original used the explicit -ceq operator.
MAXIMUM_WARNINGS_ARGUMENT_PATTERN = re.compile(r"^(?:-|/)(?:warn|w):5$", re.IGNORECASE)
ERROR_PRONE_PAYLOAD_PATTERN = re.compile(r"^ErrorProne(?:\.NET|\.Net)(?:\..*)?$", re.IGNORECASE)
ROSLYN_ANALYZER_LABEL_PATTERN = re.compile(r"(?m)^- RoslynAnalyzer\r?$", re.IGNORECASE)
RUNTIME_CONTRACT_PAYLOAD_NAMES = frozenset(
    ("runtimecontracts.dll", "runtimecontracts.dll.meta")
)
LINE_SPLIT_PATTERN = re.compile(r"\r\n|\n|\r")


def _display_path(full_path: Path, repo_root: Path) -> str:
    """Show a checked path as a repository-relative, forward-slash path."""
    return str(full_path)[len(str(repo_root)):].replace(os.sep, "/").lstrip("/")


def _resolve_root(root: str) -> Path:
    """Resolve the checked root: the argument, else the checkout holding this script."""
    candidate = root if root.strip() else str(Path(__file__).resolve().parent.parent)
    repo_root = Path(os.path.abspath(candidate))
    if not repo_root.is_dir():
        raise ValueError(f"root directory does not exist: {repo_root}")
    return repo_root


def _collect_repository_files(repo_root: Path) -> Tuple[List[Path], List[Path]]:
    """Walk the repository once and split out the assembly definitions.

    PowerShell's ``Get-ChildItem -Recurse`` skips hidden entries, which on
    every supported platform includes dot-prefixed names, so the walk prunes
    them the same way and keeps the scan off ``.git``, ``.venv``, and friends.
    """
    assemblies: List[Path] = []
    files: List[Path] = []
    for directory, subdirectories, names in os.walk(repo_root):
        subdirectories[:] = sorted(name for name in subdirectories if not name.startswith("."))
        for name in names:
            if name.startswith("."):
                continue
            path = Path(directory) / name
            files.append(path)
            if name.lower().endswith(".asmdef"):
                assemblies.append(path)
    return sorted(assemblies, key=str), files


def _directory_files(directory: Path, suffix: str) -> List[Path]:
    """List the visible files in one directory matching a case-insensitive suffix."""
    return sorted(
        (
            path
            for path in directory.iterdir()
            if path.is_file() and not path.name.startswith(".") and path.name.lower().endswith(suffix)
        ),
        key=str,
    )


def _read_text(path: Path) -> str:
    """Read a text file the way the fixture and metadata files are written."""
    return path.read_text(encoding="utf-8", errors="replace")


def _check_response_files(assembly_directory: Path, display_path: str, repo_root: Path) -> int:
    """Require exactly one ``csc.rsp`` carrying only the maximum warning level."""
    response_files = _directory_files(assembly_directory, ".rsp")
    named_response_files = [path for path in response_files if path.name == "csc.rsp"]
    if len(response_files) != 1 or len(named_response_files) != 1:
        print(
            f"[assembly-warnings] ERROR: {display_path} has {len(response_files)} compiler "
            "response files in its directory; expected exactly one named csc.rsp"
        )
        return 1

    response_file = named_response_files[0]
    response_arguments = [
        argument
        for argument in (line.strip() for line in LINE_SPLIT_PATTERN.split(_read_text(response_file)))
        if argument
    ]
    # The response file carries the warning level and nothing else, so
    # every argument other than the maximum warning level fails instead
    # of a name being checked for: a switch this guard does not name,
    # such as -nowarn: or -warnaserror-, turns off the policy silently.
    # Comment lines, several arguments on one line, and a repeated
    # warning level fail too, because the guard does not model them.
    unexpected_arguments = [
        argument for argument in response_arguments if not MAXIMUM_WARNINGS_ARGUMENT_PATTERN.search(argument)
    ]
    if len(response_arguments) != 1 or unexpected_arguments:
        found_arguments = "no arguments" if not response_arguments else " ".join(response_arguments)
        print(
            f"[assembly-warnings] ERROR: {_display_path(response_file, repo_root)} "
            "must contain exactly one argument, the maximum warning level (-warn:5); "
            f"found: {found_arguments}"
        )
        return 1
    return 0


def _check_ruleset(assembly_directory: Path, display_path: str, repo_root: Path) -> int:
    """Require exactly one strict warnings-as-errors ruleset."""
    rulesets = _directory_files(assembly_directory, ".ruleset")
    if len(rulesets) != 1:
        print(
            f"[assembly-warnings] ERROR: {display_path} has {len(rulesets)} rulesets in its "
            "directory; expected exactly 1"
        )
        return 1

    ruleset = rulesets[0]
    try:
        document = ElementTree.fromstring(ruleset.read_bytes())
    except ElementTree.ParseError as error:
        print(f"[assembly-warnings] ERROR: {_display_path(ruleset, repo_root)} is not valid XML: {error}")
        return 1

    # XPath in the original matched no-namespace elements only; ElementTree
    # tag names carry the namespace for namespaced documents, so a ruleset
    # with a default namespace fails the same way it did under PowerShell.
    error_include_all = [node for node in document.findall("IncludeAll") if node.get("Action") == "Error"]
    all_include_all = document.findall("IncludeAll")
    non_error_rules = [
        rule
        for rules in document.findall("Rules")
        for rule in rules.findall("Rule")
        if rule.get("Action") != "Error"
    ]
    included_rulesets = document.findall("Include")
    has_strict_warning_policy = (
        document.tag == "RuleSet"
        and len(error_include_all) == 1
        and len(all_include_all) == 1
        and not non_error_rules
        and not included_rulesets
    )
    if not has_strict_warning_policy:
        print(
            f"[assembly-warnings] ERROR: {_display_path(ruleset, repo_root)} must contain exactly one "
            'IncludeAll Action="Error" and may not weaken or import warning rules'
        )
        return 1
    return 0


def _is_analyzer_payload(path: Path) -> bool:
    """Return whether a file is a repository-local analyzer payload."""
    name = path.name
    if ERROR_PRONE_PAYLOAD_PATTERN.search(name) or name.lower() in RUNTIME_CONTRACT_PAYLOAD_NAMES:
        return True
    if name.lower().endswith(".dll.meta"):
        return bool(ROSLYN_ANALYZER_LABEL_PATTERN.search(_read_text(path)))
    return False


def run_lint(repo_root: Path) -> int:
    """Check every assembly definition and payload, then return the exit code."""
    assemblies, files = _collect_repository_files(repo_root)
    if not assemblies:
        print("[assembly-warnings] ERROR: no assembly definitions found")
        return 1

    errors = 0
    for assembly in assemblies:
        display_path = _display_path(assembly, repo_root)
        assembly_directory = assembly.parent
        errors += _check_response_files(assembly_directory, display_path, repo_root)
        errors += _check_ruleset(assembly_directory, display_path, repo_root)

    for payload in files:
        if not _is_analyzer_payload(payload):
            continue
        print(
            f"[assembly-warnings] ERROR: repository-local analyzer payload "
            f"{_display_path(payload, repo_root)} is forbidden; "
            "configure development analyzers in the host Unity project"
        )
        errors += 1

    if errors > 0:
        print(f"[assembly-warnings] FAILED: {errors} assembly warning policy error(s)")
        return 1

    print(
        f"[assembly-warnings] OK: all {len(assemblies)} assembly definition(s) "
        "use maximum compiler warnings and warnings as errors without repository-local analyzers"
    )
    return 0


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="assembly-warnings",
        description="Enforce the assembly warning policy.",
    )
    parser.add_argument("--root", default="", help="repository root (default: the checkout holding this script)")
    options = parser.parse_args(argv)

    try:
        repo_root = _resolve_root(options.root)
    except ValueError as error:
        print(f"[assembly-warnings] ERROR: {error}")
        return 1
    try:
        return run_lint(repo_root)
    except Exception as error:  # the PowerShell original caught everything too
        print(f"[assembly-warnings] ERROR: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
