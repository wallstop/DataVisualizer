#!/usr/bin/env python3
"""Offline credential alias contracts; only synthetic values reach fixtures."""
from __future__ import annotations

import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Dict, List, Tuple

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import mcp_aliases as aliases


class AliasTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory(prefix='credential aliases ')
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.path = self.root / '.env.local'

    def test_effective_values_keep_precedence_and_detect_divergence(self) -> None:
        cases: List[Tuple[Dict[str, str], str, str]] = [
            ({}, '', 'missing'),
            ({'GITHUB_PERSONAL_ACCESS_TOKEN': 'a', 'GITHUB_MCP_PAT': 'a'}, '', 'configured'),
            ({'GITHUB_PERSONAL_ACCESS_TOKEN': 'a', 'GITHUB_MCP_PAT': 'b'}, '', 'divergent'),
            ({'GITHUB_PERSONAL_ACCESS_TOKEN': 'a'}, 'GITHUB_MCP_PAT=b', 'divergent'),
            ({'GITHUB_PERSONAL_ACCESS_TOKEN': 'a'}, 'GITHUB_PERSONAL_ACCESS_TOKEN=b\nGITHUB_MCP_PAT=a', 'configured'),
            ({'GITHUB_PERSONAL_ACCESS_TOKEN': ''}, 'GITHUB_PERSONAL_ACCESS_TOKEN=a', 'alias-pending'),
        ]
        for environment, content, expected in cases:
            with self.subTest(expected=expected, content=content):
                self.path.write_bytes(content.encode('utf-8'))
                before = self.path.read_bytes()
                self.assertEqual(aliases.synchronize(self.root, environment, check_only=True)['github'], expected)
                self.assertEqual(self.path.read_bytes(), before)

    def test_aliases_preserve_bom_line_endings_and_repeat_without_writes(self) -> None:
        for newline in ('\n', '\r\n', '\r'):
            with self.subTest(newline=newline):
                original = '\ufeff# keep' + newline + "export ZAI_API_KEY='synthetic-a'" + newline + 'GITHUB_MCP_PAT=synthetic-b'
                self.path.write_bytes(original.encode('utf-8'))
                result = aliases.synchronize(self.root, {})
                self.assertEqual(result, {'zai': 'aliased', 'github': 'aliased'})
                content = self.path.read_bytes()
                self.assertTrue(content.startswith(original.encode('utf-8')))
                if newline == '\r\n':
                    self.assertNotIn(b'\n', content.replace(b'\r\n', b''))
                parsed = aliases.read_values(content.decode('utf-8'))
                self.assertEqual(parsed['ZAI_API_KEY'], parsed['Z_AI_API_KEY'])
                self.assertEqual(parsed['GITHUB_MCP_PAT'], parsed['GITHUB_PERSONAL_ACCESS_TOKEN'])
                stamp = self.path.stat().st_mtime_ns
                self.assertEqual(aliases.synchronize(self.root, {}), {'zai': 'configured', 'github': 'configured'})
                self.assertEqual(self.path.read_bytes(), content)
                self.assertEqual(self.path.stat().st_mtime_ns, stamp)
                if os.name == 'posix':
                    self.assertEqual(stat.S_IMODE(self.path.stat().st_mode), 0o600)

    def test_opaque_literal_values_round_trip_without_environment_mutation(self) -> None:
        for value in (' leading and trailing ', "'quoted'", '"quoted"', '$(never-run) `never-run` $HOME # literal'):
            self.path.unlink(missing_ok=True)
            environment = {'GITHUB_PERSONAL_ACCESS_TOKEN': value}
            before = dict(environment)
            self.assertEqual(aliases.synchronize(self.root, environment)['github'], 'aliased')
            self.assertEqual(aliases.read_values(self.path.read_text(encoding='utf-8'))['GITHUB_MCP_PAT'], value)
            self.assertEqual(environment, before)

    def test_check_only_and_missing_values_do_not_create_a_file(self) -> None:
        self.assertEqual(aliases.synchronize(self.root, {}), {'zai': 'missing', 'github': 'missing'})
        self.assertFalse(self.path.exists())
        result = aliases.synchronize(self.root, {'ZAI_API_KEY': 'synthetic'}, check_only=True)
        self.assertEqual(result['zai'], 'alias-pending')
        self.assertFalse(self.path.exists())

    def test_invalid_literal_values_do_not_partially_write_aliases(self) -> None:
        for value in ('synthetic\ninjected=value', 'synthetic\rvalue', 'synthetic\0value'):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    aliases.synchronize(self.root, {'ZAI_API_KEY': 'valid', 'GITHUB_MCP_PAT': value})
                self.assertFalse(self.path.exists())

    def test_cli_outputs_statuses_only_and_keeps_invalid_utf8_intact(self) -> None:
        environment: Dict[str, str] = {key: value for key, value in os.environ.items() if not key.startswith(('GITHUB_', 'ZAI_', 'Z_AI_'))}
        environment.update(GITHUB_MCP_PAT='synthetic-private-value', PYTHONIOENCODING='cp1252')
        command = [sys.executable, str(Path(aliases.__file__)), '--workspace', str(self.root)]
        result = subprocess.run(command, env=environment, capture_output=True, check=False)
        self.assertEqual(result.returncode, 0)
        self.assertEqual(json.loads(result.stdout.decode('utf-8')), {'zai': 'missing', 'github': 'aliased'})
        self.assertNotIn(b'synthetic-private-value', result.stdout + result.stderr)
        before = b'\xff synthetic-private-value'
        self.path.write_bytes(before)
        result = subprocess.run(command, env=environment, capture_output=True, check=False)
        self.assertEqual(result.returncode, 1)
        self.assertEqual(self.path.read_bytes(), before)
        self.assertNotIn(b'synthetic-private-value', result.stdout + result.stderr)

    @unittest.skipUnless(os.name == 'posix' and shutil.which('node'), 'restricted PATH fixture needs POSIX and Node')
    def test_configurator_uses_python_when_uv_is_absent(self) -> None:
        binary = self.root / 'bin'
        binary.mkdir()
        (binary / 'python').symlink_to(sys.executable)
        environment = {key: value for key, value in os.environ.items() if not key.startswith(('GITHUB_', 'ZAI_', 'Z_AI_', 'UNITY_'))}
        environment.update(PATH=str(binary), DATAVIZ_MCP_WORKSPACE=str(self.root), HOME=str(self.root / 'home'),
                           CODEX_HOME=str(self.root / 'codex'), NANOCODER_CONFIG_DIR=str(self.root / 'nano'),
                           GITHUB_PERSONAL_ACCESS_TOKEN='synthetic-private-value')
        script = Path(aliases.__file__).resolve().parents[1] / '.llm/mcp/configure.mjs'
        result = subprocess.run([str(shutil.which('node')), str(script)], env=environment, cwd=self.root, capture_output=True, check=False)
        self.assertEqual(result.returncode, 0, result.stderr.decode('utf-8', errors='replace'))
        self.assertNotIn(b'\xe2\x9d\x8c', result.stdout)
        self.assertEqual(aliases.read_values(self.path.read_text(encoding='utf-8'))['GITHUB_MCP_PAT'], 'synthetic-private-value')
        self.assertNotIn(b'synthetic-private-value', result.stdout + result.stderr)


if __name__ == '__main__':
    unittest.main()
