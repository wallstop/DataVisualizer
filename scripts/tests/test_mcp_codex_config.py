#!/usr/bin/env python3
"""Offline Codex MCP persistence contracts; never touch a real agent home."""
from __future__ import annotations

import importlib
import json
import os
import stat
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import mcp_codex_config as config


class CodexConfigTests(unittest.TestCase):
    def test_preserves_user_state_migrates_trust_and_is_idempotent(self) -> None:
        with tempfile.TemporaryDirectory(prefix='codex workspace ') as directory:
            root = Path(directory)
            home = root / 'home'
            home.mkdir()
            target = home / 'config.toml'
            target.write_text('model = "keep"\n[projects."other"]\ntrust_level = "untrusted"\n' + config.TOML_BEGIN
                              + '\n[mcp_servers.stale]\ncommand = "old"\n[projects.' + json.dumps(str(root))
                              + ']\ntrust_level = "untrusted"\n' + config.TOML_END + '\n', encoding='utf-8')
            entries = {'unity': {'url': 'http://bridge/mcp', 'bearer_token_env_var': 'UNITY_MCP_TOKEN'}}
            before = target.read_bytes()
            self.assertTrue(config.write_config(root, home, entries, check_only=True))
            self.assertEqual(target.read_bytes(), before)
            self.assertTrue(config.write_config(root, home, entries))
            result = target.read_text(encoding='utf-8')
            self.assertIn('model = "keep"', result)
            self.assertIn('[projects."other"]\ntrust_level = "untrusted"', result)
            self.assertNotIn('mcp_servers.stale', result)
            self.assertEqual(result.count('[projects.' + json.dumps(str(root)) + ']'), 1)
            self.assertTrue(result.endswith('trust_level = "trusted"\n'))
            timestamp = target.stat().st_mtime_ns
            self.assertFalse(config.write_config(root, home, entries))
            self.assertEqual(target.stat().st_mtime_ns, timestamp)
            if os.name == 'posix':
                self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o600)
            before = target.read_bytes()
            with patch.object(config, 'write_private', side_effect=OSError('synthetic failure')):
                with self.assertRaises(OSError):
                    config.write_config(root, home, {'new': {'url': 'http://other/mcp'}})
            self.assertEqual(target.read_bytes(), before)

    def test_workspace_cleanup_keeps_user_content_and_sibling_files(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            workspace = Path(directory)
            folder = workspace / '.codex'
            folder.mkdir()
            target = folder / 'config.toml'
            generated = config.TOML_BEGIN + '\n[mcp_servers.unity]\nurl = "http://old"\n' + config.TOML_END + '\n'
            target.write_text(generated, encoding='utf-8')
            (folder / 'keep').write_text('user-owned', encoding='utf-8')
            self.assertTrue(config.prune_workspace(workspace, check_only=True)[0])
            self.assertEqual(target.read_text(encoding='utf-8'), generated)
            self.assertTrue(config.prune_workspace(workspace)[0])
            self.assertFalse(target.exists())
            self.assertTrue((folder / 'keep').is_file())
            self.assertEqual(config.prune_workspace(workspace), (False, ''))
            user = 'model = "keep"\n[mcp_servers.user]\ncommand = "keep"'
            target.write_text(user + '\n' + generated, encoding='utf-8')
            changed, note = config.prune_workspace(workspace)
            self.assertTrue(changed)
            self.assertIn('user content kept', note)
            self.assertEqual(target.read_text(encoding='utf-8'), user + '\n')
            for content in (user, user + '\n', user + '\n\n'):
                target.write_text(content, encoding='utf-8')
                changed, note = config.prune_workspace(workspace)
                self.assertFalse(changed)
                self.assertIn('user-defined', note)
                self.assertEqual(target.read_text(encoding='utf-8'), content)

    def test_basic_strings_escape_windows_paths_quotes_and_control_characters(self) -> None:
        values = ['C:\\Tools\\uv.exe', 'https://example.com/"quoted"', 'line\nbreak\tend', '日本語']
        for value in values:
            with self.subTest(value=value):
                remote = config.server_table('remote', {'url': value, 'bearer_token_env_var': value, 'http_headers': {'X-Key': value}})
                local = config.server_table('local', {'command': value, 'args': [value], 'env_vars': [value], 'startup_timeout_sec': 3})
                encoded = json.dumps(value, ensure_ascii=False)
                self.assertIn('url = ' + encoded, remote)
                self.assertIn('command = ' + encoded, local)
                self.assertIn('args = [' + encoded + ']', local)
                self.assertIn('env_vars = [' + encoded + ']', local)
                self.assertIn('"X-Key" = ' + encoded, remote)

    def test_cli_check_and_invalid_input_never_create_state(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            home = root / 'isolated home'
            for data, flags, expected_exit in [('not json', [], 1), ('[]', [], 1), ('{"bad":0}', [], 1), ('{}', ['--check'], 0)]:
                result = subprocess.run(
                    [sys.executable, str(Path(config.__file__)), 'write', '--workspace', str(root), *flags],
                    input=data, env={**os.environ, 'CODEX_HOME': str(home)}, capture_output=True,
                    text=True, encoding='utf-8', check=False,
                )
                self.assertEqual(result.returncode, expected_exit, result.stderr)
                self.assertFalse(home.exists())
                if expected_exit == 0:
                    self.assertEqual(json.loads(result.stdout), {'changed': True, 'note': ''})
                else:
                    self.assertEqual(result.stdout, '')

    @unittest.skipIf(sys.version_info < (3, 11), 'stdlib TOML parser requires Python 3.11')
    def test_generated_tables_round_trip_through_a_toml_parser(self) -> None:
        parser = importlib.import_module('tomllib')
        value = 'C:\\Tools\\"quoted"\\uv.exe\t日本語\x7f'
        table = config.server_table('local', {'command': value, 'args': [value], 'env_vars': ['TOKEN']})
        parsed = parser.loads(table)['mcp_servers']['local']
        self.assertEqual(parsed['command'], value)
        self.assertEqual(parsed['args'], [value])
        remote = config.server_table('remote', {'url': 'http://bridge/mcp', 'http_headers': {'X-Quoted': value}})
        self.assertEqual(parser.loads(remote)['mcp_servers']['remote']['http_headers']['X-Quoted'], value)


if __name__ == '__main__':
    unittest.main()
