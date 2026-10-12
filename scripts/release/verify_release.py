#!/usr/bin/env python3
"""Release payload verification for com.wallstop-studios.data-visualizer.

Typed Python port of ``verify-release.mjs`` (issue #171): the same
fail-closed guards, tar and npm handling, and report lines, driven by the
standard library.

Verifies that a ``vX.Y.Z`` tag matches the package version and that the
npm package payload matches the declared allowlist, fail-closed, before
the tag-driven publish workflow performs any irreversible publication.
The ``.github/workflows/release.yml`` workflow runs this script on the
exact release tag (or on a manual dispatch rerun).

Fail-closed guards:
    - ``--tag`` must be exactly ``v<package.json version>`` (supported semver
      with the restricted prerelease suffixes used by the release scripts).
    - ``package.json`` must exist, be valid JSON, and carry a name, a version,
      and a non-empty ``files`` array.
    - The tree must be a git work tree; the expected payload is derived from
      tracked files only, so the verification always sees committed content.
    - Every packed file must be allowlisted (npm auto-included root files or
      covered by a ``files`` entry), and every allowlisted tracked file must
      be packed. Extra (junk) and missing (shrunk payload) entries both fail.

Reports the package version, the npm dist-tag (``next`` for restricted
prerelease suffixes, ``latest`` otherwise), the packed tarball filename,
and the packed entry count.

Usage:
    python scripts/release/verify_release.py --tag vX.Y.Z [--root <path>] [--package-file <path>]
"""

from __future__ import annotations

import json
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Set, Tuple, cast

from package_payload import (
    list_expected_payload_paths,
    read_package_json,
    verify_git_work_tree,
    VERSION_PATTERN,
)

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIRECTORY.parent.parent

USAGE = "\n".join(
    [
        "Verify a release tag and its npm package payload before publishing.",
        "",
        "Options:",
        "  --tag <vX.Y.Z>        Release tag to verify, e.g. v0.0.38 (required).",
        "  --package-file <path> Verify this tarball instead of packing the tree.",
        "  --root <path>         Repository root to operate on (default: this repository).",
        "  --help                Show this help.",
    ]
)


@dataclass(frozen=True)
class VerifyOptions:
    """The parsed verify command line."""

    tag: str
    package_file: Optional[str]
    root: Optional[str]


def parse_arguments(argv: Sequence[str]) -> VerifyOptions:
    """Parse the command line exactly like the original, failing closed."""
    tag: Optional[str] = None
    package_file: Optional[str] = None
    root: Optional[str] = None
    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument == "--tag":
            tag = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--package-file":
            package_file = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--root":
            root = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--help":
            print(USAGE)
            sys.exit(0)
        else:
            raise ValueError(f"Unknown argument '{argument}'.\n\n{USAGE}")
    if tag is None:
        raise ValueError(f"--tag <vX.Y.Z> is required.\n\n{USAGE}")
    return VerifyOptions(tag=tag, package_file=package_file, root=root)


def read_value(argv: Sequence[str], index: int, flag: str) -> str:
    """Read the value after a flag, failing closed when it is missing."""
    if index >= len(argv):
        raise ValueError(f"Missing value for {flag}.")
    return argv[index]


def verify_tag(tag: str, package_version: str) -> str:
    """Require the exact release tag for the checked-out package."""
    expected_tag = f"v{package_version}"
    if tag != expected_tag:
        raise ValueError(
            f"Tag '{tag}' does not match the package version '{package_version}' (expected "
            f"'{expected_tag}'). Only the exact release tag for the checked-out package can publish."
        )
    return expected_tag


def run_npm(arguments: Sequence[str], root: Path, purpose: str) -> str:
    """Run npm, launching it through a shell on Windows where npm is a .cmd shim."""
    try:
        completed = subprocess.run(
            ["npm", *arguments],
            cwd=str(root),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            shell=sys.platform == "win32",
            check=False,
        )
    except OSError as error:
        raise ValueError(f"Cannot run npm: {error}") from None
    if completed.returncode != 0:
        detail = (completed.stderr or completed.stdout or "").strip()
        raise ValueError(f"Cannot {purpose}: {detail or f'npm exited with {completed.returncode}'}")
    return completed.stdout or ""


def parse_npm_pack_json(output: str) -> List[Dict[str, Any]]:
    """Extract the JSON array defensively; npm may emit notice lines around it."""
    start = output.find("[")
    end = output.rfind("]")
    if start == -1 or end < start:
        raise ValueError("npm pack --json produced no JSON array.")
    return cast(List[Dict[str, Any]], json.loads(output[start : end + 1]))


def list_tarball_paths(root: Path, tarball_path: Path) -> Set[str]:
    """List the file entries of an npm tarball, requiring the package/ root."""
    # GNU tar parses the first colon in the -f archive name as a remote host
    # spec, so the tarball is listed from its own directory under a colon-free
    # relative name.
    try:
        completed = subprocess.run(
            ["tar", "-tzf", tarball_path.name],
            cwd=str(tarball_path.parent),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except OSError as error:
        raise ValueError(f"Cannot run tar: {error}") from None
    if completed.returncode != 0:
        detail = (completed.stderr or completed.stdout or "").strip()
        raise ValueError(f"Cannot read tarball '{tarball_path}': {detail or f'tar exited with {completed.returncode}'}")
    paths: Set[str] = set()
    for line in (completed.stdout or "").splitlines():
        entry = line.strip()
        # npm package tarballs carry file entries only; directory entries
        # from foreign archives contribute no file, so they are skipped.
        if entry == "" or entry == "package" or entry.endswith("/"):
            continue
        if not entry.startswith("package/"):
            raise ValueError(
                f"Tarball '{tarball_path}' carries the out-of-place entry '{entry}'; "
                "npm package tarballs must hold every file under the package/ root."
            )
        paths.add(entry[len("package/") :])
    return paths


def resolve_packed_paths(root: Path, package_file: str) -> Tuple[Set[str], str]:
    """Resolve the packed payload either from a tarball or by packing the tree."""
    if package_file:
        tarball_path = Path(package_file) if Path(package_file).is_absolute() else root / package_file
        if not tarball_path.exists():
            raise ValueError(f"Cannot read package file '{tarball_path}'.")
        return list_tarball_paths(root, tarball_path), tarball_path.name
    packed = parse_npm_pack_json(run_npm(["pack", "--json"], root, "pack the package"))[0]
    files = cast(List[Dict[str, Any]], packed["files"])
    return {str(file["path"]) for file in files}, str(packed["filename"])


def verify_payload(expected: Set[str], packed_paths: Set[str]) -> None:
    """Require the packed payload and the allowlist to cover each other exactly."""
    unallowlisted = sorted(path for path in packed_paths if path not in expected)
    missing = sorted(path for path in expected if path not in packed_paths)
    if len(unallowlisted) > 0:
        raise ValueError(
            f"The package carries {len(unallowlisted)} file(s) outside the publish allowlist "
            "in 'package.json'; extend the allowlist deliberately or remove the files:\n"
            + "\n".join(unallowlisted)
        )
    if len(missing) > 0:
        raise ValueError(
            f"The package is missing {len(missing)} allowlisted file(s) that the repository "
            "tracks; the payload would silently shrink:\n" + "\n".join(missing)
        )


def resolve_dist_tag(version: str) -> str:
    """Map a restricted prerelease suffix to the next dist-tag, else latest."""
    match = VERSION_PATTERN.match(version)
    return "next" if match is not None and match.group(4) is not None else "latest"


def main() -> None:
    """Verify one tag and payload, reporting the publish outputs."""
    options = parse_arguments(sys.argv[1:])
    root = Path(options.root).resolve() if options.root is not None else REPOSITORY_ROOT

    package_json = read_package_json(root)
    verify_tag(options.tag, package_json["version"])
    verify_git_work_tree(root)

    expected = set(list_expected_payload_paths(root, package_json))
    packed_paths, package_file = resolve_packed_paths(root, options.package_file or "")
    verify_payload(expected, packed_paths)

    npm_tag = resolve_dist_tag(package_json["version"])
    print(f"pkg: {package_json['name']}")
    print(f"version: {package_json['version']}")
    print(f"npm_tag: {npm_tag}")
    print(f"package_file: {package_file}")
    print(f"entries: {len(packed_paths)}")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
