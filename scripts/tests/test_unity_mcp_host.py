#!/usr/bin/env python3
"""Exercise the real HTTP bridge with an isolated, deterministic CLI process."""
from __future__ import annotations

import concurrent.futures
import contextlib
import http.client
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
from typing import Any, Optional
from unittest import mock
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from unittest.mock import Mock, patch

sys.dont_write_bytecode = True
REPO = Path(__file__).resolve().parents[2]


def cleanup_fixture_directory(path: Path) -> None:
    # A terminating Windows CLI child can retain its current-directory handle briefly.
    deadline = time.monotonic() + 2.0
    while True:
        try:
            shutil.rmtree(path)
            return
        except PermissionError as error:
            if getattr(error, 'winerror', None) != 32 or deadline <= time.monotonic():
                raise
            time.sleep(0.01)


class FixtureDirectoryCleanupTests(unittest.TestCase):
    def test_cleanup_retries_only_temporary_sharing_violations(self) -> None:
        for winerror in (None, 5, 32):
            with self.subTest(winerror=winerror):
                error = PermissionError('fixture lock')
                if winerror is not None:
                    setattr(error, 'winerror', winerror)
                directory = tempfile.TemporaryDirectory()
                with directory, mock.patch('shutil.rmtree', side_effect=[error, None]) as remove, mock.patch('time.sleep') as sleep:
                    if winerror == 32:
                        cleanup_fixture_directory(Path(directory.name))
                        self.assertEqual(remove.call_count, 2)
                        sleep.assert_called_once_with(0.01)
                    else:
                        with self.assertRaises(PermissionError) as raised:
                            cleanup_fixture_directory(Path(directory.name))
                        self.assertIs(raised.exception, error)
                        remove.assert_called_once_with(Path(directory.name))
                        sleep.assert_not_called()

    def test_cleanup_preserves_a_sharing_violation_at_the_deadline(self) -> None:
        for clock, attempts in (([0.0, 2.0], 1), ([0.0, 0.0, 2.0], 2)):
            with self.subTest(attempts=attempts):
                error = PermissionError('persistent fixture lock')
                setattr(error, 'winerror', 32)
                directory = tempfile.TemporaryDirectory()
                with directory, mock.patch('shutil.rmtree', side_effect=error) as remove:
                    with mock.patch('time.monotonic', side_effect=clock), mock.patch('time.sleep') as sleep:
                        with self.assertRaises(PermissionError) as raised:
                            cleanup_fixture_directory(Path(directory.name))
                        self.assertIs(raised.exception, error)
                        self.assertEqual(remove.call_count, attempts)
                        self.assertEqual(sleep.call_count, attempts - 1)

    def test_cleanup_removes_the_directory_after_a_transient_lock(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'fixture.txt').write_text('owned fixture', encoding='utf-8')
            remove = shutil.rmtree
            error = PermissionError('transient lock')
            setattr(error, 'winerror', 32)
            attempts = 0

            def locked_once(path: Path) -> None:
                nonlocal attempts
                attempts += 1
                if attempts == 1:
                    raise error
                remove(path)

            with mock.patch('shutil.rmtree', side_effect=locked_once):
                cleanup_fixture_directory(root)
            self.assertEqual(attempts, 2)
            self.assertFalse(root.exists(), 'A successful retry must actually remove the fixture.')

    @unittest.skipUnless(os.name == 'nt', 'Windows locks process current directories')
    def test_cleanup_waits_for_a_real_process_directory_lock(self) -> None:
        with contextlib.ExitStack() as cleanup:
            directory = tempfile.TemporaryDirectory(prefix='fixture-cwd-lock-')
            cleanup.callback(directory.cleanup)
            root = Path(directory.name)
            holder = cleanup.enter_context(subprocess.Popen(
                [sys.executable, '-c', "from pathlib import Path; import sys; Path('holder-ready').touch(); sys.stdin.buffer.read(1)"],
                cwd=root, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
            ))
            cleanup.callback(holder.kill)
            deadline = time.monotonic() + 5
            while not (root / 'holder-ready').exists() and holder.poll() is None and time.monotonic() < deadline:
                time.sleep(0.01)
            self.assertTrue((root / 'holder-ready').exists(), 'The subprocess must hold the fixture directory.')
            remove = shutil.rmtree
            observed_errors: list[int] = []

            def release_after_locked_cleanup(path: Path) -> None:
                try:
                    remove(path)
                except PermissionError as error:
                    observed_errors.append(getattr(error, 'winerror', 0))
                    _, errors = holder.communicate(input=b'release', timeout=5)
                    self.assertEqual(holder.returncode, 0, errors.decode('utf-8', errors='replace'))
                    raise

            with mock.patch('shutil.rmtree', side_effect=release_after_locked_cleanup):
                cleanup_fixture_directory(Path(directory.name))
            self.assertEqual(observed_errors, [32])
            self.assertFalse(root.exists())

FAKE_CLI = r"""
import json, os, sys, time
for line in sys.stdin:
    message = json.loads(line)
    if message.get('method') == 'crash':
        sys.stdout.write('partial output')
        sys.stdout.flush()
        os._exit(1)
    if message.get('method') == 'never-respond':
        continue
    if 'id' not in message:
        continue
    result = dict(serverInfo=dict(name='fixture'), capabilities={}, protocolVersion='2025-03-26') if message.get('method') == 'initialize' else dict(method=message.get('method'), params=message.get('params'))
    time.sleep(0.005)
    print(json.dumps(dict(jsonrpc='2.0', id=message['id'], result=result)), flush=True)
"""

PYTHON_CONFIG_SYNC_FIXTURE = """
import os, time
from pathlib import Path
Path('sync-port.pending').write_text(os.environ['UNITY_MCP_HTTP_PORT'])
Path('sync-port.pending').replace('sync-port')
time.sleep(1.8)
"""


CONFIG_SYNC_FIXTURE = """
import fs from 'node:fs';
fs.writeFileSync('sync-port.pending', process.env.UNITY_MCP_HTTP_PORT);
fs.renameSync('sync-port.pending', 'sync-port');
setTimeout(() => process.exit(0), 1800);
"""


@unittest.skipUnless(shutil.which('node'), 'node is required')
class ConfigSyncPublicationTests(unittest.TestCase):
    def test_port_is_published_only_after_the_write_completes(self) -> None:
        with contextlib.ExitStack() as cleanup:
            directory = cleanup.enter_context(tempfile.TemporaryDirectory(prefix='config-sync-publication-'))
            root = Path(directory)
            preload = root / 'pause-write.mjs'
            preload.write_text("""
import fs from 'node:fs';
const write = fs.writeFileSync;
fs.writeFileSync = (path, data, ...options) => {
    write(path, '');
    write('writer-ready', '');
    fs.readSync(0, Buffer.alloc(1), 0, 1, null);
    return write(path, data, ...options);
};
""", encoding='utf-8')
            fixture = root / 'configure.mjs'
            # Publication is synchronous; this test does not need the bridge's background delay.
            fixture.write_text(CONFIG_SYNC_FIXTURE + '\nprocess.exit(0);', encoding='utf-8')
            env = dict(os.environ, UNITY_MCP_HTTP_PORT='53386')
            process = cleanup.enter_context(subprocess.Popen(
                [str(shutil.which('node')), '--import', preload.as_uri(), str(fixture)],
                cwd=root, env=env, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
            ))
            cleanup.callback(process.kill)
            deadline = time.monotonic() + 5
            ready = root / 'writer-ready'
            while not ready.exists() and process.poll() is None and time.monotonic() < deadline:
                time.sleep(0.01)
            if not ready.exists():
                exit_code = process.poll()
                process.kill()
                _, errors = process.communicate(timeout=5)
                self.fail(f'Writer did not reach the paused empty-file state; exit={exit_code}: {errors!r}')
            port_file = root / 'sync-port'
            self.assertFalse(port_file.exists(), 'Readers must not see an incomplete selected port.')
            _, errors = process.communicate(input=b'continue', timeout=5)
            self.assertEqual(process.returncode, 0, errors.decode('utf-8', errors='replace'))
            self.assertEqual(port_file.read_text(encoding='utf-8'), '53386')


@unittest.skipUnless(shutil.which('node'), 'node is required')
class HostTests(unittest.TestCase):
    python_bridge = False

    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix='unity-mcp-host-'))
        self.addCleanup(cleanup_fixture_directory, self.root)
        script_dir = self.root / '.llm' / 'mcp'
        script_dir.mkdir(parents=True)
        (self.root / 'scripts').mkdir()
        source = REPO / ('scripts/unity_mcp_host.py' if self.python_bridge else '.llm/mcp/unity-mcp-host.mjs')
        self.script = self.root / source.relative_to(REPO)
        shutil.copyfile(source, self.script)
        shutil.copyfile(REPO / 'scripts' / 'unity_mcp_settings.py', self.root / 'scripts' / 'unity_mcp_settings.py')
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
        self.env_file.write_text(f'\ufeffexport UNITY_CLI="{sys.executable}"\nUNITY_MCP_PROJECT="{self.project}"\nUNITY_MCP_TOKEN=fixture-secret\nUNITY_MCP_TIMEOUT_MS=1500\n', encoding='utf-8')
        self.log = (self.root / 'bridge.log').open('w+', encoding='utf-8')
        self.addCleanup(self.log.close)
        self.node_options: list[str] = []
        if not self.python_bridge and self._testMethodName == 'test_auth_notifications_and_parallel_request_ids':
            observer = self.root / 'observe-response-end.mjs'
            observer.write_text("""
import http from 'node:http';
const createServer = http.createServer;
http.createServer = (handler) => createServer((request, response) => {
    const end = response.end.bind(response);
    response.end = (...args) => {
        if (request.headers['x-dataviz-drain-probe']) {
            console.log('[drain-probe]' + JSON.stringify({
                probe: request.headers['x-dataviz-drain-probe'],
                drained: request.readableEnded,
            }));
        }
        return end(...args);
    };
    handler(request, response);
});
""", encoding='utf-8')
            self.node_options = ['--import', observer.as_uri()]
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
        launcher = [sys.executable] if self.python_bridge else [str(shutil.which('node')), *self.node_options]
        return [*launcher, str(self.script), '--host', '127.0.0.1', '--port', str(self.port)]

    def logs(self) -> str:
        self.log.flush()
        return (self.root / 'bridge.log').read_text(encoding='utf-8')

    def stop(self) -> None:
        taskkill_failure: Optional[str] = None
        if os.name == 'nt' and self.process.poll() is None:
            # Windows terminate kills only the parent, leaving the CLI child
            # holding the fixture directory and log open.
            command = ['taskkill', '/PID', str(self.process.pid), '/T', '/F']
            result = subprocess.run(command, capture_output=True, check=False)
            # A partial taskkill failure can precede the parent's observed exit.
            if result.returncode != 0:
                taskkill_failure = f"taskkill failed for bridge PID {self.process.pid}: {result.stdout!r} {result.stderr!r}"
        else:
            self.process.terminate()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            try:
                self.process.kill()
                self.process.wait(timeout=5)
            except (OSError, subprocess.TimeoutExpired) as cleanup_error:
                if taskkill_failure is not None:
                    self.fail(f"{taskkill_failure}; forced cleanup failed: {cleanup_error}")
                raise
            if taskkill_failure is not None:
                self.fail(taskkill_failure)

    def http(self, route: str, payload: Optional[object] = None, token: str = 'fixture-secret', method: Optional[str] = None) -> tuple[int, Any]:
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
            with self.subTest(method=method):
                with self.assertRaises(HTTPError) as raised:
                    self.http('/mcp', {}, token='wrong', method=method)
                with raised.exception as response:
                    self.assertEqual(response.code, 401)
                    self.assertEqual(json.loads(response.read()), {'error': 'unauthorized'})
        self.assertEqual(self.rpc('initialize')['result']['serverInfo']['name'], 'fixture')
        startup_marker = os.environ.get('DATAVIZ_HARNESS_STARTUP_MARKER')
        if startup_marker:
            print('\n' + startup_marker, flush=True)
        self.assertEqual(self.http('/mcp', {'jsonrpc': '2.0', 'method': 'notifications/initialized'}), (202, None))
        methods = ['first', 'second', 'third']
        with concurrent.futures.ThreadPoolExecutor() as executor:
            results = list(executor.map(self.rpc, methods))
        self.assertEqual([result['id'] for result in results], [7, 7, 7])
        self.assertEqual([result['result']['method'] for result in results], methods)

        # Send the body only after reading the full early response, without a sleep.
        cases = [
            ('POST', '/mcp', 'wrong', 401),
            ('DELETE', '/mcp', 'wrong', 401),
            ('POST', '/missing', 'fixture-secret', 404),
            ('DELETE', '/mcp', 'fixture-secret', 204),
            ('PUT', '/mcp', 'fixture-secret', 405),
            ('POST', '/healthz', 'fixture-secret', 200),
        ]
        for method, route, token, status in cases:
            probe = f'{method}:{route}:{status}'
            with self.subTest(probe=probe):
                with socket.create_connection(('127.0.0.1', self.port), timeout=5) as client:
                    headers = (f'{method} {route} HTTP/1.1\r\nHost: 127.0.0.1\r\n'
                               f'Authorization: Bearer {token}\r\nX-Dataviz-Drain-Probe: {probe}\r\n'
                               'Content-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n')
                    client.sendall(headers.encode('ascii'))
                    with http.client.HTTPResponse(client) as response:
                        response.begin()
                        self.assertEqual(response.status, status)
                        body = response.read()
                        if status == 401:
                            self.assertEqual(json.loads(body), {'error': 'unauthorized'})
                        elif status == 200:
                            self.assertEqual(json.loads(body)['bridge'], 'dataviz-unity-mcp')
                        else:
                            self.assertEqual(body, b'')
                        if status == 405:
                            self.assertEqual(response.getheader('Allow'), 'POST, DELETE')
                    client.sendall(b'{}')
                    self.assertEqual(client.recv(1), b'', 'Close only after consuming the complete request.')
                observations = [json.loads(line.split('[drain-probe]', 1)[1])
                                for line in self.logs().splitlines() if '[drain-probe]' in line]
                if not self.python_bridge:
                    self.assertIn({'probe': probe, 'drained': True}, observations)

        # An abandoning client must leave the bridge responsive, without a retry.
        with socket.create_connection(('127.0.0.1', self.port), timeout=5) as client:
            client.sendall(b'POST /mcp HTTP/1.1\r\nHost: 127.0.0.1\r\nAuthorization: Bearer wrong\r\nContent-Length: 2\r\n\r\n')
            with http.client.HTTPResponse(client) as response:
                response.begin()
                self.assertEqual(response.status, 401)
                self.assertEqual(json.loads(response.read()), {'error': 'unauthorized'})
        self.assertEqual(self.http('/healthz')[0], 200)

    def test_timeout_parse_errors_and_batch_requests_keep_the_bridge_usable(self) -> None:
        response = self.rpc('never-respond')
        self.assertEqual(response['error']['code'], -32000)
        self.assertIn('Timed out after 1500ms', response['error']['message'])
        batch = [dict(jsonrpc='2.0', id='first', method='first'),
                 dict(jsonrpc='2.0', method='notifications/initialized'),
                 dict(jsonrpc='2.0', id='second', method='second')]
        status, result = self.http('/mcp', batch)
        self.assertEqual(status, 200)
        self.assertEqual([item['id'] for item in result], ['first', 'second'])
        request = Request(f'http://127.0.0.1:{self.port}/mcp', data=b'{',
                          headers={'Authorization': 'Bearer fixture-secret'})
        with self.assertRaises(HTTPError) as raised:
            urlopen(request, timeout=5)
        with raised.exception as error:
            self.assertEqual(error.code, 400)
            self.assertEqual(json.load(error)['error']['code'], -32700)
        self.assertEqual(self.rpc('initialize')['result']['serverInfo']['name'], 'fixture')

    def test_cli_crash_does_not_poison_restarted_stdout(self) -> None:
        self.rpc('initialize')
        result = self.rpc('crash')
        self.assertIn('error', result)
        retry = self.rpc('initialize', identifier=23)
        self.assertEqual(retry['id'], 23)
        self.assertIn('error', retry)
        deadline = time.monotonic() + 10
        recovered = self.rpc('initialize')
        while 'result' not in recovered and time.monotonic() < deadline:
            time.sleep(0.05)
            recovered = self.rpc('initialize')
        self.assertIn('result', recovered, f"Bridge did not recover: {recovered}; {self.logs()}")
        self.assertEqual(recovered['result']['serverInfo']['name'], 'fixture')
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

    def test_workspace_project_alias_precedes_a_legacy_primary_pin(self) -> None:
        self.stop()
        legacy_project = self.root / 'LegacyProject'
        (legacy_project / 'ProjectSettings').mkdir(parents=True)
        (legacy_project / 'ProjectSettings' / 'ProjectVersion.txt').write_text('6000.5', encoding='utf-8')
        legacy = self.home / '.unity-mcp'
        legacy.mkdir()
        (legacy / '.env.local').write_text(f'UNITY_MCP_PROJECT={legacy_project}\n', encoding='utf-8')
        content = self.env_file.read_text(encoding='utf-8').replace('UNITY_MCP_PROJECT=', 'UNITY_PROJECT_PATH=')
        self.env_file.write_text(content, encoding='utf-8')
        self.launch()
        self.assertEqual(self.http('/healthz')[1]['projectPath'], str(self.project))

    @unittest.skipUnless(os.name == 'posix', 'restricted PATH fixture requires POSIX symlinks')
    def test_settings_use_python_when_uv_is_unavailable(self) -> None:
        self.stop()
        binary = self.root / 'bin'
        binary.mkdir()
        (binary / 'python').symlink_to(sys.executable)
        self.env['PATH'] = str(binary)
        self.launch()
        self.assertEqual(self.http('/healthz')[1]['projectPath'], str(self.project))
        self.assertEqual(self.rpc('initialize')['result']['serverInfo']['name'], 'fixture')

    def test_invalid_settings_ports_fail_before_startup(self) -> None:
        self.stop()
        before = self.env_file.read_bytes()
        for value in ('0', '65536', '1.5', 'invalid'):
            with self.subTest(value=value):
                environment = {**self.env, 'UNITY_MCP_HTTP_PORT': value}
                result = subprocess.run(self.command()[:-2], cwd=self.root, env=environment, capture_output=True, timeout=10)
                self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
                self.assertIn(b'--port must be an integer', result.stderr)
                self.assertNotIn(b'Startup complete.', result.stdout)
                self.assertEqual(self.env_file.read_bytes(), before)

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
        if self.python_bridge:
            self.skipTest("Python startup failures are covered by route-injection unit tests.")
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

    def test_intercepted_docker_route_selects_and_publishes_a_verified_port(self) -> None:
        if self.python_bridge:
            self.skipTest("Python startup failures are covered by route-injection unit tests.")
        self.stop()
        for mode in ('wrong-instance', 'silent', 'missing-dns'):
            with self.subTest(mode=mode), contextlib.ExitStack() as cleanup:
                cleanup.callback(self.stop)
                preload = self.root / 'docker-route.mjs'
                preload.write_text("""
import http from 'node:http';
import os from 'node:os';
os.platform = () => 'win32';
const createServer = http.createServer;
http.createServer = (handler) => createServer((request, response) => {
    if (request.url === '/intercepted') {
        if (process.env.ROUTE_MODE === 'silent') return;
        response.writeHead(200, { 'content-type': 'application/json' });
        response.end(JSON.stringify({ instanceId: 'different-listener' }));
        return;
    }
    handler(request, response);
});
const get = http.get;
http.get = (options, callback) => {
    if (options.hostname !== 'host.docker.internal') return get(options, callback);
    if (process.env.ROUTE_MODE === 'missing-dns') {
        return get({ ...options, hostname: 'localhost', lookup: (_host, _options, done) => {
            const error = new Error('No Docker Desktop DNS');
            error.code = 'ENOTFOUND';
            done(error);
        } }, callback);
    }
    return get({ ...options, hostname: '127.0.0.1',
        path: String(options.port) === process.env.INTERCEPT_PORT ? '/intercepted' : options.path,
    }, callback);
};
""", encoding='utf-8')
                initial = self.port
                self.env.update(ROUTE_MODE=mode, INTERCEPT_PORT=str(initial))
                self.node_options = ['--import', preload.as_uri()]
                offset = len(self.logs())
                command = self.command()
                del command[-4:-2]  # Remove --host 127.0.0.1, retaining Node preload.
                self.launch(command)
                selected = next(line.split('=', 1)[1] for line in self.env_file.read_text(encoding='utf-8').splitlines() if line.startswith('UNITY_MCP_HTTP_PORT='))
                self.port = int(selected)
                if mode == 'missing-dns':
                    self.assertEqual(self.port, initial)
                    self.assertIn('only the local route was verified', self.logs()[offset:])
                else:
                    self.assertNotEqual(self.port, initial)
                    self.assertIn('Docker host route failed', self.logs()[offset:])
                    self.assertIn('Docker host route verified', self.logs()[offset:])
                self.assertEqual(self.http('/healthz')[1]['bridgePid'], self.process.pid)
                self.assertEqual(self.rpc('initialize')['result']['serverInfo']['name'], 'fixture')

    def test_existing_bridge_with_intercepted_docker_route_is_not_reused(self) -> None:
        if self.python_bridge:
            self.skipTest("Python startup failures are covered by route-injection unit tests.")
        old = self.process
        self.addCleanup(old.wait, 5)
        self.addCleanup(old.terminate)
        preload = self.root / 'existing-docker-route.mjs'
        preload.write_text("""
import http from 'node:http';
import os from 'node:os';
os.platform = () => 'win32';
const get = http.get;
http.get = (options, callback) => get(options.hostname === 'host.docker.internal'
    ? { ...options, hostname: '127.0.0.1',
        path: String(options.port) === process.env.INTERCEPT_PORT ? '/intercepted' : options.path }
    : options, callback);
""", encoding='utf-8')
        self.env['INTERCEPT_PORT'] = str(self.port)
        self.node_options = ['--import', preload.as_uri()]
        command = self.command()
        del command[-4:-2]
        initial = self.port
        offset = len(self.logs())
        self.launch(command)
        self.port = int(next(line.split('=', 1)[1] for line in self.env_file.read_text(encoding='utf-8').splitlines() if line.startswith('UNITY_MCP_HTTP_PORT=')))
        self.assertNotEqual(self.port, initial)
        self.assertNotIn('nothing to do', self.logs()[offset:])
        self.assertIn('Docker host route verified', self.logs()[offset:])
        self.assertEqual(self.http('/healthz')[1]['bridgePid'], self.process.pid)

    def test_repeated_start_recognizes_absolute_cli_path(self) -> None:
        before = self.env_file.read_text(encoding='utf-8')
        result = subprocess.run(self.command(), cwd=self.root, env=self.env, capture_output=True, text=True, timeout=5)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('already running', result.stdout)
        self.assertEqual(before, self.env_file.read_text(encoding='utf-8'))

    def test_slow_config_sync_keeps_http_responsive_and_uses_selected_port(self) -> None:
        self.stop()
        configure = self.root / 'scripts/mcp_configure.py' if self.python_bridge else self.script.parent / 'configure.mjs'
        configure.write_text(PYTHON_CONFIG_SYNC_FIXTURE if self.python_bridge else CONFIG_SYNC_FIXTURE, encoding='utf-8')
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


class PythonHostTests(HostTests):
    __unittest_skip__ = False
    python_bridge = True


class CleanupTests(unittest.TestCase):
    def test_taskkill_failure_requires_a_live_process(self) -> None:
        for state in ('already-exited', 'exits-while-waiting', 'remains-live'):
            with self.subTest(state=state):
                host = HostTests('test_repeated_start_recognizes_absolute_cli_path')
                process = Mock(pid=123)
                process.poll.side_effect = [None, 0 if state == 'already-exited' else None]
                process.wait.return_value = 0
                if state == 'remains-live':
                    process.wait.side_effect = [subprocess.TimeoutExpired('bridge', 5), 0]
                result = subprocess.CompletedProcess(['taskkill'], 255, stdout=b'parent terminated', stderr=b'child exited')
                def kill(command: list[str], *, capture_output: bool, check: bool) -> subprocess.CompletedProcess[bytes]:
                    if check:
                        raise subprocess.CalledProcessError(255, command, stderr=result.stderr)
                    return result
                with patch.object(host, 'process', process, create=True), patch('os.name', 'nt'), patch('subprocess.run', side_effect=kill):
                    if state == 'remains-live':
                        with self.assertRaisesRegex(AssertionError, 'parent terminated.*child exited'):
                            host.stop()
                        self.assertEqual(process.wait.call_count, 2)
                        process.kill.assert_called_once_with()
                    else:
                        host.stop()
                        process.wait.assert_called_once_with(timeout=5)
                        process.kill.assert_not_called()

    def test_taskkill_failure_keeps_forced_cleanup_errors(self) -> None:
        for failure in ('kill', 'wait'):
            with self.subTest(failure=failure):
                host = HostTests('test_repeated_start_recognizes_absolute_cli_path')
                process = Mock(pid=123)
                process.poll.return_value = None
                process.wait.side_effect = [subprocess.TimeoutExpired('bridge', 5), subprocess.TimeoutExpired('forced cleanup', 5)]
                if failure == 'kill':
                    process.kill.side_effect = OSError('cleanup denied')
                result = subprocess.CompletedProcess(['taskkill'], 255, stdout=b'parent termination', stderr=b'child termination')
                with patch.object(host, 'process', process, create=True), patch('os.name', 'nt'), patch('subprocess.run', return_value=result):
                    with self.assertRaisesRegex(AssertionError, 'parent termination.*child termination.*forced cleanup failed') as raised:
                        host.stop()
                    expected = 'cleanup denied' if failure == 'kill' else 'forced cleanup'
                    self.assertIn(expected, str(raised.exception))
                    process.kill.assert_called_once_with()
                    self.assertEqual(process.wait.call_count, 1 if failure == 'kill' else 2)

    def test_successful_taskkill_retains_forced_cleanup(self) -> None:
        host = HostTests('test_repeated_start_recognizes_absolute_cli_path')
        process = Mock(pid=123)
        process.poll.return_value = None
        process.wait.side_effect = [subprocess.TimeoutExpired('bridge', 5), 0]
        result = subprocess.CompletedProcess(['taskkill'], 0, stdout=b'terminated', stderr=b'')
        with patch.object(host, 'process', process, create=True), patch('os.name', 'nt'), patch('subprocess.run', return_value=result):
            host.stop()
        process.kill.assert_called_once_with()
        self.assertEqual(process.wait.call_args_list, [mock.call(timeout=5), mock.call(timeout=5)])


if __name__ == '__main__':
    unittest.main()
