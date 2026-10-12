#!/usr/bin/env python3
"""Shared release payload helpers for com.wallstop-studios.data-visualizer.

Typed Python port of ``package-payload.mjs`` (issue #171): the same
version pattern, allowlist rules, and fail-closed messages, driven by the
standard library.

Both ``verify_release.py`` and ``build_unitypackage.py`` resolve the
shipped package payload the same way: ``package.json`` must carry a name,
a supported semver version, and a non-empty ``files`` allowlist, and the
expected payload is derived from tracked files only (npm auto-included
root files plus ``files`` entries) so both tools always see committed
content.
"""

from __future__ import annotations

import json
import re
import subprocess
from pathlib import Path
from typing import Any, Dict, List, Sequence, cast

VERSION_PATTERN = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(rc|alpha|beta|preview)(?:\.(0|[1-9]\d*))?)?$")
AUTO_INCLUDE_ROOT_PATTERNS = tuple(re.compile(pattern, re.IGNORECASE) for pattern in (r"^readme", r"^licen[cs]e", r"^changelog"))


def run_git(root: Path, arguments: Sequence[str]) -> subprocess.CompletedProcess[str]:
    """Run a git query in the root, reporting a spawn failure fail-closed."""
    try:
        return subprocess.run(
            ["git", *arguments],
            cwd=str(root),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except OSError as error:
        raise ValueError(f"Cannot run git: {error}") from None


def git_output(root: Path, arguments: Sequence[str], purpose: str) -> str:
    """Run a git query and return its raw stdout, failing closed on an error."""
    result = run_git(root, arguments)
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        raise ValueError(f"Cannot {purpose}: {detail or f'git exited with {result.returncode}'}")
    return result.stdout or ""


def read_package_json(root: Path) -> Dict[str, Any]:
    """Read ``package.json`` and require a name, a version, and a files allowlist."""
    package_path = root / "package.json"
    if not package_path.exists():
        raise ValueError(f"Cannot read required release file '{package_path}'.")
    try:
        package_json: Any = json.loads(package_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError) as error:
        raise ValueError(f"'package.json' is not valid JSON: {error}") from None
    if not isinstance(package_json, dict):
        raise ValueError("'package.json' is missing a name field.")
    typed_json = cast(Dict[str, Any], package_json)
    name = typed_json.get("name")
    if not isinstance(name, str) or name.strip() == "":
        raise ValueError("'package.json' is missing a name field.")
    version = typed_json.get("version")
    if not isinstance(version, str) or version.strip() == "":
        raise ValueError("'package.json' is missing a version field.")
    if VERSION_PATTERN.match(version) is None:
        raise ValueError(
            f"'package.json' version '{version}' is not supported semver. Use bare x.y.z "
            "with an optional -rc/-alpha/-beta/-preview (optionally .N) prerelease suffix."
        )
    files = typed_json.get("files")
    if not isinstance(files, list) or len(cast(List[Any], files)) == 0:
        raise ValueError("'package.json' must carry a non-empty 'files' publish allowlist.")
    return typed_json


def is_auto_included_root_file(relative_path: str) -> bool:
    """Return whether npm packs this root-level file without a files entry."""
    if "/" in relative_path:
        return False
    if relative_path == "package.json":
        return True
    return any(pattern.match(relative_path) for pattern in AUTO_INCLUDE_ROOT_PATTERNS)


def is_allowlisted(relative_path: str, files_entries: Sequence[str]) -> bool:
    """Return whether a tracked path is covered by the publish allowlist."""
    if is_auto_included_root_file(relative_path):
        return True
    for raw_entry in files_entries:
        entry = raw_entry.rstrip("/")
        if relative_path == entry or relative_path.startswith(f"{entry}/"):
            return True
    return False


def verify_git_work_tree(root: Path) -> None:
    """Require the root to be a git work tree, so tools always see committed content."""
    inside = run_git(root, ["rev-parse", "--is-inside-work-tree"])
    if inside.returncode != 0 or (inside.stdout or "").strip() != "true":
        raise ValueError(f"'{root}' is not a git work tree.")


def list_expected_payload_paths(root: Path, package_json: Any) -> List[str]:
    """List the tracked, allowlisted payload paths in sorted order."""
    tracked_lines = git_output(root, ["ls-files"], "list tracked files").splitlines()
    tracked = [line for line in tracked_lines if line.strip() != ""]
    expected = [path for path in tracked if is_allowlisted(path, package_json["files"])]
    if len(expected) == 0:
        raise ValueError("The publish allowlist in 'package.json' selects no tracked files.")
    return sorted(expected)
