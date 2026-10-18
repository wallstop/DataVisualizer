#!/usr/bin/env python3
"""Offline MCP fleet, frontend syntax, and Python protocol contracts."""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Dict, Any

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from mcp_catalog import build_catalog


def settings(**changes: Any) -> Dict[str, Any]:
    result: Dict[str, Any] = dict(unity_url="http://host.example:9031/mcp?path='quoted'", unity_auth=False,
                                  has_uv=True, has_uvx=True, uv_bin='C:\\日本語 tools\\uv.exe',
                                  image_script='C:\\日本語 project\\.llm\\mcp\\zai_image_mcp.py')
    result.update(changes)
    return result


class CatalogTests(unittest.TestCase):
    def test_optional_fleet_order_and_frontend_coverage(self) -> None:
        for uv in (False, True):
            for uvx in (False, True):
                with self.subTest(uv=uv, uvx=uvx):
                    catalog = build_catalog(**settings(has_uv=uv, has_uvx=uvx))
                    expected = ['unity', 'github', 'context7'] + (['fetch', 'git'] if uvx else [])
                    expected += ['zai-vision', 'zai-web-search'] + (['zai-image'] if uv else [])
                    self.assertEqual(list(catalog), expected)
                    for name, definition in catalog.items():
                        frontends = set(definition) - {'summary'}
                        self.assertEqual(frontends, {'claude', 'codex', 'opencode', 'nanocoder', 'vscode'} |
                                         (set() if name == 'zai-image' else {'cursor'}))

    def test_unity_auth_is_optional_with_native_placeholders(self) -> None:
        for auth in (False, True):
            unity = build_catalog(**settings(unity_auth=auth))['unity']
            for frontend in ('claude', 'opencode', 'nanocoder', 'cursor'):
                self.assertEqual('headers' in unity[frontend], auth)
            self.assertEqual('bearer_token_env_var' in unity['codex'], auth)
            self.assertEqual('Authorization: Bearer ${UNITY_MCP_TOKEN}' in unity['vscode']['command'][-1], auth)
            self.assertFalse(unity['opencode']['oauth'])
            self.assertEqual(unity['opencode']['timeout'], 300000)

    @unittest.skipUnless(shutil.which('bash'), 'shell round-trip requires Bash')
    def test_wrapper_quotes_url_as_one_argument_without_execution(self) -> None:
        unity = build_catalog(**settings())['unity']
        shell = unity['vscode']['command'][-1]
        command = shell[shell.index('exec npx'):].replace('exec npx', 'capture', 1)
        script = 'capture() { printf "%s\\n" "$@"; }; ' + command
        result = subprocess.run(['bash', '-c', script], capture_output=True, text=True, check=True)
        self.assertEqual(result.stdout.splitlines()[-1], settings()['unity_url'])

    def test_paths_and_credential_syntax_follow_frontend_contracts(self) -> None:
        catalog = build_catalog(**settings())
        image = catalog['zai-image']
        self.assertEqual(image['codex']['command'], settings()['uv_bin'])
        self.assertEqual(image['codex']['args'][-1], settings()['image_script'])
        self.assertEqual(image['claude']['command'], 'uv')
        self.assertEqual(image['claude']['args'][-1], '.llm/mcp/zai_image_mcp.py')
        github = catalog['github']
        self.assertEqual(github['opencode']['headers']['Authorization'], 'Bearer {env:GITHUB_PERSONAL_ACCESS_TOKEN}')
        self.assertEqual(github['cursor']['headers']['Authorization'], 'Bearer ${env:GITHUB_PERSONAL_ACCESS_TOKEN}')
        self.assertEqual(github['codex']['http_headers'], {'X-MCP-Toolsets': 'all'})
        self.assertNotIn('timeout', github['opencode'])
        self.assertEqual(catalog['context7']['codex']['env_vars'], ['CONTEXT7_API_KEY'])
        self.assertEqual(catalog['zai-vision']['opencode']['environment']['Z_AI_MODE'], 'ZAI')

    def test_catalogs_do_not_share_mutable_definitions(self) -> None:
        first = build_catalog(**settings())
        first['context7']['claude']['args'].append('custom')
        first['context7']['claude']['env']['CONTEXT7_API_KEY'] = 'custom'
        second = build_catalog(**settings())
        self.assertNotIn('custom', second['context7']['claude']['args'])
        self.assertNotIn('custom', first['context7']['codex']['args'])
        self.assertEqual(second['context7']['claude']['env']['CONTEXT7_API_KEY'], '${CONTEXT7_API_KEY:-}')

    @unittest.skipUnless(os.name == 'posix' and shutil.which('node'), 'restricted PATH needs POSIX and Node')
    def test_configurator_falls_back_to_python_without_uv(self) -> None:
        with tempfile.TemporaryDirectory(prefix='catalog fallback ') as directory:
            root = Path(directory)
            binary = root / 'bin'
            binary.mkdir()
            (binary / 'python3').symlink_to(sys.executable)
            environment = {key: value for key, value in os.environ.items()
                           if not key.startswith(('GITHUB_', 'ZAI_', 'Z_AI_', 'UNITY_'))}
            environment.update(PATH=str(binary), DATAVIZ_MCP_WORKSPACE=str(root), HOME=str(root / 'home'),
                               CODEX_HOME=str(root / 'codex'), NANOCODER_CONFIG_DIR=str(root / 'nano'))
            script = Path(__file__).resolve().parents[2] / '.llm/mcp/configure.mjs'
            result = subprocess.run([str(shutil.which('node')), str(script), '--check'], env=environment,
                                    cwd=root, capture_output=True, check=True)
            self.assertIn(b'Servers: unity, github, context7, zai-vision, zai-web-search', result.stdout)
            self.assertNotIn(b'Python 3.9+', result.stderr)
            self.assertFalse((root / '.mcp.json').exists())

    def test_cli_uses_utf8_bytes_under_legacy_stdio_encoding(self) -> None:
        environment = dict(os.environ, PYTHONIOENCODING='cp1252')
        result = subprocess.run([sys.executable, str(Path(__file__).resolve().parents[1] / 'mcp_catalog.py')],
                                input=json.dumps(settings(), ensure_ascii=False).encode('utf-8'),
                                env=environment, capture_output=True, check=True)
        self.assertEqual(json.loads(result.stdout.decode('utf-8')), build_catalog(**settings()))


if __name__ == '__main__':
    unittest.main()
