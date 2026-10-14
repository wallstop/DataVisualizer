#!/usr/bin/env python3
"""Exercise the real HTTP bridge with an isolated, deterministic CLI process."""
from __future__ import annotations

import concurrent.futures
import json
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from typing import Any, Dict, Optional
from urllib.error import HTTPError
from urllib.request import Request, urlopen

sys.dont_write_bytecode = True
REPO = Path(__file__).resolve().parents[2]
FAKE_CLI = r"""
const readline = require('node:readline');
readline.createInterface({ input: process.stdin }).on('line', line => {
    const message = JSON.parse(line);
    if (message.method === 'crash') {
        process.stdout.write('partial output');
        process.exit(1);
    }
    if (message.id === undefined) return;
    const result = message.method === 'initialize'
        ? { serverInfo: { name: 'fixture' }, capabilities: {}, protocolVersion: '2025-03-26' }
        : { method: message.method, params: message.params };
    setTimeout(() => process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: message.id, result }) + '\n'), 5);
});
"""


@unittest.skipUnless(shutil.which('node'), 'node is required')
class HostTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix='unity-mcp-host-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        script_dir = self.root / '.llm' / 'mcp'
        script_dir.mkdir(parents=True)
        self.script = script_dir / 'unity-mcp-host.mjs'
        shutil.copyfile(REPO / '.llm' / 'mcp' / self.script.name, self.script)
        (self.root / 'mcp').write_text(FAKE_CLI, encoding='utf-8')
        self.home = self.root / 'home'
        self.home.mkdir()
        self.project = self.root / 'Project'
        (self.project / 'ProjectSettings').mkdir(parents=True)
        (self.project / 'ProjectSettings' / 'ProjectVersion.txt').write_text('6000.5', encoding='utf-8')
        with socket.socket() as listener:
            listener.bind(('127.0.0.1', 0))
            self.port = listener.getsockname()[1]
        self.env = {key: value for key, value in os.environ.items() if not key.startswith('UNITY_')}
        self.env.update(HOME=str(self.home), USERPROFILE=str(self.home), UNITY_MCP_AUTO_SYNC='0')
        self.env_file = self.root / '.env.local'
        self.env_file.write_text(f'\ufeffexport UNITY_CLI="{shutil.which("node")}"\nUNITY_MCP_PROJECT="{self.project}"\nUNITY_MCP_TOKEN=fixture-secret\nUNITY_MCP_TIMEOUT_MS=1500\n', encoding='utf-8')
        self.log = (self.root / 'bridge.log').open('w+', encoding='utf-8')
        self.addCleanup(self.log.close)
        self.launch()
        self.addCleanup(self.stop)

    def launch(self, command: Optional[list[str]] = None) -> None:
        offset = len(self.logs())
        self.process = subprocess.Popen(command or self.command(), cwd=self.root, env=self.env, stdout=self.log, stderr=self.log)
        deadline = time.monotonic() + 10
        while 'Startup complete.' not in self.logs()[offset:]:
            if self.process.poll() is not None or deadline <= time.monotonic():
                self.stop()
                self.fail('Bridge did not start: ' + self.logs()[offset:])
            time.sleep(0.02)

    def command(self) -> list[str]:
        return [str(shutil.which('node')), str(self.script), '--host', '127.0.0.1', '--port', str(self.port)]

    def logs(self) -> str:
        self.log.flush()
        return (self.root / 'bridge.log').read_text(encoding='utf-8')

    def stop(self) -> None:
        if os.name == 'nt' and self.process.poll() is None:
            # Windows terminate kills only the parent, leaving the CLI child
            # holding the fixture directory and log open.
            subprocess.run(['taskkill', '/PID', str(self.process.pid), '/T', '/F'], capture_output=True, check=True)
        else:
            self.process.terminate()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=5)

    def http(self, route: str, payload: Optional[Dict[str, Any]] = None, token: str = 'fixture-secret', method: Optional[str] = None) -> tuple[int, Any]:
        headers = {'content-type': 'application/json', 'authorization': f'Bearer {token}'}
        data = None if payload is None else json.dumps(payload).encode()
        request = Request(f'http://127.0.0.1:{self.port}{route}', data=data, headers=headers, method=method)
        with urlopen(request, timeout=5) as response:
            body = response.read()
            return response.status, json.loads(body) if body else None

    def rpc(self, method: str, identifier: int = 7) -> Any:
        return self.http('/mcp', {'jsonrpc': '2.0', 'id': identifier, 'method': method, 'params': {}})[1]

    def test_auth_notifications_and_parallel_request_ids(self) -> None:
        health = self.http('/healthz')[1]
        self.assertEqual(health['bridge'], 'dataviz-unity-mcp')
        self.assertEqual(health['bridgePid'], self.process.pid)
        self.assertTrue(health['instanceId'])
        self.assertEqual(health['projectPath'], str(self.project))
        self.assertTrue(health['authRequired'])
        self.assertFalse(health['ok'])
        for method in ['POST', 'DELETE']:
            with self.assertRaises(HTTPError) as raised:
                self.http('/mcp', {}, token='wrong', method=method)
            self.assertEqual(raised.exception.code, 401)
        self.assertEqual(self.rpc('initialize')['result']['serverInfo']['name'], 'fixture')
        self.assertEqual(self.http('/mcp', {'jsonrpc': '2.0', 'method': 'notifications/initialized'}), (202, None))
        methods = ['first', 'second', 'third']
        with concurrent.futures.ThreadPoolExecutor() as executor:
            results = list(executor.map(self.rpc, methods))
        self.assertEqual([result['id'] for result in results], [7, 7, 7])
        self.assertEqual([result['result']['method'] for result in results], methods)

    def test_cli_crash_does_not_poison_restarted_stdout(self) -> None:
        self.rpc('initialize')
        result = self.rpc('crash')
        self.assertIn('error', result)
        retry = self.rpc('initialize', identifier=23)
        self.assertEqual(retry['id'], 23)
        self.assertIn('error', retry)
        time.sleep(1.05)
        self.assertEqual(self.rpc('initialize')['result']['serverInfo']['name'], 'fixture')
        self.assertTrue(self.http('/healthz')[1]['ok'])

    def test_workspace_transport_settings_override_legacy_host_file(self) -> None:
        self.stop()
        legacy = self.home / '.unity-mcp'
        legacy.mkdir()
        (legacy / '.env.local').write_text('UNITY_MCP_TOKEN=stale-home-token\nUNITY_MCP_HTTP_PORT=1\nUNITY_MCP_PROJECT=missing', encoding='utf-8')
        self.launch()
        health = self.http('/healthz')[1]
        self.assertEqual(health['projectPath'], str(self.project))
        self.assertEqual(self.rpc('initialize')['result']['serverInfo']['name'], 'fixture')
        self.stop()
        self.env_file.write_text(self.env_file.read_text(encoding='utf-8').replace('UNITY_MCP_TOKEN=fixture-secret', ''), encoding='utf-8')
        self.launch()
        self.assertFalse(self.http('/healthz')[1]['authRequired'])
        self.assertEqual(self.rpc('initialize')['result']['serverInfo']['name'], 'fixture')

    def test_default_wildcard_bind_verifies_its_own_http_instance(self) -> None:
        self.stop()
        self.env.update(HTTP_PROXY='http://127.0.0.1:1', ALL_PROXY='http://127.0.0.1:1')
        command = self.command()
        del command[2:4]  # Exercise the production default: all interfaces.
        self.launch(command)
        self.assertIn(f'Local HTTP self-check passed (bridge PID {self.process.pid},', self.logs())
        health = self.http('/healthz')[1]
        self.assertEqual(health['bridgePid'], self.process.pid)
        self.assertIn(health['instanceId'], self.logs())
        self.assertIsNone(health['pid'])  # Health does not start Unity.

    def test_loopback_occupant_forces_wildcard_bridge_to_another_port(self) -> None:
        self.stop()
        with socket.socket() as occupant:
            occupant.bind(('127.0.0.1', 0))
            self.port = occupant.getsockname()[1]
            occupant.listen(8)
            offset = len(self.logs())
            command = self.command()
            del command[2:4]
            self.launch(command)
            self.assertIn(f'Port {self.port} is busy', self.logs()[offset:])
            selected = next(line.split('=', 1)[1] for line in self.env_file.read_text(encoding='utf-8').splitlines() if line.startswith('UNITY_MCP_HTTP_PORT='))
            self.assertNotEqual(int(selected), self.port)
            self.port = int(selected)
            self.assertEqual(self.http('/healthz')[1]['bridgePid'], self.process.pid)
            self.assertEqual(self.rpc('initialize')['result']['serverInfo']['name'], 'fixture')

    def test_failed_self_check_exits_before_publishing_port(self) -> None:
        self.stop()
        preload = self.root / 'redirect-health.mjs'
        preload.write_text("import http from 'node:http'; const get = http.get; http.get = (options, callback) => get({ ...options, path: '/wrong-endpoint' }, callback);", encoding='utf-8')
        env = dict(self.env, NODE_OPTIONS=f'--import={preload.as_uri()}')
        before = self.env_file.read_text(encoding='utf-8')
        result = subprocess.run(self.command(), cwd=self.root, env=env, capture_output=True, text=True, timeout=8)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn('Local HTTP self-check failed', result.stderr)
        self.assertNotIn('Local HTTP self-check passed', result.stdout)
        self.assertEqual(before, self.env_file.read_text(encoding='utf-8'))

    def test_repeated_start_recognizes_absolute_cli_path(self) -> None:
        before = self.env_file.read_text(encoding='utf-8')
        result = subprocess.run(self.command(), cwd=self.root, env=self.env, capture_output=True, text=True, timeout=5)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('already running', result.stdout)
        self.assertEqual(before, self.env_file.read_text(encoding='utf-8'))

    def test_slow_config_sync_keeps_http_responsive_and_uses_selected_port(self) -> None:
        self.stop()
        configure = self.script.parent / 'configure.mjs'
        configure.write_text("import fs from 'node:fs'; fs.writeFileSync('sync-port', process.env.UNITY_MCP_HTTP_PORT); setTimeout(() => process.exit(0), 1800);", encoding='utf-8')
        self.env['UNITY_MCP_AUTO_SYNC'] = '1'
        self.env['UNITY_MCP_HTTP_PORT'] = '9020'
        self.env_file.write_text(self.env_file.read_text(encoding='utf-8').replace(f'UNITY_MCP_HTTP_PORT={self.port}', 'UNITY_MCP_HTTP_PORT=9020'), encoding='utf-8')
        self.process = subprocess.Popen(self.command(), cwd=self.root, env=self.env, stdout=self.log, stderr=self.log)
        deadline = time.monotonic() + 5
        port_file = self.root / 'sync-port'
        while not port_file.exists() and time.monotonic() < deadline:
            time.sleep(0.02)
        self.assertTrue(port_file.exists(), self.logs())
        start = time.monotonic()
        self.assertEqual(self.http('/healthz')[0], 200)
        self.assertLess(time.monotonic() - start, 1)
        self.assertEqual(port_file.read_text(encoding='utf-8'), str(self.port))
        time.sleep(1.8)


if __name__ == '__main__':
    unittest.main()
