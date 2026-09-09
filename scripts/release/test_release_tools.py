#!/usr/bin/env python3
"""Tests for the dependency-free release tooling."""

from __future__ import annotations

import hashlib
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from release_tools import (  # noqa: E402
    ReleaseError,
    build_unitypackage,
    bump_version,
    npm_dist_tag,
    prepare_release,
    resolve_version,
    rotate_changelog,
    source_entries,
    sync_context,
    changelog_section,
    validate_unitypackage,
    validate_release_tree,
)


def write_meta(path: Path, guid: str, folder: bool = False) -> None:
    content = f"fileFormatVersion: 2\nguid: {guid}\n"
    if folder:
        content += "folderAsset: yes\nDefaultImporter:\n"
    else:
        content += "DefaultImporter:\n"
    path.with_name(path.name + ".meta").write_text(content, encoding="utf-8")


class ReleaseToolsTests(unittest.TestCase):
    def test_version_bumps_and_explicit_versions(self) -> None:
        self.assertEqual(bump_version("1.2.3", "major"), "2.0.0")
        self.assertEqual(bump_version("1.2.3-rc.1", "patch"), "1.2.4")
        self.assertEqual(resolve_version("1.2.3", None, "1.3.0-preview.1"), "1.3.0-preview.1")
        self.assertEqual(npm_dist_tag("1.3.0-rc.1"), "next")
        self.assertEqual(npm_dist_tag("1.3.0-alpha"), "next")
        self.assertEqual(npm_dist_tag("1.3.0-test.1"), "latest")
        self.assertEqual(npm_dist_tag("1.3.0"), "latest")
        with self.assertRaises(ReleaseError):
            resolve_version("1.2.3", "patch", "1.3.0")
        with self.assertRaises(ReleaseError):
            resolve_version("1.2.3", None, "1.2.2")

    def test_changelog_rotation_preserves_unreleased_heading(self) -> None:
        source = "# Changelog\n\n## [Unreleased]\n\n### Fixed\n\n- A fix.\n\n## [1.0.0] - 2026-01-01\n"
        rotated = rotate_changelog(source, "1.1.0", "2026-09-09")
        self.assertIn("## [Unreleased]\n\n## [1.1.0] - 2026-09-09", rotated)
        self.assertIn("### Fixed\n\n- A fix.", rotated)
        with self.assertRaises(ReleaseError):
            rotate_changelog("# Changelog\n\n## [Unreleased]\n", "1.1.0", "2026-09-09")

    def test_prepare_dry_run_and_validate_release_tree(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".llm").mkdir()
            (root / "package.json").write_text(
                '{"name": "fixture", "version": "1.0.0"}\n', encoding="utf-8"
            )
            context = "# Instructions\n\n- **Version**: 1.0.0\n"
            (root / ".llm" / "context.md").write_text(context, encoding="utf-8")
            changelog = "# Changelog\n\n## [Unreleased]\n\n### Changed\n\n- Change.\n"
            (root / "CHANGELOG.md").write_text(changelog, encoding="utf-8")
            result = prepare_release(root, "patch", None, "2026-09-09", True)
            self.assertEqual(result["version"], "1.0.1")
            self.assertEqual((root / "package.json").read_text(encoding="utf-8"), '{"name": "fixture", "version": "1.0.0"}\n')
            prepare_release(root, "patch", None, "2026-09-09", False)
            self.assertEqual(validate_release_tree(root)["version"], "1.0.1")

    def test_unitypackage_is_deterministic_and_matches_source(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index, folder in enumerate(("Editor", "Runtime", "Tests"), 1):
                folder_path = root / folder
                folder_path.mkdir()
                write_meta(folder_path, f"{index:032x}", folder=True)
                source = folder_path / f"Sample{index}.txt"
                source.write_bytes(f"sample-{index}".encode("ascii"))
                write_meta(source, f"{index + 3:032x}")
            # Simulates a stale empty directory left by a local move.
            (root / "Runtime" / "EmptyLeftover").mkdir()
            first = root / "one.unitypackage"
            second = root / "two.unitypackage"
            build_unitypackage(root, first, ("Editor", "Runtime", "Tests"))
            build_unitypackage(root, second, ("Editor", "Runtime", "Tests"))
            self.assertEqual(first.read_bytes(), second.read_bytes())
            self.assertEqual(
                validate_unitypackage(first, root, ("Editor", "Runtime", "Tests"), first.with_name(first.name + ".sha256"))["entries"],
                6,
            )

    def test_missing_metadata_and_duplicate_guids_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            editor = root / "Editor"
            editor.mkdir()
            write_meta(editor, "1".zfill(32), folder=True)
            source = editor / "Sample.txt"
            source.write_text("sample", encoding="utf-8")
            with self.assertRaises(ReleaseError):
                source_entries(root, ("Editor",))
            write_meta(source, "1".zfill(32))
            with self.assertRaises(ReleaseError):
                source_entries(root, ("Editor",))

    def test_checksum_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index, folder in enumerate(("Editor", "Runtime", "Tests"), 1):
                folder_path = root / folder
                folder_path.mkdir()
                write_meta(folder_path, f"{index:032x}", folder=True)
                source = folder_path / "Sample.txt"
                source.write_text("sample", encoding="utf-8")
                write_meta(source, f"{index + 3:032x}")
            archive = root / "package.unitypackage"
            build_unitypackage(root, archive, ("Editor", "Runtime", "Tests"))
            checksum = archive.with_name(archive.name + ".sha256")
            checksum.write_text(f"{'0' * 64}  {archive.name}\n", encoding="ascii")
            with self.assertRaises(ReleaseError):
                validate_unitypackage(archive, root, ("Editor", "Runtime", "Tests"), checksum)

    def test_changelog_section_is_ready_for_release_notes(self) -> None:
        source = "# Changelog\n\n## [1.2.3] - 2026-09-09\n\n### Changed\n\n- A change.\n\n## [1.2.2] - 2026-01-01\n"
        self.assertEqual(
            changelog_section(source, "1.2.3"),
            "## [1.2.3] - 2026-09-09\n\n### Changed\n\n- A change.",
        )

    def test_workflows_have_release_only_triggers_and_no_unity_runner(self) -> None:
        root = Path(__file__).parents[2]
        prepare = (root / ".github/workflows/release-prepare.yml").read_text(encoding="utf-8")
        tag = (root / ".github/workflows/release-tag.yml").read_text(encoding="utf-8")
        publish = (root / ".github/workflows/npm-publish.yml").read_text(encoding="utf-8")
        self.assertIn("workflow_dispatch:", prepare)
        self.assertIn("github.ref == 'refs/heads/main'", prepare)
        self.assertIn("github.event.pull_request.merged == true", tag)
        self.assertIn("startsWith(github.event.pull_request.head.ref, 'release/v')", tag)
        self.assertIn('tags:\n      - "v*"', publish)
        self.assertNotIn("actions/unity", publish.lower())
        self.assertNotRegex(publish, r"(?m)^\s*-?\s*unity(?:\s|$)")


if __name__ == "__main__":
    unittest.main(verbosity=2)
