#!/usr/bin/env python3
"""Offline frontend JSON persistence contracts; no real user homes."""
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
from typing import Dict

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import mcp_json_config as config


class JsonConfigTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory(prefix='json config ')
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.home = self.root / 'home'
        self.nano = self.root / 'nano-home'
        self.writer = config.JsonWriter(self.root, self.home, self.nano)
        self.managed = {name: {'unity': {'url': 'http://bridge/mcp'}} for name in ('claude', 'opencode', 'nanocoder', 'vscode', 'cursor')}

    def put(self, path: Path, value: object) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(('\ufeff' + json.dumps(value)).encode('utf-8'))

    def read(self, path: Path) -> config.JsonMap:
        return self.writer.read(path)

    def snapshot(self) -> Dict[str, bytes]:
        return {path.relative_to(self.root).as_posix(): path.read_bytes() for path in self.root.rglob('*') if path.is_file()}

    def environment(self) -> Dict[str, str]:
        result = {key: value for key, value in os.environ.items() if not key.startswith(('UNITY_', 'GITHUB_', 'ZAI_', 'Z_AI_'))}
        result.update(DATAVIZ_MCP_WORKSPACE=str(self.root), HOME=str(self.home), USERPROFILE=str(self.home),
                      CODEX_HOME=str(self.root / 'codex'), NANOCODER_CONFIG_DIR=str(self.nano))
        return result

    def test_merge_migration_and_user_providers_survive(self) -> None:
        self.put(self.root / '.mcp.json', {'keep': 3, 'mcpServers': {'user': {'command': 'custom'}}})
        self.put(self.root / '.claude/settings.local.json', {'keep': 4, 'enableAllProjectMcpServers': 1})
        self.put(self.home / '.claude.json', {'projects': {'other': {'keep': True}, str(self.root): {'enabledMcpjsonServers': ['user']}}})
        self.put(self.root / 'opencode.json', {'theme': 'keep', 'mcp': {'timeout': {'startup': 4}, 'old': {'enabled': False, 'timeout': 12}, 'servers': {'user': {'disabled': False}}}})
        self.put(self.root / 'agents.config.json', {'keep': 5, 'nanocoder': {'providers': [{'name': 'zai', 'models': ['stale']}, {'name': 'user', 'models': ['custom']}]}})
        outcomes = self.writer.write_all(self.managed)
        self.assertTrue(all(not value['error'] for value in outcomes.values()), outcomes)
        self.assertEqual(self.read(self.root / '.mcp.json')['mcpServers']['user'], {'command': 'custom'})
        self.assertIs(self.read(self.root / '.claude/settings.local.json')['enableAllProjectMcpServers'], True)
        approvals = self.read(self.home / '.claude.json')['projects']
        self.assertEqual(approvals['other'], {'keep': True})
        self.assertEqual(approvals[str(self.root)]['enabledMcpjsonServers'], ['unity', 'user'])
        opencode = self.read(self.root / 'opencode.json')
        self.assertEqual(opencode['theme'], 'keep')
        self.assertEqual(opencode['mcp']['timeout'], {'startup': 4})
        self.assertEqual(opencode['mcp']['servers']['old'], {'disabled': True, 'timeout': {'catalog': 12, 'execution': 12}})
        self.assertIn('user', opencode['mcp']['servers'])
        providers = self.read(self.root / 'agents.config.json')['nanocoder']['providers']
        self.assertEqual([item['name'] for item in providers], ['zai', 'openrouter', 'user'])
        self.assertEqual(providers[-1]['models'], ['custom'])
        self.assertEqual(self.read(self.nano / 'nanocoder-preferences.json')['trustedDirectories'], [str(self.root)])

    def test_check_only_idempotence_and_modes(self) -> None:
        before = self.snapshot()
        outcomes = config.JsonWriter(self.root, self.home, self.nano, check_only=True).write_all(self.managed)
        self.assertTrue(all(value['changed'] for value in outcomes.values()))
        self.assertEqual(self.snapshot(), before)
        self.writer.write_all(self.managed)
        before = self.snapshot()
        outcomes = self.writer.write_all(self.managed)
        self.assertTrue(all(not value['changed'] and not value['error'] for value in outcomes.values()), outcomes)
        self.assertEqual(self.snapshot(), before)
        if os.name == 'posix':
            self.assertEqual(stat.S_IMODE((self.root / '.cursor/mcp.json').stat().st_mode), 0o644)
            self.assertEqual(stat.S_IMODE((self.root / '.mcp.json').stat().st_mode), 0o600)

    def test_invalid_user_json_is_retained_and_other_frontends_continue(self) -> None:
        target = self.root / '.mcp.json'
        target.write_bytes(b'invalid user json')
        outcomes = self.writer.write_all(self.managed)
        self.assertTrue(outcomes['claude']['error'])
        self.assertEqual(target.read_bytes(), b'invalid user json')
        self.assertTrue(all(not value['error'] for name, value in outcomes.items() if name != 'claude'))
        self.assertTrue((self.root / '.cursor/mcp.json').is_file())

    def test_cli_protocol_is_utf8_under_windows_1252_stdio(self) -> None:
        self.managed['vscode']['日本語'] = {'command': '日本語'}
        result = subprocess.run(
            [sys.executable, str(Path(config.__file__)), '--workspace', str(self.root), '--home', str(self.home), '--nano-home', str(self.nano)],
            input=json.dumps(self.managed, ensure_ascii=False).encode('utf-8'), env={**os.environ, 'PYTHONIOENCODING': 'cp1252'},
            capture_output=True, check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr.decode('utf-8', errors='replace'))
        self.assertTrue(all(not value['error'] for value in json.loads(result.stdout.decode('utf-8')).values()))
        self.assertEqual(self.read(self.root / '.vscode/mcp.json')['servers']['日本語']['command'], '日本語')
        self.assertEqual(result.stderr, b'')

    @unittest.skipUnless(os.name == 'posix' and shutil.which('node'), 'restricted PATH fixture needs POSIX and Node')
    def test_node_configurator_falls_back_to_python_without_uv(self) -> None:
        binary = self.root / 'bin'
        binary.mkdir()
        (binary / 'python').symlink_to(sys.executable)
        environment = {**self.environment(), 'PATH': str(binary)}
        script = Path(config.__file__).resolve().parents[1] / '.llm/mcp/configure.mjs'
        result = subprocess.run([str(shutil.which('node')), str(script)], env=environment, cwd=self.root, capture_output=True, check=False)
        output = result.stdout.decode('utf-8', errors='replace')
        self.assertEqual(result.returncode, 0, result.stderr.decode('utf-8', errors='replace'))
        self.assertNotIn('❌', output)
        self.assertTrue((self.root / '.cursor/mcp.json').is_file())

    @unittest.skipUnless(shutil.which('node'), 'Node is required for the configurator')
    def test_failed_batch_is_not_restarted_for_each_frontend(self) -> None:
        script = self.root / '.llm/mcp/configure.mjs'
        script.parent.mkdir(parents=True)
        source = Path(config.__file__).resolve().parents[1] / '.llm/mcp/configure.mjs'
        shutil.copyfile(source, script)
        helper = self.root / 'scripts/mcp_json_config.py'
        helper.parent.mkdir()
        helper.write_text("from pathlib import Path\nimport sys\nwith Path('writer-runs').open('a') as stream: stream.write('run\\n')\n"
                          "print('synthetic writer failure', file=sys.stderr)\nsys.exit(1)\n", encoding='utf-8')
        result = subprocess.run([str(shutil.which('node')), str(script)], env=self.environment(), cwd=self.root, capture_output=True, check=False)
        output = result.stdout.decode('utf-8', errors='replace')
        self.assertEqual((self.root / 'writer-runs').read_text(encoding='utf-8').splitlines(), ['run'])
        for label in ('Claude Code', 'OpenCode', 'Nanocoder', 'VS Code', 'Cursor'):
            self.assertIn('❌ ' + label, output)
        self.assertTrue((self.root / 'codex/config.toml').is_file(), 'The independent Codex writer still runs')


if __name__ == '__main__':
    unittest.main()
