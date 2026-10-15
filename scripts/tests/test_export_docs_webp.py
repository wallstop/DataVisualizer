#!/usr/bin/env python3
"""Stdlib publication contracts; real codec roundtrips run locally through uv."""
from __future__ import annotations

import contextlib
import io
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Tuple
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import export_docs_webp as exporter
from harness_scope import is_surface_path, self_test_selection


class ExportTests(unittest.TestCase):
    @staticmethod
    def make_sources(root: Path) -> None:
        for shot, dimensions in exporter.read_inventory(exporter.DEFAULT_MANIFEST).items():
            (root / f"data-visualizer-{shot}.png").write_bytes(b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", *dimensions))

    @staticmethod
    def encoded(source: Path) -> bytes:
        return source.name.encode("utf-8")

    def test_inventory_and_png_changes_select_the_export_regressions(self) -> None:
        for path in ("docs/images/capture-manifest.json", "docs/images/data-visualizer-search.png"):
            with self.subTest(path=path):
                self.assertTrue(is_surface_path(path))
                selection = self_test_selection([path], ["test_export_docs_webp.py"])
                self.assertFalse(selection.all)
                self.assertEqual(selection.names, ("test_export_docs_webp.py",))
        workflow = (exporter.DEFAULT_SOURCE.parents[1] / ".github/workflows/llm-lint.yml").read_text(encoding="utf-8")
        self.assertEqual(workflow.count('      - "docs/images/capture-manifest.json"'), 2)
        self.assertEqual(workflow.count('      - "docs/images/data-visualizer-*.png"'), 2)

    def test_committed_pngs_match_the_reviewed_inventory(self) -> None:
        self.assertEqual(len(exporter.validate_inventory(exporter.DEFAULT_SOURCE)), 6)

    def test_missing_capture_fails_before_encoding_or_writing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "absent output"
            with patch.object(exporter, "encode_verified") as encode:
                self.assertEqual(exporter.main(["--source-dir", directory, "--output-dir", str(output)]), 1)
                encode.assert_not_called()
            self.assertFalse(output.exists())

    def test_complete_set_is_validated_before_publication(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.make_sources(root)
            with patch.object(exporter, "encode_verified", side_effect=[b"first", ValueError("corrupt capture")]):
                self.assertEqual(exporter.main(["--source-dir", directory]), 1)
            self.assertEqual(list(root.glob("*.webp")), [])

    def test_search_capture_is_required_and_checked(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.make_sources(root)
            with patch.object(exporter, "encode_verified", side_effect=self.encoded):
                self.assertEqual(exporter.main(["--source-dir", directory]), 0)
                self.assertEqual(len(list(root.glob("*.webp"))), 6)
                self.assertEqual(exporter.main(["--source-dir", directory, "--check"]), 0)
                (root / "data-visualizer-search.webp").unlink()
                self.assertEqual(exporter.main(["--source-dir", directory, "--check"]), 1)

    def test_check_write_and_repeat_preserve_unchanged_files(self) -> None:
        with tempfile.TemporaryDirectory(prefix="webp with spaces ") as directory:
            root = Path(directory)
            self.make_sources(root)
            with patch.object(exporter, "encode_verified", side_effect=self.encoded):
                self.assertEqual(exporter.main(["--source-dir", directory, "--check"]), 1)
                self.assertEqual(list(root.glob("*.webp")), [])
                self.assertEqual(exporter.main(["--source-dir", directory]), 0)
                files = sorted(root.glob("*.webp"))
                self.assertEqual(len(files), 6)
                timestamps = [file.stat().st_mtime_ns for file in files]
                self.assertEqual(exporter.main(["--source-dir", directory, "--check"]), 0)
                self.assertEqual(exporter.main(["--source-dir", directory]), 0)
                self.assertEqual([file.stat().st_mtime_ns for file in files], timestamps)
                self.assertEqual(list(root.glob(".docs-webp-*")), [])

    def test_bad_inventory_fails_before_encoding_or_publication(self) -> None:
        for problem in ("missing-search", "unexpected", "wrong-size", "empty-png"):
            with self.subTest(problem=problem), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                self.make_sources(root)
                search = root / "data-visualizer-search.png"
                if problem == "missing-search":
                    search.unlink()
                elif problem == "unexpected":
                    (root / "data-visualizer-extra.png").write_bytes(search.read_bytes())
                elif problem == "wrong-size":
                    search.write_bytes(search.read_bytes()[:16] + struct.pack(">II", 1, 1))
                else:
                    search.write_bytes(b"")
                output = root / "published"
                with patch.object(exporter, "encode_verified") as encode:
                    self.assertEqual(exporter.main(["--source-dir", directory, "--output-dir", str(output)]), 1)
                    encode.assert_not_called()
                self.assertFalse(output.exists())

    def test_duplicate_captures_preserve_previous_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.make_sources(root)
            previous = root / "data-visualizer-create.webp"
            previous.write_bytes(b"previous reviewed capture")
            with patch.object(exporter, "encode_verified", return_value=b"same pixels"):
                self.assertEqual(exporter.main(["--source-dir", directory]), 1)
            self.assertEqual(list(root.glob("*.webp")), [previous])
            self.assertEqual(previous.read_bytes(), b"previous reviewed capture")

    def test_manifest_rejects_invalid_schema_inventory_and_dimensions(self) -> None:
        valid = json.loads(exporter.DEFAULT_MANIFEST.read_text(encoding="utf-8"))
        invalid_cases: Tuple[object, ...] = ([], {**valid, "schema": 2}, {**valid, "schema": True},
                        {**valid, "captures": {}},
                        {**valid, "captures": {**valid["captures"], "search": [0, 262]}},
                        {**valid, "captures": {**valid["captures"], "search": [True, 262]}})
        for invalid in invalid_cases:
            with self.subTest(manifest=invalid), tempfile.TemporaryDirectory() as directory:
                manifest = Path(directory) / "manifest.json"
                manifest.write_text(json.dumps(invalid), encoding="utf-8")
                with self.assertRaises(ValueError):
                    exporter.read_inventory(manifest)


if __name__ == "__main__":
    with contextlib.redirect_stdout(io.StringIO()):
        unittest.main()
