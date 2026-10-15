#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["Pillow==12.3.0"]
# ///
"""Export Unity-captured PNGs as pixel-verified lossless WebP copies.

Run with ``uv run scripts/export_docs_webp.py`` after reviewing fresh captures.
Pillow belongs only to this optional local export, not to docs CI or the
stdlib harness. PNGs remain the capture sources. Every input is validated
before publication; unchanged output bytes keep their timestamps.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import struct
import tempfile
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple, cast

REQUIRED_SHOTS: Tuple[str, ...] = ("layout", "instance-actions", "create", "import", "search", "settings")
DEFAULT_SOURCE = Path(__file__).resolve().parents[1] / "docs" / "images"
DEFAULT_MANIFEST = DEFAULT_SOURCE / "capture-manifest.json"


def encode_verified(source: Path) -> bytes:
    # Import only when encoding: --help and missing-input diagnostics work
    # with ordinary Python too. uv resolves the pinned codec for real exports.
    from PIL import Image

    with Image.open(source) as captured:
        if captured.format != "PNG":
            raise ValueError(f"Expected a PNG capture: {source}")
        with captured.convert("RGBA") as pixels:
            if pixels.getcolors(maxcolors=1) is not None:
                raise ValueError(f"Blank capture: {source}")
            stream = io.BytesIO()
            # exact retains RGB even in fully transparent pixels. Verify the
            # decoded bytes rather than trusting an encoder flag alone.
            pixels.save(stream, format="WEBP", lossless=True, exact=True, quality=100, method=6)
            encoded = stream.getvalue()
            with Image.open(io.BytesIO(encoded)) as decoded:
                with decoded.convert("RGBA") as roundtrip:
                    if roundtrip.size != pixels.size or roundtrip.tobytes() != pixels.tobytes():
                        raise ValueError(f"WebP changed captured pixels: {source}")
    if source.stat().st_size <= len(encoded):
        raise ValueError(f"WebP is not smaller than the capture: {source}")
    return encoded


def read_inventory(manifest: Path) -> Dict[str, Tuple[int, int]]:
    document: object = json.loads(manifest.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"Invalid capture manifest: {manifest}")
    fields = cast(Dict[str, object], document)
    raw_captures = fields.get("captures")
    if type(fields.get("schema")) is not int or fields.get("schema") != 1 or not isinstance(raw_captures, dict):
        raise ValueError("Capture manifest must use schema 1 and contain exactly: " + ", ".join(REQUIRED_SHOTS))
    captures = cast(Dict[str, object], raw_captures)
    if set(captures) != set(REQUIRED_SHOTS):
        raise ValueError("Capture manifest must contain exactly: " + ", ".join(REQUIRED_SHOTS))
    inventory: Dict[str, Tuple[int, int]] = {}
    for shot, raw in captures.items():
        if not isinstance(raw, list):
            raise ValueError(f"Invalid dimensions for capture: {shot}")
        dimensions = cast(List[object], raw)
        if len(dimensions) != 2:
            raise ValueError(f"Invalid dimensions for capture: {shot}")
        width, height = dimensions
        if type(width) is not int or type(height) is not int or width <= 0 or height <= 0:
            raise ValueError(f"Invalid dimensions for capture: {shot}")
        inventory[shot] = (width, height)
    return inventory


def png_dimensions(source: Path) -> Tuple[int, int]:
    with source.open("rb") as stream:
        header = stream.read(24)
    if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
        raise ValueError(f"Invalid PNG header: {source}")
    return struct.unpack(">II", header[16:24])


def validate_inventory(source_dir: Path, manifest: Path = DEFAULT_MANIFEST) -> List[Path]:
    inventory = read_inventory(manifest)
    sources = [source_dir / f"data-visualizer-{shot}.png" for shot in sorted(inventory)]
    for source in sources:
        if not source.is_file():
            raise ValueError(f"Missing capture: {source}")
    unexpected = set(source_dir.glob("data-visualizer-*.png")) - set(sources)
    if unexpected:
        raise ValueError("Unexpected captures outside the manifest: " + ", ".join(sorted(path.name for path in unexpected)))
    for shot, dimensions in inventory.items():
        source = source_dir / f"data-visualizer-{shot}.png"
        if png_dimensions(source) != dimensions:
            raise ValueError(f"Capture dimensions differ from the reviewed manifest ({dimensions}): {source}")
    return sources


def export(source_dir: Path, output_dir: Path, check: bool, manifest: Path = DEFAULT_MANIFEST) -> int:
    sources = validate_inventory(source_dir, manifest)
    seen: Dict[bytes, str] = {}
    pending: List[Tuple[Path, bytes]] = []
    total_source = total_webp = 0
    # Validate the complete set before creating or replacing any output.
    for source in sources:
        encoded = encode_verified(source)
        digest = hashlib.sha256(encoded).digest()
        if digest in seen:
            raise ValueError(f"Duplicate captures: {seen[digest]} and {source.name}")
        seen[digest] = source.name
        target = output_dir / source.with_suffix(".webp").name
        if not target.is_file() or target.read_bytes() != encoded:
            pending.append((target, encoded))
        total_source += source.stat().st_size
        total_webp += len(encoded)
        print(f"[webp] {source.name}: {source.stat().st_size} -> {len(encoded)} bytes")
    if check and pending:
        raise ValueError("WebP copies are missing or stale: " + ", ".join(target.name for target, _ in pending))
    if pending:
        output_dir.mkdir(parents=True, exist_ok=True)
        # Dot-prefixed staging is invisible to Unity. Replace each complete
        # file atomically; the scope removes all temporary files on failure.
        with tempfile.TemporaryDirectory(prefix=".docs-webp-", dir=output_dir) as staging:
            for target, encoded in pending:
                temporary = Path(staging) / target.name
                temporary.write_bytes(encoded)
                os.replace(temporary, target)
    print(f"[webp] OK: {len(sources)} captures; {total_source} -> {total_webp} bytes; {len(pending)} updated")
    return len(pending)


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Export pixel-identical, smaller WebP copies of the documentation captures.")
    parser.add_argument("--source-dir", type=Path, default=DEFAULT_SOURCE, help="directory containing reviewed Unity PNG captures")
    parser.add_argument("--output-dir", type=Path, help="destination (default: source directory)")
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST, help="reviewed shot names and pixel dimensions")
    parser.add_argument("--check", action="store_true", help="verify existing copies without writing")
    options = parser.parse_args(argv)
    try:
        export(options.source_dir, options.output_dir or options.source_dir, options.check, options.manifest)
        return 0
    except (OSError, ValueError, ImportError) as error:
        print(f"[webp] ERROR: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
