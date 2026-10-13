#!/usr/bin/env python3
"""Stdlib-only publication contracts; the optional codec is verified locally."""
from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path
from typing import Sequence
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from export_docs_gif import FRAME_NAMES, publish
from harness_scope import self_test_selection


class GifPublicationTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        for name in FRAME_NAMES:
            (self.root / name).write_bytes(b'fixture')
        self.output = self.root / 'published' / 'create.gif'

    def encode(self, frames: Sequence[Path]) -> bytes:
        self.assertEqual(tuple(path.name for path in frames), FRAME_NAMES)
        return b'GIF89a-verified-fixture'

    def test_publication_is_atomic_and_unchanged_output_keeps_mtime(self) -> None:
        self.assertTrue(publish(self.root, self.output, False, self.encode))
        stamp = self.output.stat().st_mtime_ns
        self.assertFalse(publish(self.root, self.output, False, self.encode))
        self.assertEqual(self.output.stat().st_mtime_ns, stamp)
        self.assertEqual(list(self.output.parent.glob('.gif-export-*')), [])
        with patch('export_docs_gif.os.replace', side_effect=OSError('replace refused')):
            with self.assertRaises(OSError):
                publish(self.root, self.output, False, lambda _: b'changed')
        self.assertEqual(self.output.read_bytes(), b'GIF89a-verified-fixture')
        self.assertEqual(list(self.output.parent.glob('.gif-export-*')), [])

    def test_check_rejects_stale_and_missing_outputs_without_writes(self) -> None:
        with self.assertRaises(ValueError):
            publish(self.root, self.output, True, self.encode)
        self.assertFalse(self.output.parent.exists())
        publish(self.root, self.output, False, self.encode)
        self.output.write_bytes(b'old')
        with self.assertRaises(ValueError):
            publish(self.root, self.output, True, self.encode)
        self.assertEqual(self.output.read_bytes(), b'old')
        self.output.write_bytes(self.encode([self.root / name for name in FRAME_NAMES]))
        self.assertFalse(publish(self.root, self.output, True, self.encode))

    def test_export_and_metadata_select_every_affected_contract(self) -> None:
        expected = ('test_export_docs_gif.py', 'test_fast_check.py', 'test_harness_scope.py')
        paths = ['scripts/export_docs_gif.py', 'scripts/export_docs_gif.py.meta',
                 'scripts/tests/test_export_docs_gif.py', 'scripts/tests/test_export_docs_gif.py.meta',
                 'scripts/harness_scope.py']
        selection = self_test_selection(paths, expected)
        self.assertFalse(selection.all)
        self.assertEqual(selection.names, expected)

    def test_missing_later_frame_never_calls_encoder(self) -> None:
        (self.root / FRAME_NAMES[1]).unlink()
        with patch('export_docs_gif.encode_verified') as encoder:
            with self.assertRaises(ValueError):
                publish(self.root, self.output, False, encoder)
            encoder.assert_not_called()
        self.assertFalse(self.output.parent.exists())


if __name__ == '__main__':
    unittest.main()
