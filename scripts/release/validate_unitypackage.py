#!/usr/bin/env python3
"""Standalone ``.unitypackage`` validator for com.wallstop-studios.data-visualizer.

Typed Python port of ``validate-unitypackage.mjs`` (issue #171): the same
archive and payload checks, messages, and report lines, driven by the
standard library.

Validates a built ``.unitypackage`` against the tracked,
``files``-allowlisted package payload with no Unity invocation, so a
locally built or cloud-built archive can be fail-closed before it is
attached to a release. The archive is parsed with standard ``tar``
tooling, independent of the tar writer in ``build_unitypackage.py``, so a
builder defect cannot mirror itself into the verification. The payload
rules are shared with the builder and ``verify_release.py`` via
``package_payload.py``.

Archive layout checks: the ``.sha256`` sidecar next to the archive must be
sha256sum formatted, name this archive, and carry its actual SHA-256;
every member must be ``<guid>/(pathname|asset|asset.meta)`` inside a
lowercase 32-hex GUID directory with no duplicates; every GUID directory
carries ``pathname`` + ``asset.meta``, plus ``asset`` for file entries
(folder entries, marked by a trailing-``/`` pathname, must not carry an
``asset``); every ``pathname`` is one ``\\n``-terminated safe relative path
under ``Packages/``.

Payload checks: the root must be a git work tree whose valid
``package.json`` allowlists at least one tracked file; the expected
entries are derived from tracked files only (fail-closed on unsafe paths,
missing or orphan ``.meta`` companions, and malformed or duplicate
committed GUIDs). The archive's staged paths must exactly cover the
expected payload, and every entry must match the repository: GUID
directory name equals the committed ``guid:`` field, and
``asset.meta``/``asset`` bytes equal the committed meta/tracked file.

Reports the package name, version, archive filename, SHA-256, and GUID
directory (entry) count. Gating a release directory's overall artifact set
(archive + sidecar + npm tarball) belongs to the release workflow slice.

Usage:
    python scripts/release/validate_unitypackage.py --archive <path> [--root <path>]
"""

from __future__ import annotations

import hashlib
import re
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Set

from package_payload import list_expected_payload_paths, read_package_json, verify_git_work_tree

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIRECTORY.parent.parent
META_SUFFIX = ".meta"
GUID_DIRECTORY_PATTERN = re.compile(r"^([0-9a-f]{32})/(pathname|asset|asset\.meta)$")
META_GUID_PATTERN = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)
CHECKSUM_LINE_PATTERN = re.compile(r"^([0-9a-fA-F]{64})[ \t]+\*?(.+)$")

USAGE = "\n".join(
    [
        "Validate a built .unitypackage against the tracked package payload.",
        "",
        "Options:",
        "  --archive <path>          The .unitypackage to validate (required).",
        "  --root <path>             Repository root to operate on (default: this repository).",
        "  --help                    Show this help.",
    ]
)


@dataclass(frozen=True)
class ValidateOptions:
    """The parsed validate command line."""

    archive: str
    root: Optional[str]


def parse_arguments(argv: Sequence[str]) -> ValidateOptions:
    """Parse the command line exactly like the original, failing closed."""
    archive: Optional[str] = None
    root: Optional[str] = None
    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument == "--archive":
            archive = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--root":
            root = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--help":
            print(USAGE)
            sys.exit(0)
        else:
            raise ValueError(f"Unknown argument '{argument}'.\n\n{USAGE}")
    if archive is None:
        raise ValueError(f"--archive <path> is required.\n\n{USAGE}")
    return ValidateOptions(archive=archive, root=root)


def read_value(argv: Sequence[str], index: int, flag: str) -> str:
    """Read the value after a flag, failing closed when it is missing."""
    if index >= len(argv):
        raise ValueError(f"Missing value for {flag}.")
    return argv[index]


def compare_ordinal(left: str, right: str) -> int:
    """Compare two strings by code unit, like the original's ordinal sort."""
    if left < right:
        return -1
    if left > right:
        return 1
    return 0


def is_unsafe_payload_path(relative_path: str) -> bool:
    """Require a safe relative path with no backslashes or dot segments."""
    if relative_path.startswith("/") or "\\" in relative_path:
        return True
    return any(segment in ("", ".", "..") for segment in relative_path.split("/"))


def is_unsafe_staged_path(staged_path: str) -> bool:
    """Require a staged path under Packages/, tolerating one folder slash."""
    if not staged_path.startswith("Packages/"):
        return True
    # Folder entries legitimately end with exactly one trailing '/'.
    without_trailing_slash = staged_path[:-1] if staged_path.endswith("/") else staged_path
    return is_unsafe_payload_path(without_trailing_slash)


def run_tar(arguments: Sequence[str], purpose: str, working_directory: Path) -> str:
    """Run tar from the archive's directory under a colon-free relative name.

    GNU tar parses the first colon in the -f archive name as a remote host
    spec, so a Windows drive path fails with 'Cannot connect to C: resolve
    failed'. Every invocation therefore runs from the archive's directory
    with a colon-free relative name, portable across the GNU and BSD tar
    builds.
    """
    try:
        completed = subprocess.run(
            ["tar", *arguments],
            cwd=str(working_directory),
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
        raise ValueError(f"Cannot {purpose}: {detail or f'tar exited with {completed.returncode}'}")
    return completed.stdout or ""


def verify_checksum(archive_path: Path, checksum_file: Path, archive_sha256: str) -> None:
    """Require a sha256sum-formatted sidecar naming this archive and hash."""
    if not checksum_file.exists():
        raise ValueError(
            f"Cannot read the required checksum sidecar '{checksum_file}'; a release "
            "archive must ship with its .sha256 checksum."
        )
    lines = checksum_file.read_text(encoding="utf-8").strip().splitlines()
    if len(lines) != 1:
        raise ValueError(f"Checksum file '{checksum_file}' must carry exactly one sha256sum line.")
    match = CHECKSUM_LINE_PATTERN.match(lines[0])
    if match is None:
        raise ValueError(
            f"Checksum file '{checksum_file}' is not in sha256sum format "
            "(<64-hex hash> <whitespace> <filename>)."
        )
    if match.group(1).lower() != archive_sha256:
        raise ValueError(
            f"Checksum file '{checksum_file}' carries '{match.group(1)}' but the archive hashes to "
            f"'{archive_sha256}'; the artifact is corrupt or the sidecar is stale."
        )
    recorded_name = match.group(2).strip()
    archive_name = archive_path.name
    if recorded_name != archive_name:
        raise ValueError(
            f"Checksum file '{checksum_file}' names '{recorded_name}' but the validated archive "
            f"is '{archive_name}'."
        )


def list_archive_members(archive_path: Path) -> List[str]:
    """List every member name the archive carries."""
    listing = run_tar(["-tzf", archive_path.name], "list the archive", archive_path.parent)
    return [entry.strip() for entry in listing.splitlines() if entry.strip() != ""]


def group_members(members: Sequence[str]) -> Dict[str, Dict[str, bool]]:
    """Group members by GUID directory, failing closed on shape errors."""
    grouped: Dict[str, Dict[str, bool]] = {}
    seen: Set[str] = set()
    for name in members:
        if name in seen:
            raise ValueError(
                f"The archive carries the duplicate member '{name}'; duplicate members would "
                "stage the same content twice."
            )
        seen.add(name)
        match = GUID_DIRECTORY_PATTERN.match(name)
        if match is None:
            raise ValueError(
                f"The archive carries the unexpected member '{name}'; every member must be "
                "<guid>/(pathname|asset|asset.meta) inside a lowercase 32-hex GUID directory."
            )
        guid = match.group(1)
        group = grouped.setdefault(guid, {})
        group[match.group(2)] = True
    if len(grouped) == 0:
        raise ValueError(
            "The archive carries no GUID-directory entries; a .unitypackage must stage at "
            "least one asset."
        )
    return grouped


def read_member(extract_directory: Path, name: str) -> bytes:
    """Read one extracted member, failing closed if it vanished."""
    path = extract_directory / name
    try:
        return path.read_bytes()
    except OSError:
        raise ValueError(f"The archive member '{name}' vanished between listing and extraction.") from None


def decode_staged_path(guid: str, pathname_bytes: bytes) -> str:
    """Decode one pathname member, requiring one safe Packages/-rooted path."""
    text = pathname_bytes.decode("utf-8")
    if not text.endswith("\n") or "\n" in text[:-1] or "\r" in text:
        raise ValueError(f"The pathname member for guid '{guid}' must be exactly one \\n-terminated staged path.")
    staged_path = text[:-1]
    if staged_path == "" or is_unsafe_staged_path(staged_path):
        raise ValueError(
            f"The pathname member for guid '{guid}' stages the unsafe path '{staged_path}'; "
            "staged paths must be safe relative paths under Packages/."
        )
    return staged_path


class ArchiveEntry:
    """One archive entry: its GUID, staged path, and folder flag."""

    def __init__(self, guid: str, staged_path: str, is_folder: bool) -> None:
        self.guid = guid
        self.staged_path = staged_path
        self.is_folder = is_folder


class ExpectedEntry:
    """One expected entry derived from the tracked payload."""

    def __init__(self, staged_path: str, asset_path: str, meta_path: str) -> None:
        self.staged_path = staged_path
        self.asset_path = asset_path
        self.meta_path = meta_path
        self.guid: Optional[str] = None
        self.meta_content: Optional[bytes] = None


def resolve_archive_entries(grouped: Dict[str, Dict[str, bool]], extract_directory: Path) -> List[ArchiveEntry]:
    """Turn grouped members into entries, failing closed on layout errors."""
    entries: List[ArchiveEntry] = []
    for guid, members in grouped.items():
        if not members.get("pathname", False) or not members.get("asset.meta", False):
            raise ValueError(
                f"GUID directory '{guid}' is incomplete: every entry needs 'pathname' and "
                "'asset.meta' members."
            )
        staged_path = decode_staged_path(guid, read_member(extract_directory, f"{guid}/pathname"))
        is_folder = staged_path.endswith("/")
        if is_folder and members.get("asset", False):
            raise ValueError(
                f"GUID directory '{guid}' stages the folder '{staged_path}' but carries an "
                "'asset' member; folder entries carry pathname + asset.meta only."
            )
        if not is_folder and not members.get("asset", False):
            raise ValueError(
                f"GUID directory '{guid}' stages the file '{staged_path}' but is missing its "
                "'asset' member."
            )
        entries.append(ArchiveEntry(guid, staged_path, is_folder))
    return sorted(entries, key=lambda entry: entry.staged_path)


def resolve_expected_entries(root: Path, package_json: Dict[str, Any]) -> List[ExpectedEntry]:
    """Derive the expected archive entries from the tracked payload."""
    payload_paths = list_expected_payload_paths(root, package_json)
    package_root_prefix = f"Packages/{package_json['name']}"
    unsafe = [path for path in payload_paths if is_unsafe_payload_path(path)]
    if len(unsafe) > 0:
        raise ValueError(
            f"The payload carries {len(unsafe)} unsafe path(s) that cannot be validated as "
            "staged .unitypackage entries:\n" + "\n".join(unsafe)
        )
    payload = set(payload_paths)
    entries: List[ExpectedEntry] = []
    missing_meta: List[str] = []
    for path in payload_paths:
        if path.endswith(META_SUFFIX):
            continue
        meta_path = f"{path}{META_SUFFIX}"
        if meta_path not in payload:
            missing_meta.append(path)
            continue
        entries.append(ExpectedEntry(f"{package_root_prefix}/{path}", path, meta_path))
    if len(missing_meta) > 0:
        raise ValueError(
            f"{len(missing_meta)} payload file(s) have no committed '.meta' companion, so the "
            "archive cannot carry them with their importer settings:\n" + "\n".join(sorted(missing_meta))
        )
    folders: List[ExpectedEntry] = []
    orphan_metas: List[str] = []
    for path in payload_paths:
        if not path.endswith(META_SUFFIX):
            continue
        target = path[: -len(META_SUFFIX)]
        if target in payload:
            continue
        if any(candidate.startswith(f"{target}/") for candidate in payload_paths):
            folders.append(ExpectedEntry(f"{package_root_prefix}/{target}/", path, path))
            continue
        orphan_metas.append(path)
    if len(orphan_metas) > 0:
        raise ValueError(
            f"{len(orphan_metas)} payload '.meta' file(s) target neither a tracked file nor a "
            "directory with tracked payload children:\n" + "\n".join(sorted(orphan_metas))
        )
    entries.extend(folders)
    guid_owners: Dict[str, str] = {}
    for entry in entries:
        meta_content = (root / entry.meta_path).read_bytes()
        match = META_GUID_PATTERN.search(meta_content.decode("utf-8"))
        if match is None:
            raise ValueError(
                f"'{entry.meta_path}' carries no well-formed 'guid:' field, so its archive entry "
                "cannot be checked against the committed asset identity."
            )
        guid = match.group(1).lower()
        owner = guid_owners.get(guid)
        if owner is not None:
            raise ValueError(
                f"'{entry.meta_path}' reuses guid '{guid}', already carried by '{owner}'; "
                "duplicate committed identities cannot produce a valid import."
            )
        guid_owners[guid] = entry.meta_path
        entry.guid = guid
        entry.meta_content = meta_content
    return entries


def compare_archive_with_expected(
    archive_entries: Sequence[ArchiveEntry],
    expected_entries: Sequence[ExpectedEntry],
    root: Path,
    extract_directory: Path,
) -> None:
    """Require the archive and the tracked payload to agree exactly."""
    expected = {entry.staged_path: entry for entry in expected_entries}
    claimed: Dict[str, ArchiveEntry] = {}
    for archive_entry in archive_entries:
        previous = claimed.get(archive_entry.staged_path)
        if previous is not None:
            raise ValueError(
                f"GUID directories '{previous.guid}' and '{archive_entry.guid}' both stage "
                f"'{archive_entry.staged_path}'; duplicate assets would corrupt the import."
            )
        claimed[archive_entry.staged_path] = archive_entry
    unexpected = [entry.staged_path for entry in archive_entries if entry.staged_path not in expected]
    if len(unexpected) > 0:
        raise ValueError(
            f"The archive stages {len(unexpected)} path(s) outside the tracked payload; "
            "extend the allowlist deliberately or rebuild from a clean tree:\n" + "\n".join(unexpected)
        )
    missing = sorted(path for path in expected.keys() if path not in claimed)
    if len(missing) > 0:
        raise ValueError(
            f"The archive is missing {len(missing)} tracked payload path(s); the payload "
            "would silently shrink:\n" + "\n".join(missing)
        )
    mismatches: List[str] = []
    for archive_entry in archive_entries:
        expected_entry = expected[archive_entry.staged_path]
        if archive_entry.guid != expected_entry.guid:
            mismatches.append(
                f"'{archive_entry.staged_path}' sits in guid directory '{archive_entry.guid}' but "
                f"the committed meta '{expected_entry.meta_path}' carries '{expected_entry.guid}'."
            )
            continue
        archived_meta = read_member(extract_directory, f"{archive_entry.guid}/asset.meta")
        if archived_meta != expected_entry.meta_content:
            mismatches.append(
                f"'{archive_entry.staged_path}': asset.meta does not byte-match the committed "
                f"'{expected_entry.meta_path}'."
            )
        if archive_entry.is_folder:
            continue
        archived_asset = read_member(extract_directory, f"{archive_entry.guid}/asset")
        if archived_asset != (root / expected_entry.asset_path).read_bytes():
            mismatches.append(
                f"'{archive_entry.staged_path}': asset does not byte-match the tracked file "
                f"'{expected_entry.asset_path}'."
            )
    if len(mismatches) > 0:
        raise ValueError(
            f"The archive disagrees with the repository in {len(mismatches)} place(s):\n"
            + "\n".join(mismatches)
        )


def main() -> None:
    """Validate one archive against the tracked payload, reporting the result."""
    options = parse_arguments(sys.argv[1:])
    root = Path(options.root).resolve() if options.root is not None else REPOSITORY_ROOT
    archive_path = Path(options.archive).resolve()
    try:
        archive_stat = archive_path.stat()
    except OSError:
        raise ValueError(f"Cannot read a non-empty archive file at '{archive_path}'.") from None
    if not archive_path.is_file() or archive_stat.st_size == 0:
        raise ValueError(f"Cannot read a non-empty archive file at '{archive_path}'.")

    gzip_buffer = archive_path.read_bytes()
    archive_sha256 = hashlib.sha256(gzip_buffer).hexdigest()
    verify_checksum(archive_path, Path(f"{archive_path}.sha256"), archive_sha256)

    grouped = group_members(list_archive_members(archive_path))
    extract_directory = Path(tempfile.mkdtemp(prefix="unitypackage-validate-"))
    try:
        run_tar(["-xzf", archive_path.name, "-C", str(extract_directory)], "extract the archive", archive_path.parent)
        archive_entries = resolve_archive_entries(grouped, extract_directory)

        verify_git_work_tree(root)
        package_json = read_package_json(root)
        expected_entries = resolve_expected_entries(root, package_json)
        compare_archive_with_expected(archive_entries, expected_entries, root, extract_directory)

        print(f"pkg: {package_json['name']}")
        print(f"version: {package_json['version']}")
        print(f"package_file: {archive_path.name}")
        print(f"sha256: {archive_sha256}")
        print(f"entries: {len(archive_entries)}")
    finally:
        shutil.rmtree(extract_directory, ignore_errors=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
