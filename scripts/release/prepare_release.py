#!/usr/bin/env python3
"""Release preparation for com.wallstop-studios.data-visualizer.

Typed Python port of ``prepare-release.mjs`` (issue #171): the same bump
rules, changelog rotation, version sync, and console lines, driven by the
standard library.

Bumps ``package.json``, syncs the ``**Version**:`` line in
``.llm/context.md``, and rotates the ``## [Unreleased]`` changelog section
into ``## [X.Y.Z] - YYYY-MM-DD`` (Keep a Changelog 1.1.0). The
dispatchable ``.github/workflows/release.yml`` workflow runs this script
and opens the ``release/vX.Y.Z`` pull request.

Usage:
    python scripts/release/prepare_release.py --bump patch|minor|major [--root <path>] [--dry-run]
    python scripts/release/prepare_release.py --version <semver>   [--root <path>] [--dry-run]

Explicit versions accept bare semver with an optional prerelease suffix
restricted to ``-rc``, ``-alpha``, ``-beta``, or ``-preview`` (optionally
``.N``), matching the dist-tag rules in ``.github/workflows/release.yml``.
A version bump applies to the numeric core and drops an existing
prerelease suffix; prerelease flows use ``--version`` instead.

``package.json`` is patched in place (raw text, validated by a JSON parse
after patching), so its formatting is preserved byte-for-byte apart from
the version value.
"""

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, cast

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIRECTORY.parent.parent

VERSION_PATTERN = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(rc|alpha|beta|preview)(?:\.(0|[1-9]\d*))?)?$")
UNRELEASED_HEADING_PATTERN = re.compile(r"^## \[Unreleased\]\s*$")
SECTION_HEADING_PATTERN = re.compile(r"^## ")
CONTEXT_VERSION_PATTERN = re.compile(r"(\*\*Version\*\*:\s*)([^\r\n]*)")
PACKAGE_VERSION_PATTERN = re.compile(r'("version"\s*:\s*")([^"\r\n]*)(")')

USAGE = "\n".join(
    [
        "Prepare a release: bump the version and rotate the changelog.",
        "",
        "Options:",
        "  --bump <patch|minor|major>  Apply a semantic version bump.",
        "  --version <semver>          Set an explicit version (mutually exclusive with --bump).",
        "  --root <path>               Repository root to operate on (default: this repository).",
        "  --dry-run                   Report the plan without writing any file.",
        "  --help                      Show this help.",
    ]
)


@dataclass(frozen=True)
class PrepareOptions:
    """The parsed prepare command line."""

    bump: Optional[str]
    version: Optional[str]
    root: Optional[str]
    dry_run: bool


def parse_arguments(argv: Sequence[str]) -> PrepareOptions:
    """Parse the command line exactly like the original, failing closed."""
    bump: Optional[str] = None
    version: Optional[str] = None
    root: Optional[str] = None
    dry_run = False
    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument == "--bump":
            bump = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--version":
            version = read_value(argv, index + 1, argument)
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
    return PrepareOptions(bump=bump, version=version, root=root, dry_run=dry_run)


def read_value(argv: Sequence[str], index: int, flag: str) -> str:
    """Read the value after a flag, failing closed when it is missing."""
    if index >= len(argv):
        raise ValueError(f"Missing value for {flag}.")
    return argv[index]


def validate_semver(value: str, label: str) -> str:
    """Require a supported semver version."""
    if VERSION_PATTERN.match(value) is None:
        raise ValueError(
            f"{label} '{value}' is not supported semver. Use bare x.y.z with an optional "
            "-rc/-alpha/-beta/-preview (optionally .N) prerelease suffix."
        )
    return value


def bump_version(current: str, bump: str) -> str:
    """Apply one semantic bump to the numeric core, dropping any prerelease."""
    major, minor, patch = (int(part) for part in current.split("-")[0].split("."))
    if bump == "patch":
        return f"{major}.{minor}.{patch + 1}"
    if bump == "minor":
        return f"{major}.{minor + 1}.0"
    return f"{major + 1}.0.0"


def resolve_target_version(options: PrepareOptions, current: str) -> str:
    """Resolve the explicit version or apply the requested bump."""
    if options.version is not None:
        if options.bump is not None:
            raise ValueError("--version and --bump are mutually exclusive; provide only one.")
        return validate_semver(options.version, "Explicit version")
    if options.bump is None:
        raise ValueError(f"Provide --bump <patch|minor|major> or --version <semver>.\n\n{USAGE}")
    bump = options.bump
    if bump not in ("patch", "minor", "major"):
        raise ValueError(f"--bump must be patch, minor, or major; got '{bump}'.")
    target = bump_version(validate_semver(current, "Current version"), bump)
    if target == current:
        raise ValueError(f"A {bump} bump of '{current}' produces no version change.")
    return target


@dataclass(frozen=True)
class RepositoryFiles:
    """The three release files, read once."""

    package_path: Path
    changelog_path: Path
    context_path: Path
    package_raw: str
    package_json: Dict[str, Any]
    changelog: str
    context: str


def read_repository_files(root: Path) -> RepositoryFiles:
    """Read the three release files, requiring all of them."""
    package_path = root / "package.json"
    changelog_path = root / "CHANGELOG.md"
    context_path = root / ".llm/context.md"
    for file_path in (package_path, changelog_path, context_path):
        try:
            file_path.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            raise ValueError(f"Cannot read required release file '{file_path}'.") from None
    package_raw = package_path.read_text(encoding="utf-8")
    try:
        package_json: Any = json.loads(package_raw)
    except (json.JSONDecodeError, UnicodeDecodeError) as error:
        raise ValueError(f"'package.json' is not valid JSON: {error}") from None
    if not isinstance(package_json, dict):
        raise ValueError("'package.json' is missing a version field.")
    return RepositoryFiles(
        package_path=package_path,
        changelog_path=changelog_path,
        context_path=context_path,
        package_raw=package_raw,
        package_json=cast(Dict[str, Any], package_json),
        changelog=changelog_path.read_text(encoding="utf-8"),
        context=context_path.read_text(encoding="utf-8"),
    )


def patch_package_version(package_raw: str, version: str) -> str:
    """Patch the first version value, then require the result to parse to it."""
    updated = PACKAGE_VERSION_PATTERN.sub(rf"\g<1>{version}\g<3>", package_raw, count=1)
    try:
        parsed: Any = json.loads(updated)
    except (json.JSONDecodeError, UnicodeDecodeError) as error:
        raise ValueError(
            f"Patching 'package.json' did not produce '{version}': {error}. "
            "The first \"version\" key must be the top-level package version."
        ) from None
    if not isinstance(parsed, dict):
        raise ValueError(
            f"Patching 'package.json' did not produce '{version}': the patched file is not a "
            "JSON object. The first \"version\" key must be the top-level package version."
        )
    actual = cast(Dict[str, Any], parsed).get("version")
    if actual != version:
        raise ValueError(
            f"Patching 'package.json' did not produce '{version}': 'package.json' carries "
            f"version '{actual}' after patching. "
            "The first \"version\" key must be the top-level package version."
        )
    return updated


def sync_context_version(context: str, version: str) -> str:
    """Sync the '**Version**:' line the harness lints keep in sync."""
    updated = CONTEXT_VERSION_PATTERN.sub(rf"\g<1>{version}", context, count=1)
    if updated == context:
        raise ValueError("'.llm/context.md' does not carry a '**Version**: x.y.z' line to sync.")
    return updated


def duplicate_section_index(lines: List[str], version: str) -> int:
    """Find an existing version heading, or -1."""
    heading = f"## [{version}]"
    for index, line in enumerate(lines):
        if line.startswith(heading):
            return index
    return -1


def rotate_changelog(changelog: str, version: str, date_stamp: str) -> str:
    """Rotate the Unreleased section into the dated release section."""
    lines = re.split(r"\r?\n", changelog)
    unreleased_indexes = [index for index, line in enumerate(lines) if UNRELEASED_HEADING_PATTERN.match(line)]
    if len(unreleased_indexes) == 0:
        raise ValueError("CHANGELOG.md has no '## [Unreleased]' section to rotate.")
    if len(unreleased_indexes) > 1:
        raise ValueError("CHANGELOG.md carries more than one Unreleased section.")
    unreleased_index = unreleased_indexes[0]
    if duplicate_section_index(lines, version) != -1:
        raise ValueError(f"CHANGELOG.md already carries a '## [{version}]' section.")

    section_end = unreleased_index + 1
    while section_end < len(lines) and SECTION_HEADING_PATTERN.match(lines[section_end]) is None:
        section_end += 1
    body_lines = lines[unreleased_index + 1 : section_end]
    content_lines = [line for line in body_lines if line.strip() != "" and re.match(r"^###\s", line.strip()) is None]
    if len(content_lines) == 0:
        raise ValueError(
            "The '## [Unreleased]' section has no release notes; add entries before preparing a release."
        )
    while len(body_lines) > 0 and body_lines[-1].strip() == "":
        body_lines.pop()

    replacement = ["## [Unreleased]", "", f"## [{version}] - {date_stamp}", *body_lines, ""]
    return "\n".join([*lines[:unreleased_index], *replacement, *lines[section_end:]])


def utc_date_stamp() -> str:
    """Return today's UTC date, matching the original's ISO stamp."""
    return datetime.now(timezone.utc).strftime("%Y-%m-%d")


def main() -> None:
    """Bump the version, sync the context line, and rotate the changelog."""
    options = parse_arguments(sys.argv[1:])
    root = Path(options.root).resolve() if options.root is not None else REPOSITORY_ROOT
    files = read_repository_files(root)

    current_version = files.package_json.get("version")
    if not isinstance(current_version, str) or current_version.strip() == "":
        raise ValueError("'package.json' is missing a version field.")
    target_version = resolve_target_version(options, current_version)
    if target_version == current_version:
        raise ValueError(
            f"Explicit version '{target_version}' equals the current version; a release must change the version."
        )

    updated_package_json = patch_package_version(files.package_raw, target_version)
    updated_context = sync_context_version(files.context, target_version)
    updated_changelog = rotate_changelog(files.changelog, target_version, utc_date_stamp())

    changed_files = ["package.json", ".llm/context.md", "CHANGELOG.md"]
    if options.dry_run:
        print(f"dry-run: would bump {current_version} -> {target_version}")
        print(f"dry-run: would update {', '.join(changed_files)}")
    else:
        files.package_path.write_bytes(updated_package_json.encode("utf-8"))
        files.context_path.write_bytes(updated_context.encode("utf-8"))
        files.changelog_path.write_bytes(updated_changelog.encode("utf-8"))
        print(f"Bumped {current_version} -> {target_version}.")
        print(f"Updated {', '.join(changed_files)}.")
    print(f"previous-version: {current_version}")
    print(f"next-version: {target_version}")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
