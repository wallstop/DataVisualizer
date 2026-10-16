#!/usr/bin/env python3
"""Deterministic ``.unitypackage`` builder for com.wallstop-studios.data-visualizer.

Typed Python port of ``build-unitypackage.mjs`` (issue #171): the same
archive layout, fail-closed guards, deterministic staging, and console
lines, driven by the standard library.

Builds the release ``.unitypackage`` and a ``.sha256`` checksum file from
the tracked, ``files``-allowlisted package payload with no Unity
invocation, so the archive can be assembled locally or in cloud CI. The
payload rules are shared with ``verify_release.py`` via
``package_payload.py``, so the npm tarball and the ``.unitypackage``
always carry the same shipped set.

Archive layout (standard ``.unitypackage`` GUID directories, staged under
``Packages/<package name>/``):
    - Every payload file that is not a ``.meta`` yields ``<guid>/pathname``,
      ``<guid>/asset``, and ``<guid>/asset.meta``, where ``<guid>`` is the
      ``guid:`` field of the committed ``.meta`` and ``asset.meta`` carries
      the full committed ``.meta`` content. This matches Unity's own
      exports, so an import keeps the committed asset identities instead of
      reassigning GUIDs.
    - Every payload ``.meta`` folds into its target's ``asset.meta``; it
      never becomes a separate GUID entry.
    - A ``.meta`` whose target is a tracked directory (e.g. ``Editor.meta``)
      yields ``<guid>/pathname`` with a trailing ``/`` plus
      ``<guid>/asset.meta`` and no ``asset`` member, matching Unity's own
      folder exports.

Fail-closed guards:
    - The tree must be a git work tree; the payload is derived from tracked
      files only, so the builder always sees committed content.
    - ``package.json`` must carry a name, a supported semver version, and a
      non-empty ``files`` allowlist selecting at least one tracked file.
    - Every payload file must have its committed ``.meta`` companion, and
      every payload ``.meta`` must target a tracked file or directory.
      Missing and orphan ``.meta`` files both fail.
    - Every used ``.meta`` must carry a well-formed ``guid:`` field, and no
      two entries may share one GUID; malformed or duplicate identities
      fail.
    - Payload paths must be safe relative paths (no absolute paths, no
      backslashes, no ``.``/``..``/empty segments).

Determinism: GUID directory names come from the committed ``.meta`` files,
members are emitted in sorted staged-path order, tar headers use zeroed
ownership and timestamps, and the gzip stream carries no timestamp, so two
builds of the same tree are byte-identical. The archive carries only
GUID-directory members, so staged path length never approaches the ustar
name field.

Usage:
    python scripts/release/build_unitypackage.py [--root <path>] [--output <dir>]
"""

from __future__ import annotations

import gzip
import hashlib
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

from package_payload import list_expected_payload_paths, read_package_json, verify_git_work_tree

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIRECTORY.parent.parent
META_SUFFIX = ".meta"

META_GUID_PATTERN = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)

USAGE = "\n".join(
    [
        "Build the deterministic release .unitypackage and its .sha256 checksum.",
        "",
        "Options:",
        "  --root <path>     Repository root to operate on (default: this repository).",
        "  --output <dir>    Output directory (default: <root>/dist).",
        "  --help            Show this help.",
    ]
)


@dataclass(frozen=True)
class BuildOptions:
    """The parsed build command line."""

    root: Optional[str]
    output: Optional[str]


def parse_arguments(argv: Sequence[str]) -> BuildOptions:
    """Parse the command line exactly like the original, failing closed."""
    root: Optional[str] = None
    output: Optional[str] = None
    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument == "--root":
            root = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--output":
            output = read_value(argv, index + 1, argument)
            index += 2
        elif argument == "--help":
            print(USAGE)
            sys.exit(0)
        else:
            raise ValueError(f"Unknown argument '{argument}'.\n\n{USAGE}")
    return BuildOptions(root=root, output=output)


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


class StagedEntry:
    """One staged archive entry: a file or a tracked directory."""

    def __init__(self, staged_path: str, meta_path: str, asset_path: Optional[str]) -> None:
        self.staged_path = staged_path
        self.meta_path = meta_path
        self.asset_path = asset_path


def collect_archive_entries(root: Path, payload_paths: Sequence[str], package_root_prefix: str) -> List[StagedEntry]:
    """Stage every payload file and directory meta, failing closed on gaps."""
    unsafe = [path for path in payload_paths if is_unsafe_payload_path(path)]
    if len(unsafe) > 0:
        raise ValueError(
            f"The payload carries {len(unsafe)} unsafe path(s) that cannot be staged "
            "into a .unitypackage:\n" + "\n".join(unsafe)
        )
    payload = set(payload_paths)
    files: List[StagedEntry] = []
    missing_meta: List[str] = []
    for path in payload_paths:
        if path.endswith(META_SUFFIX):
            continue
        meta_path = f"{path}{META_SUFFIX}"
        if meta_path not in payload:
            missing_meta.append(path)
            continue
        files.append(StagedEntry(f"{package_root_prefix}/{path}", meta_path, path))
    if len(missing_meta) > 0:
        raise ValueError(
            f"{len(missing_meta)} payload file(s) have no committed '.meta' companion; a "
            ".unitypackage cannot carry them without their importer settings:\n"
            + "\n".join(sorted(missing_meta))
        )
    folders: List[StagedEntry] = []
    orphan_metas: List[str] = []
    for path in payload_paths:
        if not path.endswith(META_SUFFIX):
            continue
        target = path[: -len(META_SUFFIX)]
        if target in payload:
            continue
        if any(candidate.startswith(f"{target}/") for candidate in payload_paths):
            folders.append(StagedEntry(f"{package_root_prefix}/{target}/", path, None))
            continue
        orphan_metas.append(path)
    if len(orphan_metas) > 0:
        raise ValueError(
            f"{len(orphan_metas)} payload '.meta' file(s) target neither a tracked file nor a "
            "directory with tracked payload children:\n" + "\n".join(sorted(orphan_metas))
        )
    return sorted([*files, *folders], key=lambda entry: entry.staged_path)


def resolve_meta_guid(meta_path: str, meta_content: bytes) -> str:
    """Extract the lowercase GUID a staged entry is named after."""
    match = META_GUID_PATTERN.search(meta_content.decode("utf-8"))
    if match is None:
        raise ValueError(
            f"'{meta_path}' carries no well-formed 'guid:' field, so its .unitypackage entry "
            "cannot be named with the committed asset identity."
        )
    return match.group(1).lower()


def resolve_members(root: Path, entries: Sequence[StagedEntry]) -> List[Tuple[str, bytes]]:
    """Build every member: the staged path marker, the asset, and its meta."""
    members: List[Tuple[str, bytes]] = []
    guid_owners: Dict[str, str] = {}
    for entry in entries:
        meta_content = (root / entry.meta_path).read_bytes()
        guid = resolve_meta_guid(entry.meta_path, meta_content)
        owner = guid_owners.get(guid)
        if owner is not None:
            raise ValueError(
                f"'{entry.meta_path}' reuses guid '{guid}', already carried by '{owner}'; "
                "duplicate identities would corrupt the import."
            )
        guid_owners[guid] = entry.meta_path
        members.append((f"{guid}/pathname", f"{entry.staged_path}\n".encode("utf-8")))
        if not entry.staged_path.endswith("/"):
            members.append((f"{guid}/asset", (root / str(entry.asset_path)).read_bytes()))
        members.append((f"{guid}/asset.meta", meta_content))
    return members


def write_octal(value: int, length: int) -> bytes:
    """Format one zero-padded octal field ending in NUL, like the original."""
    return f"{value:o}".zfill(length - 1).encode("ascii") + b"\0"


def write_tar_header(name: str, size: int) -> bytes:
    """Build one ustar header with zeroed ownership and timestamps."""
    if len(name.encode("utf-8")) > 100:
        raise ValueError(f"Staged member '{name}' does not fit a ustar header.")
    header = bytearray(512)
    name_bytes = name.encode("utf-8")
    header[0 : len(name_bytes)] = name_bytes
    header[100:108] = write_octal(0o644, 8)
    header[108:116] = write_octal(0, 8)
    header[116:124] = write_octal(0, 8)
    header[124:136] = write_octal(size, 12)
    header[136:148] = write_octal(0, 12)
    header[148:156] = b"        "
    header[156:157] = b"0"
    header[257:263] = b"ustar\0"
    header[263:265] = b"00"
    header[229:237] = b"0000000\0"
    header[237:245] = b"0000000\0"
    checksum = sum(header)
    header[148:156] = f"{checksum:o}".zfill(6).encode("ascii") + b"\0 "
    return bytes(header)


def write_tar(members: Sequence[Tuple[str, bytes]]) -> bytes:
    """Serialize members into one tar stream with the two-block trailer."""
    chunks: List[bytes] = []
    for name, data in members:
        chunks.append(write_tar_header(name, len(data)))
        chunks.append(data)
        padding = (512 - (len(data) % 512)) % 512
        if padding > 0:
            chunks.append(bytes(padding))
    chunks.append(bytes(1024))
    return b"".join(chunks)


def main() -> None:
    """Build the archive and its checksum sidecar from the tracked payload."""
    options = parse_arguments(sys.argv[1:])
    root = Path(options.root).resolve() if options.root is not None else REPOSITORY_ROOT
    output_directory = (
        Path(options.output).resolve() if options.output is not None else root / "dist"
    )

    verify_git_work_tree(root)
    package_json = read_package_json(root)
    payload_paths = list_expected_payload_paths(root, package_json)
    package_root_prefix = f"Packages/{package_json['name']}"
    entries = collect_archive_entries(root, payload_paths, package_root_prefix)
    tar_buffer = write_tar(resolve_members(root, entries))
    # mtime=0 pins the gzip header; level 6 matches the original's default
    # compression so builds of the same tree stay byte-identical.
    gzip_buffer = gzip.compress(tar_buffer, compresslevel=6, mtime=0)

    archive_name = f"{package_json['name']}-{package_json['version']}.unitypackage"
    archive_path = output_directory / archive_name
    output_directory.mkdir(parents=True, exist_ok=True)
    archive_path.write_bytes(gzip_buffer)
    sha256 = hashlib.sha256(gzip_buffer).hexdigest()
    Path(f"{archive_path}.sha256").write_bytes(f"{sha256}  {archive_name}\n".encode("utf-8"))

    print(f"package_file: {archive_name}")
    print(f"sha256: {sha256}")
    print(f"entries: {len(entries)}")
    print(f"output: {output_directory}")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
