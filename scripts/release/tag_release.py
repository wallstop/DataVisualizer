#!/usr/bin/env python3
"""Release tagging for com.wallstop-studios.data-visualizer.

Typed Python port of ``tag-release.mjs`` (issue #171): the same
fail-closed guards and console lines, driven by the standard library.

Verifies that a merged ``release/vX.Y.Z`` tree is internally consistent
and creates exactly one annotated ``vX.Y.Z`` tag. The
``.github/workflows/release.yml`` workflow runs this script on the merged
release pull request, pushes the tag, and continues to artifact
validation and publication in the same job.

Fail-closed guards:
    - The branch must be exactly ``release/v<package.json version>``
      (supported semver with the restricted prerelease suffixes used by
      prepare_release.py).
    - CHANGELOG.md must carry exactly one ``## [<version>] - YYYY-MM-DD``
      section and that section must have release notes.
    - The tag must not already exist; an existing tag is never retargeted.
    - The working tree must be clean, so the tag always points at committed
      content.
    - ``--commit`` must resolve to an existing commit.

Usage:
    python scripts/release/tag_release.py --branch release/vX.Y.Z [--commit <sha>] [--root <path>] [--dry-run]
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, cast

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIRECTORY.parent.parent

VERSION_PATTERN = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(rc|alpha|beta|preview)(?:\.(0|[1-9]\d*))?)?$")
SECTION_HEADING_PATTERN = re.compile(r"^## ")
SECTION_DATE_PATTERN = re.compile(r"^\d{4}-\d{2}-\d{2}\s*$")

USAGE = "\n".join(
    [
        "Tag a merged release: verify the release tree and create one annotated vX.Y.Z tag.",
        "",
        "Options:",
        "  --branch <release/vX.Y.Z>  Release branch that prepared this version (required).",
        "  --commit <sha>             Commit to tag (default: HEAD).",
        "  --root <path>              Repository root to operate on (default: this repository).",
        "  --dry-run                  Verify the tree without creating any tag.",
        "  --help                     Show this help.",
    ]
)


@dataclass(frozen=True)
class TagOptions:
    """The parsed tag command line."""

    branch: str
    commit: Optional[str]
    root: Optional[str]
    dry_run: bool


def parse_arguments(argv: Sequence[str]) -> TagOptions:
    """Parse the command line exactly like the original, failing closed."""
    branch: Optional[str] = None
    commit: Optional[str] = None
    root: Optional[str] = None
    dry_run = False
    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument == "--branch":
            branch = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--commit":
            commit = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--root":
            root = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--dry-run":
            dry_run = True
            index += 1
        elif argument == "--help":
            print(USAGE)
            sys.exit(0)
        else:
            raise ValueError(f"Unknown argument '{argument}'.\n\n{USAGE}")
    if branch is None:
        raise ValueError(f"--branch <release/vX.Y.Z> is required.\n\n{USAGE}")
    return TagOptions(branch=branch, commit=commit, root=root, dry_run=dry_run)


def read_value(argv: Sequence[str], index: int, flag: str) -> str:
    """Read the value after a flag, failing closed when it is missing."""
    if index >= len(argv):
        raise ValueError(f"Missing value for {flag}.")
    return argv[index]


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
    """Run a git query and return its trimmed stdout, failing closed on an error."""
    result = run_git(root, arguments)
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        raise ValueError(f"Cannot {purpose}: {detail or f'git exited with {result.returncode}'}")
    return (result.stdout or "").strip()


def read_package_version(root: Path) -> str:
    """Read the package version, requiring supported semver."""
    package_path = root / "package.json"
    try:
        package_raw = package_path.read_text(encoding="utf-8")
    except OSError:
        raise ValueError(f"Cannot read required release file '{package_path}'.") from None
    try:
        package_json: Any = json.loads(package_raw)
    except (json.JSONDecodeError, UnicodeDecodeError) as error:
        raise ValueError(f"'package.json' is not valid JSON: {error}") from None
    if not isinstance(package_json, dict):
        raise ValueError("'package.json' is missing a version field.")
    version = cast(Dict[str, Any], package_json).get("version")
    if not isinstance(version, str) or version.strip() == "":
        raise ValueError("'package.json' is missing a version field.")
    if VERSION_PATTERN.match(version) is None:
        raise ValueError(
            f"'package.json' version '{version}' is not supported semver. Use bare x.y.z "
            "with an optional -rc/-alpha/-beta/-preview (optionally .N) prerelease suffix."
        )
    return version


def verify_branch(options: TagOptions, version: str) -> str:
    """Require the exact release branch for the version being tagged."""
    expected_branch = f"release/v{version}"
    branch = options.branch
    if branch != expected_branch:
        raise ValueError(
            f"Branch '{branch}' does not match the release branch for version "
            f"'{version}' (expected '{expected_branch}'). Only a merged "
            "`release/vX.Y.Z` pull request can be tagged."
        )
    return expected_branch


def verify_changelog_section(root: Path, version: str) -> None:
    """Require exactly one dated release section with notes for the version."""
    changelog_path = root / "CHANGELOG.md"
    try:
        changelog = changelog_path.read_text(encoding="utf-8")
    except OSError:
        raise ValueError(f"Cannot read required release file '{changelog_path}'.") from None
    lines = re.split(r"\r?\n", changelog)
    heading_prefix = f"## [{version}] - "
    section_indexes: List[int] = []
    for index, line in enumerate(lines):
        if not line.startswith(heading_prefix):
            continue
        if SECTION_DATE_PATTERN.match(line[len(heading_prefix) :].strip()) is None:
            continue
        section_indexes.append(index)
    if len(section_indexes) == 0:
        raise ValueError(
            f"CHANGELOG.md carries no '## [{version}] - YYYY-MM-DD' section for version "
            f"'{version}'. The merged changelog section must match the package version exactly."
        )
    if len(section_indexes) > 1:
        raise ValueError(
            f"CHANGELOG.md carries {len(section_indexes)} '## [{version}] - ...' sections; "
            "exactly one is required."
        )
    section_index = section_indexes[0]
    section_end = section_index + 1
    while section_end < len(lines) and SECTION_HEADING_PATTERN.match(lines[section_end]) is None:
        section_end += 1
    has_notes = any(line.strip() != "" for line in lines[section_index + 1 : section_end])
    if not has_notes:
        raise ValueError(f"The '## [{version}] - ...' section has no release notes; a release tag requires notes.")


def resolve_commit(root: Path, commit_argument: Optional[str]) -> str:
    """Resolve the requested commit or HEAD."""
    if commit_argument is not None:
        return git_output(root, ["rev-parse", "--verify", f"{commit_argument}^{{commit}}"], f"resolve '{commit_argument}'")
    return git_output(root, ["rev-parse", "HEAD"], "resolve HEAD")


def verify_git_state(root: Path, tag: str) -> None:
    """Require a clean work tree and an unused tag name."""
    inside = run_git(root, ["rev-parse", "--is-inside-work-tree"])
    if inside.returncode != 0 or (inside.stdout or "").strip() != "true":
        raise ValueError(f"'{root}' is not a git work tree.")
    status = run_git(root, ["status", "--porcelain"])
    if status.returncode != 0:
        detail = (status.stderr or "").strip()
        raise ValueError(f"Cannot read git status: {detail or f'git exited with {status.returncode}'}")
    status_text = (status.stdout or "").strip()
    if status_text != "":
        raise ValueError(
            "The working tree is dirty; a release tag must point at committed content only.\n" + status_text
        )
    existing = run_git(root, ["rev-parse", "-q", "--verify", f"refs/tags/{tag}"])
    if existing.returncode == 0:
        raise ValueError(
            f"Tag '{tag}' already exists; an existing release tag is never retargeted. "
            "Cut a new version or delete the stale tag deliberately."
        )


def create_tag(root: Path, tag: str, commit: str, dry_run: bool) -> None:
    """Create the annotated tag, or print the plan under --dry-run."""
    arguments = ["tag", "-a", tag, "-m", f"Release {tag}", commit]
    if dry_run:
        print(f"dry-run: would create annotated tag {tag} at {commit}")
        return
    git_output(root, arguments, f"create tag '{tag}'")
    print(f"Created annotated tag {tag} at {commit}.")


def main() -> None:
    """Verify the merged release tree and create the annotated tag."""
    options = parse_arguments(sys.argv[1:])
    root = Path(options.root).resolve() if options.root is not None else REPOSITORY_ROOT

    version = read_package_version(root)
    verify_branch(options, version)
    verify_changelog_section(root, version)

    tag = f"v{version}"
    verify_git_state(root, tag)
    commit = resolve_commit(root, options.commit)

    create_tag(root, tag, commit, options.dry_run)
    print(f"version: {version}")
    print(f"tag: {tag}")
    print(f"commit: {commit}")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
