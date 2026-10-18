#!/usr/bin/env python3
"""Offline Unity bridge startup settings contracts."""
from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Dict

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import unity_mcp_settings as settings


class SettingsTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory(prefix='bridge settings ')
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.workspace = self.root / 'workspace'
        self.home = self.root / 'home'
        self.workspace.mkdir()
        self.home.mkdir()

    def project(self, name: str) -> Path:
        path = self.root / name
        (path / 'ProjectSettings').mkdir(parents=True)
        (path / 'ProjectSettings' / 'ProjectVersion.txt').write_text('6000.5', encoding='utf-8')
        return path

    def write_env(self, path: Path, values: Dict[str, str]) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(('\ufeff' + '\r\n'.join(key + '=' + value for key, value in values.items())).encode('utf-8'))

    def test_project_precedence_checks_source_before_alias_name(self) -> None:
        projects = [self.project(name) for name in ('process', 'workspace project', 'legacy')]
        for winner in range(3):
            for alias in ('UNITY_MCP_PROJECT', 'UNITY_PROJECT_PATH'):
                with self.subTest(winner=winner, alias=alias):
                    sources = [{alias if index == winner else 'UNITY_MCP_PROJECT': str(project)} if winner <= index else {}
                               for index, project in enumerate(projects)]
                    self.write_env(self.workspace / '.env.local', sources[1])
                    self.write_env(self.home / '.unity-mcp' / '.env.local', sources[2])
                    result = settings.resolve_settings(self.workspace, self.home, sources[0])
                    self.assertEqual(result['projectPath'], str(projects[winner]))
                    self.assertEqual(result['cliArgs'], ['mcp', '--project-path', str(projects[winner])])

    def test_legacy_file_only_supplies_launch_settings(self) -> None:
        project = self.project('legacy project')
        self.write_env(self.home / '.unity-mcp' / '.env.local', {
            'UNITY_CLI': 'legacy-cli', 'UNITY_MCP_PROJECT': str(project), 'UNITY_MCP_CLI_ARGS': '--instance  host:port',
            'UNITY_MCP_TOKEN': 'legacy-secret', 'UNITY_MCP_HTTP_PORT': '1', 'UNITY_MCP_TIMEOUT_MS': '2', 'UNITY_MCP_AUTO_SYNC': '0',
        })
        result = settings.resolve_settings(self.workspace, self.home, {})
        self.assertEqual(result['cliCommand'], 'legacy-cli')
        self.assertEqual(result['cliArgs'][-2:], ['--instance', 'host:port'])
        self.assertEqual((result['token'], result['port'], result['timeout'], result['autoSync']), ('', '9020', '180000', ''))
        self.write_env(self.workspace / '.env.local', {'UNITY_CLI': 'workspace-cli', 'UNITY_MCP_TOKEN': 'workspace-secret'})
        self.assertEqual(settings.resolve_settings(self.workspace, self.home, {})['cliCommand'], 'workspace-cli')
        result = settings.resolve_settings(self.workspace, self.home, {'UNITY_CLI': 'process-cli', 'UNITY_MCP_HTTP_PORT': '0'})
        self.assertEqual(result['cliCommand'], 'process-cli')
        self.assertEqual(result['token'], 'workspace-secret')
        self.assertEqual(result['port'], '0', 'Node retains ownership of numeric validation')
        self.assertNotIn('secret', '\n'.join(result['warnings']))

    def test_literal_file_formats_duplicates_and_missing_files(self) -> None:
        path = self.workspace / '.env.local'
        path.write_bytes(b'\xef\xbb\xbfexport\tKEY="first"\r\nKEY=second\r\nTOKEN=\'$(never-run) $HOME # literal\'\r\n# ignored\r\n')
        self.assertEqual(settings.read_env_file(path), {'KEY': 'second', 'TOKEN': '$(never-run) $HOME # literal'})
        self.assertEqual(settings.read_env_file(path.parent / 'missing'), {})

    def test_nested_and_invalid_project_pins_use_the_enclosing_workspace_project(self) -> None:
        project = self.project('project')
        workspace = project / 'Packages' / 'package'
        workspace.mkdir(parents=True)
        for configured in ('', str(workspace), str(self.root / 'not-a-project')):
            with self.subTest(configured=configured):
                result = settings.resolve_settings(workspace, self.home, {'UNITY_MCP_PROJECT': configured})
                self.assertEqual(result['projectPath'], str(project))
                self.assertTrue(result['warnings'])
        result = settings.resolve_settings(self.workspace, self.home, {'UNITY_MCP_PROJECT': str(self.root / 'missing')})
        self.assertEqual(result['projectPath'], '')
        self.assertEqual(result['cliArgs'], ['mcp'])
        self.assertIn('WITHOUT a project pin', result['warnings'][0])

    @unittest.skipUnless(os.name == 'posix', 'symlink fixture requires POSIX privileges')
    def test_project_pin_retains_logical_symlink_spelling(self) -> None:
        project = self.project('physical project')
        alias = self.root / 'logical project'
        alias.symlink_to(project, target_is_directory=True)
        result = settings.resolve_settings(alias, self.home, {})
        self.assertEqual(result['projectPath'], str(alias))

    def test_cli_protocol_uses_utf8_without_printing_credentials_to_diagnostics(self) -> None:
        project = self.project('日本語')
        self.write_env(self.workspace / '.env.local', {'UNITY_PROJECT_PATH': str(project), 'UNITY_MCP_TOKEN': 'synthetic-secret'})
        environment = {key: value for key, value in os.environ.items() if not key.startswith('UNITY_')}
        environment['PYTHONIOENCODING'] = 'cp1252'
        result = subprocess.run(
            [sys.executable, str(Path(settings.__file__)), '--workspace', str(self.workspace), '--home', str(self.home)],
            env=environment, capture_output=True, check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr.decode('utf-8', errors='replace'))
        parsed = json.loads(result.stdout.decode('utf-8'))
        self.assertEqual(parsed['projectPath'], str(project))
        self.assertEqual(parsed['token'], 'synthetic-secret')
        self.assertEqual(result.stderr, b'')


if __name__ == '__main__':
    unittest.main()
