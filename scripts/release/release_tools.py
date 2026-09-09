#!/usr/bin/env python3
"""Dependency-free helpers used by the package release workflows.

The Unity package builder intentionally operates on committed source and .meta
files only. It does not invoke Unity or depend on a Unity installation.
"""

from __future__ import annotations

import argparse
import datetime as dt
import gzip
import hashlib
import io
import json
import re
import tarfile
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence


SEMVER = re.compile(
    r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
    r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
    r"(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"
)
GUID = re.compile(r"^[0-9a-fA-F]{32}$")
GUID_LINE = re.compile(r"(?m)^guid:\s*([^\s#]+)\s*$")
UNRELEASED = re.compile(r"(?m)^## \[Unreleased\][ \t]*\r?$")
RELEASE_HEADER = re.compile(
    r"(?m)^## \[([^\]]+)\] - (\d{4}-\d{2}-\d{2})[ \t]*\r?$"
)
LINK_REFERENCE = re.compile(r"(?m)^\[([^\]]+)\]:[ \t]*(\S+)[ \t]*\r?$")
CHANGELOG_REPOSITORY_URL = "https://github.com/wallstop/DataVisualizer"
DEFAULT_INCLUDES = ("Editor", "Runtime", "Tests")


class ReleaseError(ValueError):
    """Raised when release input or generated artifacts are invalid."""


@dataclass(frozen=True)
class PackageEntry:
    guid: str
    pathname: str
    metadata: bytes
    asset: bytes | None


def parse_version(version: str) -> tuple[int, int, int, str | None, str | None]:
    match = SEMVER.fullmatch(version)
    if not match:
        raise ReleaseError(f"Invalid semantic version: {version!r}")
    major, minor, patch, prerelease, build = match.groups()
    return int(major), int(minor), int(patch), prerelease, build


def bump_version(version: str, part: str) -> str:
    major, minor, patch, _, _ = parse_version(version)
    if part == "major":
        major, minor, patch = major + 1, 0, 0
    elif part == "minor":
        minor, patch = minor + 1, 0
    elif part == "patch":
        patch += 1
    else:
        raise ReleaseError(f"Unsupported bump kind: {part!r}")
    return f"{major}.{minor}.{patch}"


def compare_versions(left: str, right: str) -> int:
    left_parts = parse_version(left)
    right_parts = parse_version(right)
    for left_value, right_value in zip(left_parts[:3], right_parts[:3]):
        if left_value != right_value:
            return 1 if left_value > right_value else -1
    left_pre, right_pre = left_parts[3], right_parts[3]
    if left_pre is None or right_pre is None:
        if left_pre == right_pre:
            return 0
        return 1 if left_pre is None else -1
    left_identifiers, right_identifiers = left_pre.split("."), right_pre.split(".")
    for left_identifier, right_identifier in zip(left_identifiers, right_identifiers):
        if left_identifier == right_identifier:
            continue
        left_numeric, right_numeric = left_identifier.isdigit(), right_identifier.isdigit()
        if left_numeric and right_numeric:
            return 1 if int(left_identifier) > int(right_identifier) else -1
        if left_numeric != right_numeric:
            return -1 if left_numeric else 1
        return 1 if left_identifier > right_identifier else -1
    if len(left_identifiers) == len(right_identifiers):
        return 0
    return 1 if len(left_identifiers) > len(right_identifiers) else -1


def resolve_version(current: str, bump: str | None, explicit: str | None) -> str:
    if bool(bump) == bool(explicit):
        raise ReleaseError("Specify exactly one of bump or explicit version")
    candidate = explicit if explicit else bump_version(current, bump)
    parse_version(candidate)
    if compare_versions(candidate, current) <= 0:
        raise ReleaseError("Release version must be greater than the current package version")
    return candidate


def npm_dist_tag(version: str) -> str:
    _, _, _, prerelease, _ = parse_version(version)
    if prerelease and prerelease.split(".", 1)[0].lower() in {"rc", "alpha", "beta", "preview"}:
        return "next"
    return "latest"


def load_package(root: Path) -> dict:
    package_path = root / "package.json"
    try:
        package = json.loads(package_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ReleaseError(f"Cannot read {package_path}: {error}") from error
    if not isinstance(package, dict) or not isinstance(package.get("version"), str):
        raise ReleaseError("package.json must contain a string version")
    parse_version(package["version"])
    return package


def sync_context(text: str, version: str) -> str:
    pattern = re.compile(r"(?m)^(?P<prefix>- \*\*Version\*\*:\s*)(?P<value>[^\r\n]+)")
    matches = list(pattern.finditer(text))
    if len(matches) != 1:
        raise ReleaseError(".llm/context.md must contain exactly one package version line")
    match = matches[0]
    return text[: match.start()] + f"- **Version**: {version}" + text[match.end() :]


def rotate_changelog(text: str, version: str, release_date: str) -> str:
    try:
        date = dt.date.fromisoformat(release_date)
    except ValueError as error:
        raise ReleaseError(f"Invalid release date: {release_date!r}") from error
    release_matches = list(RELEASE_HEADER.finditer(text))
    if any(match.group(1) == version for match in release_matches):
        raise ReleaseError(f"Changelog already contains {version}")
    unreleased = UNRELEASED.search(text)
    if unreleased is None:
        raise ReleaseError("CHANGELOG.md must contain an ## [Unreleased] section")
    next_header = re.search(r"(?m)^## \[", text[unreleased.end() :])
    body_end = unreleased.end() + next_header.start() if next_header else len(text)
    body = text[unreleased.end() : body_end].strip()
    if not body:
        raise ReleaseError("The Unreleased changelog section must contain changes")
    rendered_date = date.isoformat()
    newline = "\r\n" if "\r\n" in text else "\n"
    replacement = (
        f"## [Unreleased]{newline}{newline}"
        f"## [{version}] - {rendered_date}{newline}{newline}"
        f"{body}{newline}{newline}"
    )
    rotated = text[: unreleased.start()] + replacement + text[body_end:].lstrip("\r\n")
    previous_version = release_matches[0].group(1) if release_matches else None
    return _update_changelog_links(rotated, version, previous_version)


def _update_changelog_links(text: str, version: str, previous_version: str | None) -> str:
    """Keep Keep-a-Changelog comparison references aligned with a new release."""
    newline = "\r\n" if "\r\n" in text else "\n"
    release_url = (
        f"{CHANGELOG_REPOSITORY_URL}/compare/v{previous_version}...v{version}"
        if previous_version
        else f"{CHANGELOG_REPOSITORY_URL}/releases/tag/v{version}"
    )
    desired = {
        "Unreleased": f"{CHANGELOG_REPOSITORY_URL}/compare/v{version}...HEAD",
        version: release_url,
    }
    seen: set[str] = set()

    def replace_reference(match: re.Match[str]) -> str:
        label = match.group(1)
        if label not in desired:
            return match.group(0)
        seen.add(label)
        line_ending = "\r" if match.group(0).endswith("\r") else ""
        return f"[{label}]: {desired[label]}{line_ending}"

    updated = LINK_REFERENCE.sub(replace_reference, text)
    missing = [f"[{label}]: {desired[label]}" for label in desired if label not in seen]
    if missing:
        updated = updated.rstrip("\r\n") + newline * 2 + newline.join(missing) + newline
    return updated


def changelog_contains_version(text: str, version: str) -> bool:
    return any(match.group(1) == version for match in RELEASE_HEADER.finditer(text))


def changelog_section(text: str, version: str) -> str:
    for match in RELEASE_HEADER.finditer(text):
        if match.group(1) != version:
            continue
        section_end = re.search(r"(?m)^## ", text[match.end() :])
        end = match.end() + section_end.start() if section_end else len(text)
        section = text[match.start() : end].strip()
        return re.sub(r"(?m)^\[[^\]]+\]:\s+\S+\s*$", "", section).strip()
    raise ReleaseError(f"CHANGELOG.md has no dated section for {version}")


def prepare_release(
    root: Path,
    bump: str | None,
    explicit: str | None,
    release_date: str,
    dry_run: bool,
) -> dict:
    package_path = root / "package.json"
    context_path = root / ".llm" / "context.md"
    changelog_path = root / "CHANGELOG.md"
    package = load_package(root)
    current = package["version"]
    version = resolve_version(current, bump, explicit)
    context = context_path.read_text(encoding="utf-8")
    changelog = changelog_path.read_text(encoding="utf-8")
    new_context = sync_context(context, version)
    new_changelog = rotate_changelog(changelog, version, release_date)
    package["version"] = version
    package_text = json.dumps(package, indent=2, ensure_ascii=False) + "\n"
    result = {"current": current, "version": version, "date": release_date, "dry_run": dry_run}
    if not dry_run:
        package_path.write_text(package_text, encoding="utf-8")
        context_path.write_text(new_context, encoding="utf-8")
        changelog_path.write_text(new_changelog, encoding="utf-8")
    return result


def validate_release_tree(root: Path, expected_version: str | None = None) -> dict:
    package = load_package(root)
    version = package["version"]
    if expected_version and version != expected_version:
        raise ReleaseError(f"package.json has {version}, expected {expected_version}")
    changelog = (root / "CHANGELOG.md").read_text(encoding="utf-8")
    if not changelog_contains_version(changelog, version):
        raise ReleaseError(f"CHANGELOG.md has no dated section for {version}")
    context = (root / ".llm" / "context.md").read_text(encoding="utf-8")
    if sync_context(context, version) != context:
        raise ReleaseError(".llm/context.md version is not synchronized with package.json")
    return {"name": package.get("name"), "version": version, "changelog": True}


def _safe_relative(path: str) -> str:
    normalized = path.replace("\\", "/")
    parts = normalized.split("/")
    if not normalized or normalized.startswith("/") or "" in parts or any(
        part in (".", "..") for part in parts
    ):
        raise ReleaseError(f"Unsafe asset path: {path!r}")
    return normalized


def _guid_from_meta(metadata: bytes, path: Path) -> str:
    try:
        text = metadata.decode("utf-8")
    except UnicodeDecodeError as error:
        raise ReleaseError(f"Metadata is not UTF-8: {path}") from error
    matches = GUID_LINE.findall(text)
    if len(matches) != 1 or not GUID.fullmatch(matches[0]):
        raise ReleaseError(f"Metadata must contain one valid GUID: {path}")
    return matches[0].lower()


def _meta_path(source: Path) -> Path:
    return source.with_name(source.name + ".meta")


def source_entries(root: Path, includes: Sequence[str]) -> list[PackageEntry]:
    entries: list[PackageEntry] = []
    seen_guids: dict[str, str] = {}
    seen_paths: set[str] = set()
    for include in includes:
        source_root = root / include
        if not source_root.is_dir() or source_root.is_symlink():
            raise ReleaseError(f"Shipped source root is missing or unsafe: {include}")
        paths = [source_root, *sorted(source_root.rglob("*"), key=lambda item: item.as_posix())]
        for source in paths:
            if source.is_symlink():
                raise ReleaseError(f"Symlinks are not allowed in shipped payload: {source}")
            if source.name.endswith(".meta"):
                continue
            if (
                source.is_dir()
                and source != source_root
                and not _meta_path(source).is_file()
                and not any(source.iterdir())
            ):
                # Empty directories can be left behind by a local move/delete. They
                # are not package payload and do not exist in a clean checkout.
                continue
            relative = _safe_relative(source.relative_to(root).as_posix())
            metadata_path = _meta_path(source)
            if not metadata_path.is_file() or metadata_path.is_symlink():
                raise ReleaseError(f"Missing committed metadata for {relative}")
            metadata = metadata_path.read_bytes()
            guid = _guid_from_meta(metadata, metadata_path)
            if guid in seen_guids:
                raise ReleaseError(f"Duplicate GUID {guid}: {seen_guids[guid]} and {relative}")
            if relative in seen_paths:
                raise ReleaseError(f"Duplicate shipped path: {relative}")
            seen_guids[guid] = relative
            seen_paths.add(relative)
            asset = None if source.is_dir() else source.read_bytes()
            entries.append(PackageEntry(guid, relative, metadata, asset))
    return sorted(entries, key=lambda entry: (entry.guid, entry.pathname))


def _tar_bytes(entries: Iterable[PackageEntry]) -> bytes:
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.USTAR_FORMAT) as archive:
        for entry in entries:
            for name, content in (("pathname", entry.pathname.encode("utf-8")), ("asset.meta", entry.metadata)):
                info = tarfile.TarInfo(f"{entry.guid}/{name}")
                info.size = len(content)
                info.mode = 0o644
                info.mtime = 0
                info.uid = info.gid = 0
                info.uname = info.gname = ""
                archive.addfile(info, io.BytesIO(content))
            if entry.asset is not None:
                info = tarfile.TarInfo(f"{entry.guid}/asset")
                info.size = len(entry.asset)
                info.mode = 0o644
                info.mtime = 0
                info.uid = info.gid = 0
                info.uname = info.gname = ""
                archive.addfile(info, io.BytesIO(entry.asset))
    compressed = io.BytesIO()
    with gzip.GzipFile(fileobj=compressed, mode="wb", filename="", mtime=0) as output:
        output.write(raw.getvalue())
    return compressed.getvalue()


def write_checksum(path: Path, data: bytes) -> Path:
    checksum_path = path.with_name(path.name + ".sha256")
    checksum_path.write_text(f"{hashlib.sha256(data).hexdigest()}  {path.name}\n", encoding="ascii")
    return checksum_path


def build_unitypackage(root: Path, output: Path, includes: Sequence[str]) -> dict:
    entries = source_entries(root, includes)
    if not entries:
        raise ReleaseError("Cannot build an empty Unity package")
    data = _tar_bytes(entries)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(data)
    checksum = write_checksum(output, data)
    return {"archive": str(output), "checksum": str(checksum), "entries": len(entries), "bytes": len(data)}


def _read_archive(path: Path) -> dict[str, dict[str, bytes]]:
    groups: dict[str, dict[str, bytes]] = {}
    with gzip.open(path, "rb") as compressed:
        with tarfile.open(fileobj=compressed, mode="r:") as archive:
            for member in archive:
                if not member.isfile():
                    raise ReleaseError(f"Archive member is not a regular file: {member.name}")
                parts = member.name.split("/")
                if len(parts) != 2 or not GUID.fullmatch(parts[0]) or parts[1] not in {
                    "pathname",
                    "asset.meta",
                    "asset",
                }:
                    raise ReleaseError(f"Invalid Unity package member: {member.name}")
                group = groups.setdefault(parts[0].lower(), {})
                if parts[1] in group:
                    raise ReleaseError(f"Duplicate Unity package member: {member.name}")
                payload = archive.extractfile(member)
                if payload is None:
                    raise ReleaseError(f"Cannot read Unity package member: {member.name}")
                group[parts[1]] = payload.read()
    if not groups:
        raise ReleaseError("Unity package archive is empty")
    return groups


def validate_unitypackage(
    archive: Path,
    source_root: Path | None,
    includes: Sequence[str],
    checksum: Path | None,
) -> dict:
    data = archive.read_bytes()
    if checksum:
        expected = checksum.read_text(encoding="ascii").split()[0].lower()
        actual = hashlib.sha256(data).hexdigest()
        if expected != actual:
            raise ReleaseError(f"Checksum mismatch for {archive}")
    groups = _read_archive(archive)
    seen_paths: set[str] = set()
    for guid, members in groups.items():
        if set(members) - {"pathname", "asset.meta", "asset"}:
            raise ReleaseError(f"Unexpected members for GUID {guid}")
        if set(members) < {"pathname", "asset.meta"}:
            raise ReleaseError(f"GUID {guid} is missing pathname or asset.meta")
        try:
            pathname = _safe_relative(members["pathname"].decode("utf-8").strip())
        except UnicodeDecodeError as error:
            raise ReleaseError(f"Pathname for GUID {guid} is not UTF-8") from error
        if pathname in seen_paths:
            raise ReleaseError(f"Duplicate asset pathname: {pathname}")
        seen_paths.add(pathname)
        metadata_guid = _guid_from_meta(members["asset.meta"], Path(f"{guid}/asset.meta"))
        if metadata_guid != guid:
            raise ReleaseError(f"GUID mismatch for {pathname}: {metadata_guid} != {guid}")
    if source_root:
        expected = {entry.guid: entry for entry in source_entries(source_root, includes)}
        actual = {
            guid: PackageEntry(
                guid,
                members["pathname"].decode("utf-8").strip(),
                members["asset.meta"],
                members.get("asset"),
            )
            for guid, members in groups.items()
        }
        if set(actual) != set(expected):
            raise ReleaseError("Unity package GUID set does not match shipped source")
        for guid, entry in expected.items():
            received = actual[guid]
            if received.pathname != entry.pathname or received.metadata != entry.metadata:
                raise ReleaseError(f"Unity package metadata mismatch for {entry.pathname}")
            if (received.asset is None) != (entry.asset is None):
                raise ReleaseError(f"Unity package asset presence mismatch for {entry.pathname}")
            if received.asset is not None and received.asset != entry.asset:
                raise ReleaseError(f"Unity package asset mismatch for {entry.pathname}")
    return {"archive": str(archive), "entries": len(groups), "checksum": hashlib.sha256(data).hexdigest()}


def _includes(values: Sequence[str] | None) -> tuple[str, ...]:
    return tuple(values) if values else DEFAULT_INCLUDES


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    prepare = subparsers.add_parser("prepare")
    prepare.add_argument("--root", type=Path, default=Path("."))
    bump = prepare.add_mutually_exclusive_group(required=True)
    bump.add_argument("--bump", choices=("patch", "minor", "major"))
    bump.add_argument("--version")
    prepare.add_argument("--date", default=dt.date.today().isoformat())
    prepare.add_argument("--dry-run", action="store_true")

    tree = subparsers.add_parser("validate-release")
    tree.add_argument("--root", type=Path, default=Path("."))
    tree.add_argument("--version")

    section = subparsers.add_parser("changelog-section")
    section.add_argument("--root", type=Path, default=Path("."))
    section.add_argument("--version", required=True)

    build = subparsers.add_parser("build-unitypackage")
    build.add_argument("--root", type=Path, default=Path("."))
    build.add_argument("--output", type=Path, required=True)
    build.add_argument("--include", action="append")

    validate = subparsers.add_parser("validate-unitypackage")
    validate.add_argument("--archive", type=Path, required=True)
    validate.add_argument("--checksum", type=Path)
    validate.add_argument("--root", type=Path)
    validate.add_argument("--include", action="append")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    if args.command == "prepare":
        result = prepare_release(args.root, args.bump, args.version, args.date, args.dry_run)
    elif args.command == "validate-release":
        result = validate_release_tree(args.root, args.version)
    elif args.command == "changelog-section":
        result = {"section": changelog_section((args.root / "CHANGELOG.md").read_text(encoding="utf-8"), args.version)}
    elif args.command == "build-unitypackage":
        result = build_unitypackage(args.root, args.output, _includes(args.include))
    else:
        result = validate_unitypackage(
            args.archive,
            args.root,
            _includes(args.include),
            args.checksum,
        )
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ReleaseError as error:
        print(f"release_tools: error: {error}")
        raise SystemExit(2)
