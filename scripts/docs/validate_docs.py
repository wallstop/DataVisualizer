#!/usr/bin/env python3
"""Validate the local documentation graph and shipped automation examples."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "docs"
MARKDOWN_LINK = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")


class DocumentationError(RuntimeError):
    """Raised when a documentation invariant is not met."""


def load_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise DocumentationError(f"{path.relative_to(ROOT)} is not valid JSON: {error}") from error


def check_link(source: Path, target: str) -> None:
    target = target.strip().strip("<>").split("#", 1)[0].split("?", 1)[0]
    if not target or target.startswith(("http://", "https://", "mailto:", "tel:")):
        return

    resolved = (source.parent / target).resolve()
    try:
        resolved.relative_to(DOCS.resolve())
    except ValueError as error:
        raise DocumentationError(
            f"{source.relative_to(ROOT)} links outside docs: {target}"
        ) from error
    if not resolved.is_file():
        raise DocumentationError(
            f"{source.relative_to(ROOT)} links to missing file: {target}"
        )


def validate_markdown() -> set[Path]:
    referenced_images: set[Path] = set()
    for source in sorted(DOCS.rglob("*.md")):
        contents = source.read_text(encoding="utf-8")
        for raw_target in MARKDOWN_LINK.findall(contents):
            target = raw_target.strip().strip("<>")
            check_link(source, target)
            if target.startswith(("http://", "https://", "mailto:", "tel:", "#")):
                continue
            target_path = (source.parent / target.split("#", 1)[0].split("?", 1)[0]).resolve()
            if target.startswith("!"):
                referenced_images.add(target_path)
        for match in re.finditer(r"!\[[^\]]*\]\(([^)]+)\)", contents):
            target = match.group(1).strip().strip("<>").split("#", 1)[0].split("?", 1)[0]
            if not target.startswith(("http://", "https://")):
                referenced_images.add((source.parent / target).resolve())
    return referenced_images


def validate_examples() -> None:
    automation = DOCS / "automation"
    for schema_name in (
        "data-visualizer-automation-request.schema.json",
        "data-visualizer-automation-result.schema.json",
    ):
        schema = load_json(automation / schema_name)
        if not schema.get("$schema") or not schema.get("$id"):
            raise DocumentationError(f"{schema_name} must declare $schema and $id")

    for example in sorted(automation.glob("*.example.json")):
        request = load_json(example)
        required = ("schemaVersion", "requestId", "operation")
        if request.get("schemaVersion") != 1 or any(
            key not in request for key in required
        ):
            raise DocumentationError(
                f"{example.relative_to(ROOT)} must be a schema-version 1 request"
            )
        if not isinstance(request["requestId"], str) or not request["requestId"].strip():
            raise DocumentationError(f"{example.relative_to(ROOT)} has an empty requestId")
        if not isinstance(request["operation"], int) or not 0 <= request["operation"] <= 10:
            raise DocumentationError(f"{example.relative_to(ROOT)} has an invalid operation")


def validate_images(referenced_images: set[Path]) -> None:
    for image in sorted(referenced_images):
        if not image.is_file() or image.stat().st_size < 1024:
            raise DocumentationError(f"Referenced image is missing or too small: {image}")

    manifest = load_json(DOCS / "images" / "manifest.json")
    scenarios = manifest.get("scenarios")
    if manifest.get("version") != 1 or not isinstance(scenarios, list):
        raise DocumentationError("docs/images/manifest.json must declare version 1 and scenarios")
    manifest_files: set[str] = set()
    for scenario in scenarios:
        if not all(scenario.get(key) for key in ("id", "file", "alt", "usedBy")):
            raise DocumentationError("every image manifest entry needs id, file, alt, and usedBy")
        image = DOCS / "images" / scenario["file"]
        manifest_files.add(image.name)
        if not image.is_file() or image.stat().st_size < 1024:
            raise DocumentationError(f"image manifest file is missing or too small: {image}")
    actual_files = {image.name for image in (DOCS / "images").glob("*.jpg")}
    if actual_files != manifest_files:
        raise DocumentationError(
            f"image manifest drift: expected {sorted(actual_files)}, listed {sorted(manifest_files)}"
        )


def validate_generated_output() -> None:
    site = ROOT / "site"
    if site.exists():
        raise DocumentationError("generated site/ must stay outside the repository")

    ignore = (ROOT / ".gitignore").read_text(encoding="utf-8")
    if "/site/" not in ignore:
        raise DocumentationError(".gitignore must exclude generated /site/")


def main() -> int:
    try:
        validate_images(validate_markdown())
        validate_examples()
        validate_generated_output()
    except DocumentationError as error:
        print(f"documentation validation failed: {error}", file=sys.stderr)
        return 1
    print("documentation validation passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
