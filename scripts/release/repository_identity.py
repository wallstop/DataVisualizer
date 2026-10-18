#!/usr/bin/env python3
"""Check npm provenance identity; repair only the known unpublished 0.1.1 metadata."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any, Dict, cast

REPOSITORY = "wallstop/DataVisualizer"
LEGACY_URL = "git+https://github.com/Ambiguous-Interactive/DxVisualizer.git"
CANONICAL_URL = f"git+https://github.com/{REPOSITORY}.git"


def verify_identity(root: Path, repository: str, repair_legacy: bool = False) -> bool:
    """Require the case-sensitive Actions identity before packing or remote writes.

    Recovery changes only repository.url for the observed, unpublished version.
    It leaves the source tag, version, code, and released notes intact.
    """
    if re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) is None:
        raise ValueError("Expected a GitHub owner/repository identity.")
    path = root / "package.json"
    package = cast(Dict[str, Any], json.loads(path.read_text(encoding="utf-8")))
    metadata = package.get("repository")
    repository_metadata = cast(Dict[str, Any], metadata) if isinstance(metadata, dict) else None
    url: Any = repository_metadata.get("url") if repository_metadata is not None else cast(Any, metadata)
    expected = f"git+https://github.com/{repository}.git"
    if url == expected:
        return False
    if (
        repair_legacy
        and repository == REPOSITORY
        and package.get("name") == "com.wallstop-studios.data-visualizer"
        and package.get("version") == "0.1.1"
        and repository_metadata is not None
        and url == LEGACY_URL
    ):
        repository_metadata["url"] = CANONICAL_URL
        path.write_text(json.dumps(package, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print("::notice::Repaired only repository.url in the 0.1.1 build tree; the existing tag is unchanged.")
        return True
    raise ValueError(f"package.json repository.url is {url!r}; npm provenance requires {expected!r}. Fix the manifest before releasing.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--repository", required=True)
    parser.add_argument("--repair-legacy", action="store_true")
    args = parser.parse_args()
    verify_identity(args.root, args.repository, args.repair_legacy)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError) as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
