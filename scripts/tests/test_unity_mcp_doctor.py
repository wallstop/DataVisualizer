#!/usr/bin/env python3
"""Offline contracts for project selection, bridge RPC and diagnostic exits."""
from __future__ import annotations

import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from typing import Dict, List, Optional

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from unity_mcp_doctor import Doctor, JsonObject, enclosing_project, read_env_local, request_json


class DoctorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.project = self.root / 'Project'
        (self.project / 'ProjectSettings').mkdir(parents=True)
        (self.project / 'ProjectSettings' / 'ProjectVersion.txt').write_text('6000.5', encoding='utf-8')
        (self.project / 'Packages').mkdir()
        self.manifest = self.project / 'Packages' / 'manifest.json'
        self.manifest.write_text('{"dependencies":{"com.unity.pipeline":"1"}}', encoding='utf-8')
        self.calls: List[JsonObject] = []
        self.headers: List[Dict[str, str]] = []
        self.urls: List[str] = []
        self.responses: Dict[str, JsonObject] = {
            'health': {'ok': True, 'cli': ['unity', 'mcp', '--project-path', str(self.project)]},
            'initialize': {'result': {'serverInfo': {'name': 'Unity'}}},
            'tools/list': {'result': {'tools': [{'name': 'editor_status'}]}},
            'tools/call': {'result': {'content': [{'text': json.dumps({'projectPath': str(self.project)})}]}},
        }
        self.error_method = ''

    def command(self, arguments: List[str]) -> str:
        return '1.0' if arguments == ['--version'] else json.dumps({'data': {'instances': [{'project': str(self.project)}]}})

    def request(self, url: str, headers: Dict[str, str], payload: Optional[JsonObject]) -> JsonObject:
        self.urls.append(url)
        self.headers.append(dict(headers))
        method = str(payload['method']) if payload else 'health'
        if payload:
            self.calls.append(payload)
        if method == self.error_method:
            raise OSError('offline fixture')
        return self.responses.get(method, {})

    def run_doctor(self, environment: Optional[Dict[str, str]] = None) -> tuple[Doctor, int, str]:
        env = {'UNITY_MCP_PROJECT': str(self.project)} if environment is None else dict(environment)
        env.setdefault('DATAVIZ_IN_CONTAINER', '0')
        doctor = Doctor(self.root, self.root / 'home', env, self.command, self.request)
        stream = io.StringIO()
        with contextlib.redirect_stdout(stream):
            result = doctor.run()
        return doctor, result, stream.getvalue()

    def test_healthy_roundtrip_and_token_precedence(self) -> None:
        (self.root / '.env.local').write_text('UNITY_MCP_TOKEN=local-secret\nUNITY_MCP_HTTP_PORT=9001', encoding='utf-8')
        doctor, code, output = self.run_doctor({'UNITY_MCP_PROJECT': str(self.project), 'UNITY_MCP_TOKEN': 'process-secret', 'UNITY_MCP_HTTP_PORT': '9002'})
        self.assertEqual((doctor.failures, doctor.warnings, code), (0, 0, 0))
        self.assertEqual([call['method'] for call in self.calls], ['initialize', 'notifications/initialized', 'tools/list', 'tools/call'])
        self.assertEqual([call['id'] for call in self.calls if 'id' in call], [1, 2, 3])
        self.assertEqual(self.calls[0]['params'], {'protocolVersion': '2025-03-26', 'capabilities': {}, 'clientInfo': {'name': 'unity-mcp-doctor', 'version': '0.1.0'}})
        self.assertEqual(self.calls[3]['params'], {'name': 'editor_status', 'arguments': {}})
        self.assertEqual(self.headers[0], {})
        self.assertTrue(all(headers['authorization'] == 'Bearer process-secret' for headers in self.headers[1:]))
        self.assertTrue(all(url.startswith('http://127.0.0.1:9002/') for url in self.urls))
        self.assertNotIn('secret', output)
        self.assertIn('0 failure(s), 0 warning(s)', output)

    def test_project_and_environment_precedence(self) -> None:
        home = self.root / 'home' / '.unity-mcp'
        home.mkdir(parents=True)
        (self.root / '.env.local').write_text(f'UNITY_MCP_PROJECT="{self.project}"\nUNITY_MCP_HTTP_PORT=9001', encoding='utf-8')
        (home / '.env.local').write_text('UNITY_MCP_PROJECT=missing\nUNITY_MCP_HTTP_PORT=9003\nUNITY_MCP_TOKEN=stale-home-token', encoding='utf-8')
        self.assertEqual(self.run_doctor({})[0].project, self.project)
        self.assertTrue(self.urls[0].startswith('http://127.0.0.1:9001/'))
        self.assertTrue(all('authorization' not in headers for headers in self.headers))
        self.assertEqual(self.run_doctor({'UNITY_PROJECT_PATH': str(self.project / 'Packages')})[0].project, self.project)
        doctor, code, output = self.run_doctor({'UNITY_MCP_PROJECT': str(self.root / 'missing'), 'UNITY_PROJECT_PATH': str(self.project)})
        self.assertEqual(code, 1)
        self.assertIsNone(doctor.project)
        self.assertIn('UNPINNED', output)

    def test_nested_pin_and_enclosing_fallback_warn(self) -> None:
        doctor, code, output = self.run_doctor({'UNITY_MCP_PROJECT': str(self.project / 'Packages')})
        self.assertEqual((doctor.project, doctor.warnings, code), (self.project, 1, 0))
        self.assertIn('self-heals', output)
        doctor.root = self.project / 'Packages'
        doctor.environment = {}
        with contextlib.redirect_stdout(io.StringIO()):
            doctor.check_project()
        self.assertEqual(doctor.project, self.project)
        self.assertIsNone(enclosing_project(self.root))

    def test_offline_and_rpc_failure_severity(self) -> None:
        for method, code, failures, warnings in [('health', 1, 1, 0), ('initialize', 1, 1, 0), ('tools/list', 1, 1, 0), ('tools/call', 1, 1, 0)]:
            with self.subTest(method=method):
                self.error_method = method
                doctor, result, _ = self.run_doctor()
                self.assertEqual((result, doctor.failures, doctor.warnings), (code, failures, warnings))

    def test_empty_tools_wrong_editor_and_unpinned_bridge(self) -> None:
        self.responses['tools/list'] = {'result': {'tools': []}}
        self.responses['tools/call'] = {'result': {'content': [{'text': json.dumps({'projectPath': str(self.root)})}]}}
        self.responses['health'] = {'ok': True, 'cli': ['unity', 'mcp']}
        doctor, code, output = self.run_doctor()
        self.assertEqual((code, doctor.failures, doctor.warnings), (1, 2, 1))
        self.assertIn('WRONG EDITOR', output)

    def test_missing_manifest_and_editor_canary_warn(self) -> None:
        self.manifest.unlink()
        self.responses['tools/call'] = {'error': {'message': 'not running'}}
        doctor, code, _ = self.run_doctor()
        self.assertEqual((code, doctor.failures, doctor.warnings), (1, 1, 1))
        self.manifest.write_text('{"dependencies":{}}', encoding='utf-8')
        self.assertEqual(self.run_doctor()[1], 1)

    def test_cli_failure_is_reported_without_stopping_bridge_checks(self) -> None:
        doctor = Doctor(self.root, self.root, {'UNITY_MCP_PROJECT': str(self.project), 'DATAVIZ_IN_CONTAINER': '0'}, self.raise_command_error, self.request)
        with contextlib.redirect_stdout(io.StringIO()):
            code = doctor.run()
        self.assertEqual((code, doctor.failures, doctor.warnings), (1, 1, 1))
        self.assertEqual(len(self.calls), 4)

    def raise_command_error(self, arguments: List[str]) -> str:
        raise OSError('missing')

    def test_http_transport_encodes_authenticated_json(self) -> None:
        with patch('unity_mcp_doctor.urlopen') as opener:
            opener.return_value.__enter__.return_value = io.BytesIO(b'{"result":{"tools":[]}}')
            result = request_json('http://127.0.0.1:9020/mcp', {'authorization': 'Bearer private'}, {'id': 1})
            request = opener.call_args.args[0]
            self.assertEqual(json.loads(request.data), {'id': 1})
            self.assertEqual(request.get_header('Authorization'), 'Bearer private')
            self.assertEqual(request.get_method(), 'POST')
            self.assertEqual(opener.call_args.kwargs['timeout'], 30)
            self.assertEqual(result, {'result': {'tools': []}})

    def test_environment_parser_preserves_literal_values(self) -> None:
        path = self.root / '.env.local'
        self.assertEqual(read_env_local(path), {})
        path.write_text('\ufeff# comment\n bad\n=invalid\n export A = "x=y"\nB=\'quoted\'\nC=literal # text', encoding='utf-8')
        self.assertEqual(read_env_local(path), {'A': 'x=y', 'B': 'quoted', 'C': 'literal # text'})

    def test_container_uses_host_bridge_and_windows_project_paths(self) -> None:
        self.responses['tools/call'] = {'result': {'content': [{'text': json.dumps({'projectPath': 'C:/Code/Project'})}]}}
        doctor, code, output = self.run_doctor({'DATAVIZ_IN_CONTAINER': '1', 'UNITY_MCP_PROJECT': 'c:\\code\\project'})
        self.assertEqual((code, doctor.failures), (0, 0))
        self.assertTrue(self.urls[0].startswith('http://host.docker.internal:9020/'))
        self.assertNotIn('Unity CLI on PATH', output)

    def test_custom_endpoint_and_rpc_errors_fail(self) -> None:
        for method in ['initialize', 'tools/list', 'tools/call']:
            with self.subTest(method=method):
                original = self.responses[method]
                self.responses[method] = {'error': {'code': -32000, 'message': 'CLI unavailable'}}
                doctor, code, output = self.run_doctor({'DATAVIZ_IN_CONTAINER': '1', 'UNITY_MCP_HTTP_URL': 'http://bridge:9999/custom'})
                self.assertEqual(code, 1)
                self.assertTrue(doctor.failures)
                self.assertIn('CLI unavailable', output)
                self.assertEqual(self.urls[0], 'http://bridge:9999/healthz')
                self.responses[method] = original

    def test_editor_tool_error_and_missing_server_info_fail(self) -> None:
        self.responses['tools/call'] = {'result': {'isError': True, 'content': [{'text': 'not running'}]}}
        self.assertEqual(self.run_doctor()[1], 1)
        self.responses['initialize'] = {'result': {}}
        self.assertEqual(self.run_doctor()[1], 1)


if __name__ == '__main__':
    unittest.main()
