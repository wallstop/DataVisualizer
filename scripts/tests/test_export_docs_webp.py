#!/usr/bin/env python3
"""Stdlib publication contracts; real codec roundtrips run locally through uv."""
from __future__ import annotations

import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import export_docs_webp as exporter


class ExportTests(unittest.TestCase):
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
            for shot in exporter.REQUIRED_SHOTS:
                (root / f"data-visualizer-{shot}.png").touch()
            with patch.object(exporter, "encode_verified", side_effect=[b"first", ValueError("corrupt capture")]):
                self.assertEqual(exporter.main(["--source-dir", directory]), 1)
            self.assertEqual(list(root.glob("*.webp")), [])

    def test_additional_capture_is_exported_and_checked(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for shot in (*exporter.REQUIRED_SHOTS, "search"):
                (root / f"data-visualizer-{shot}.png").touch()
            with patch.object(exporter, "encode_verified", return_value=b"verified-image"):
                self.assertEqual(exporter.main(["--source-dir", directory]), 0)
                self.assertEqual(len(list(root.glob("*.webp"))), 6)
                self.assertEqual(exporter.main(["--source-dir", directory, "--check"]), 0)
                (root / "data-visualizer-search.webp").unlink()
                self.assertEqual(exporter.main(["--source-dir", directory, "--check"]), 1)

    def test_check_write_and_repeat_preserve_unchanged_files(self) -> None:
        with tempfile.TemporaryDirectory(prefix="webp with spaces ") as directory:
            root = Path(directory)
            for shot in exporter.REQUIRED_SHOTS:
                (root / f"data-visualizer-{shot}.png").touch()
            with patch.object(exporter, "encode_verified", return_value=b"verified-image"):
                self.assertEqual(exporter.main(["--source-dir", directory, "--check"]), 1)
                self.assertEqual(list(root.glob("*.webp")), [])
                self.assertEqual(exporter.main(["--source-dir", directory]), 0)
                files = sorted(root.glob("*.webp"))
                self.assertEqual(len(files), 5)
                timestamps = [file.stat().st_mtime_ns for file in files]
                self.assertEqual(exporter.main(["--source-dir", directory, "--check"]), 0)
                self.assertEqual(exporter.main(["--source-dir", directory]), 0)
                self.assertEqual([file.stat().st_mtime_ns for file in files], timestamps)
                self.assertEqual(list(root.glob(".docs-webp-*")), [])


if __name__ == "__main__":
    with contextlib.redirect_stdout(io.StringIO()):
        unittest.main()
