#!/usr/bin/env python3
"""Check the Unity CLI project pin and HTTP MCP bridge (uv run entry point)."""
from __future__ import annotations

import io
import json
import ntpath
import os
import subprocess
import sys
from pathlib import Path
from typing import Callable, Dict, List, Mapping, Optional, Protocol, Union, cast
from urllib.error import HTTPError
from urllib.request import Request, urlopen

Json = Union[None, bool, int, float, str, List['Json'], Dict[str, 'Json']]
JsonObject = Dict[str, Json]
REPO_ROOT = Path(__file__).resolve().parent.parent


class ReconfigurableOutput(Protocol):
    def reconfigure(self, *, encoding: str) -> None: ...


def object_value(value: Json) -> JsonObject:
    return value if isinstance(value, dict) else {}


def read_env_local(path: Path) -> Dict[str, str]:
    try:
        text = path.read_text(encoding='utf-8')
    except (OSError, UnicodeError):
        return {}
    result: Dict[str, str] = {}
    for raw in text.lstrip("\ufeff").splitlines():
        line = raw.strip()
        if not line or line.startswith('#') or '=' not in line:
            continue
        key, value = (part.strip() for part in line.split('=', 1))
        if key.startswith('export '):
            key = key[7:].strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1]
        if key:
            result[key] = value
    return result


def enclosing_project(start: Path) -> Optional[Path]:
    directory = start.resolve()
    for _ in range(32):
        if (directory / 'ProjectSettings' / 'ProjectVersion.txt').is_file():
            return directory
        parent = directory.parent
        if parent == directory:
            break
        directory = parent
    return None


def unity_command(arguments: List[str]) -> str:
    local = {**read_env_local(Path.home() / '.unity-mcp' / '.env.local'), **read_env_local(REPO_ROOT / '.env.local')}
    executable = os.environ.get('UNITY_CLI') or local.get('UNITY_CLI') or 'unity'
    return subprocess.check_output([executable, *arguments], text=True, encoding='utf-8').strip()


def request_json(url: str, headers: Dict[str, str], payload: Optional[JsonObject], timeout: float = 30) -> JsonObject:
    data = None if payload is None else json.dumps(payload).encode('utf-8')
    try:
        with urlopen(Request(url, data=data, headers=headers), timeout=timeout) as response:
            body = response.read()
            return object_value(cast(Json, json.loads(body))) if body else {}
    except HTTPError as error:
        # urllib raises before the error body is read, so the exception text
        # alone carries only the status. The bridge's error JSON names the
        # reason (token mismatch, bad project pin), which is the diagnostic
        # this doctor exists to surface.
        detail = error.read().decode('utf-8', errors='replace').strip()
        raise ValueError(f'HTTP {error.code} from {url}: {detail[:300]}') from error


class Doctor:
    def __init__(
        self, root: Path, home: Path, environment: Mapping[str, str],
        command: Callable[[List[str]], str] = unity_command,
        request: Callable[[str, Dict[str, str], Optional[JsonObject]], JsonObject] = request_json,
    ) -> None:
        self.root = root
        self.environment = environment
        host_local = read_env_local(home / '.unity-mcp' / '.env.local')
        self.local = {key: value for key, value in host_local.items()
                      if key in ('UNITY_CLI', 'UNITY_MCP_PROJECT', 'UNITY_PROJECT_PATH', 'UNITY_MCP_CLI_ARGS')}
        self.local.update(read_env_local(root / '.env.local'))
        self.command = command
        self.request = request
        self.failures = 0
        self.warnings = 0
        self.project: Optional[Path] = None
        container_flag = environment.get('DATAVIZ_IN_CONTAINER')
        self.remote = container_flag == '1' if container_flag is not None else Path('/.dockerenv').exists() or Path('/run/.containerenv').exists()

    def fail(self, message: str) -> None:
        self.failures += 1
        print(f'❌ {message}')

    def warn(self, message: str) -> None:
        self.warnings += 1
        print(f'⚠️  {message}')

    def ok(self, message: str) -> None:
        print(f'✅ {message}')

    def finish(self) -> int:
        result = 'Bridge stack healthy.' if self.failures == 0 else 'Address the ❌ items above.'
        print(f'\nDoctor summary: {self.failures} failure(s), {self.warnings} warning(s). {result}')
        return 0 if self.failures == 0 else 1

    def check_project(self) -> None:
        configured = (self.environment.get('UNITY_MCP_PROJECT') or self.environment.get('UNITY_PROJECT_PATH')
                      or self.local.get('UNITY_MCP_PROJECT') or self.local.get('UNITY_PROJECT_PATH') or '')
        if configured:
            candidate = Path(configured).resolve()
            self.project = enclosing_project(candidate)
            if self.project == candidate:
                self.ok(f'Project pin resolves to a Unity project root: {candidate}')
            elif self.project:
                self.warn(f"Configured project '{configured}' is not a project root; the bridge self-heals "
                          f"to the enclosing project '{self.project}'. Fix the config value.")
            else:
                self.fail(f"Configured project '{configured}' is not a Unity project root and no enclosing "
                          'project exists; the bridge would start UNPINNED (wrong-editor risk).')
        else:
            self.project = enclosing_project(self.root)
            if self.project:
                self.warn(f"No project pin configured; the bridge falls back to the enclosing Unity project "
                          f"'{self.project}'. Set UNITY_MCP_PROJECT in .env.local to be explicit.")
            else:
                self.fail('No project pin configured and no enclosing Unity project found; '
                          'set UNITY_MCP_PROJECT in .env.local to the Unity project root.')

    def check_editor(self) -> None:
        if self.project is None:
            return
        try:
            manifest = object_value(cast(Json, json.loads((self.project / 'Packages' / 'manifest.json').read_text(encoding='utf-8'))))
            key = next((key for key in object_value(manifest.get('dependencies')).keys() if 'pipeline' in key), None)
            if key:
                self.ok(f'Pipeline package present in project manifest: {key}')
            else:
                self.fail('com.unity.pipeline missing from the project manifest; run '
                          f'unity pipeline install --project-path {self.project} (unity mcp tools need it).')
        except (OSError, ValueError) as error:
            self.warn(f'Could not read project Packages/manifest.json: {error}')
        try:
            status = object_value(cast(Json, json.loads(self.command(['status', '--format', 'json']))))
            instances = object_value(status.get('data')).get('instances')
            editors = [str(object_value(item).get('project', '')) for item in instances] if isinstance(instances, list) else []
            if not editors:
                self.warn('No Unity Editor is running; editor-dependent tools will fail until one is. '
                          f'Start it with: unity open {self.project}')
            elif not any(Path(editor).resolve() == self.project for editor in editors):
                print(f"ℹ️  `unity status` only lists editor(s) for: {', '.join(editors)}. It does not list every reachable "
                      'Editor; the editor_status canary below is authoritative for whether the pinned project is drivable.')
            else:
                self.ok('A running Editor belongs to the pinned project.')
        except (OSError, ValueError, subprocess.SubprocessError):
            self.warn('Could not query `unity status` for running editors.')

    def check_bridge(self) -> None:
        port = self.environment.get('UNITY_MCP_HTTP_PORT') or self.local.get('UNITY_MCP_HTTP_PORT') or '9020'
        host = 'host.docker.internal' if self.remote else '127.0.0.1'
        endpoint = (self.environment.get('UNITY_MCP_HTTP_URL') or self.local.get('UNITY_MCP_HTTP_URL') or f'http://{host}:{port}/mcp').rstrip('/')
        base = endpoint.rsplit('/', 1)[0]
        try:
            health = self.request(base + '/healthz', {}, None)
            self.ok(f"Bridge healthz: ok={str(health.get('ok')).lower()} cli={json.dumps(health.get('cli'))}")
        except (OSError, ValueError) as error:
            self.fail(f'Bridge HTTP health failed on {base}: {error}. Check the host /healthz URL, bridge port, and firewall; '
                      'start it on the host with: npm run unity:mcp:host')
            return
        headers = {'content-type': 'application/json', 'accept': 'application/json, text/event-stream'}
        token = self.environment.get('UNITY_MCP_TOKEN') or self.local.get('UNITY_MCP_TOKEN') or ''
        if token:
            headers['authorization'] = f'Bearer {token}'
        cli = health.get('cli')
        if cli and '--project-path' not in str(cli):
            self.warn('Bridge CLI command has no --project-path pin; it may attach to whichever Editor is running. '
                      'Restart the bridge with UNITY_MCP_PROJECT set.')

        def rpc(identifier: int, method: str, params: JsonObject) -> JsonObject:
            result = self.request(endpoint, headers, {'jsonrpc': '2.0', 'id': identifier, 'method': method, 'params': params})
            if 'error' in result:
                raise ValueError(json.dumps(result['error']))
            if not isinstance(result.get('result'), dict):
                raise ValueError(f'{method} returned no result object')
            return result

        try:
            init = rpc(1, 'initialize', {'protocolVersion': '2025-03-26', 'capabilities': {},
                                       'clientInfo': {'name': 'unity-mcp-doctor', 'version': '0.1.0'}})
            if not object_value(init.get('result')).get('serverInfo'):
                raise ValueError('initialize returned no serverInfo')
            server = object_value(init.get('result')).get('serverInfo', init.get('error'))
            self.ok(f'MCP initialize: server={json.dumps(server)}')
        except (OSError, ValueError) as error:
            self.fail(f'MCP initialize failed: {error}')
            return
        try:
            self.request(endpoint, headers, {'jsonrpc': '2.0', 'method': 'notifications/initialized'})
            tools = object_value(rpc(2, 'tools/list', {}).get('result')).get('tools')
            count = len(tools) if isinstance(tools, list) else 0
            if count == 0:
                self.fail('tools/list returned 0 tools.')
            else:
                self.ok(f'tools/list: {count} tools available.')
        except (OSError, ValueError) as error:
            self.fail(f'tools/list failed: {error}')
        try:
            call = rpc(3, 'tools/call', {'name': 'editor_status', 'arguments': {}})
            if object_value(call.get('result')).get('isError'):
                raise ValueError('editor_status returned isError=true')
            content = object_value(call.get('result')).get('content')
            text = object_value(content[0]).get('text') if isinstance(content, list) and content else json.dumps(call.get('error'))
            text = text if isinstance(text, str) else json.dumps(text)
            try:
                status = object_value(cast(Json, json.loads(text)))
            except ValueError:
                status = {}
            driven = status.get('projectPath')
            if isinstance(driven, str) and driven:
                expected = (self.environment.get('UNITY_MCP_PROJECT') or self.environment.get('UNITY_PROJECT_PATH')
                            or self.local.get('UNITY_MCP_PROJECT') or self.local.get('UNITY_PROJECT_PATH') or '') if self.remote else str(self.project or '')
                # Windows host paths are opaque to Linux. Compare them with
                # Windows rules instead of resolving C:\\... under /workspaces.
                normalize: Callable[[str], str] = (lambda value: ntpath.normcase(ntpath.normpath(value))) if ntpath.splitdrive(driven)[0] else os.path.normpath
                actual = normalize(driven)
                if expected and actual == normalize(expected):
                    self.ok(f'editor_status drives the pinned project: {driven}')
                elif not expected and self.remote:
                    self.ok(f'editor_status drives host project: {driven}')
                else:
                    self.fail(f"WRONG EDITOR: the MCP server drives '{actual}' but the pinned project is "
                              f"'{expected}'. Restart the bridge after fixing UNITY_MCP_PROJECT.")
            else:
                self.fail('editor_status did not return a project (editor for the pinned project likely not running): '
                          + ' '.join(text[:200].split()))
        except (OSError, ValueError) as error:
            self.fail(f'editor_status canary failed: {error}')

    def run(self) -> int:
        if self.remote:
            print('ℹ️  Container client: Unity CLI and project paths belong to the host; checking through the bridge.')
        else:
            try:
                self.ok(f"Unity CLI on PATH: {self.command(['--version'])}")
            except (OSError, subprocess.SubprocessError):
                self.fail('Unity CLI not runnable (`unity --version` failed). Install it or set UNITY_CLI; '
                          'the bridge spawns `unity mcp`.')
            self.check_project()
            self.check_editor()
        self.check_bridge()
        return self.finish()


if __name__ == '__main__':
    # Keep Windows console diagnostics readable even with its legacy codepage.
    output = cast(ReconfigurableOutput, sys.stdout)
    if isinstance(sys.stdout, io.TextIOWrapper):
        output.reconfigure(encoding='utf-8')
    sys.exit(Doctor(REPO_ROOT, Path.home(), os.environ).run())
