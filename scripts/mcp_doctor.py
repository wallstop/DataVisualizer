#!/usr/bin/env python3
"""Check MCP credentials and connectivity; repair proven credential aliases on request."""
from __future__ import annotations

import argparse
import io
import json
import os
import queue
import re
import shutil
import signal
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import Dict, List, Mapping, Optional, cast
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from unity_mcp_doctor import Json, JsonObject, ReconfigurableOutput, object_value, read_env_local, request_json

INITIALIZE: JsonObject = {'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {
    'protocolVersion': '2025-03-26', 'capabilities': {}, 'clientInfo': {'name': 'mcp-doctor', 'version': '0.1.0'}}}


class Doctor:
    def __init__(self, root: Path, environment: Mapping[str, str]) -> None:
        self.root = root
        self.environment = environment
        self.local = read_env_local(root / '.env.local')
        self.failures = 0
        self.warnings = 0
        try:
            self.timeout = float(environment.get('DATAVIZ_MCP_PROBE_TIMEOUT_MS') or '20000') / 1000 or 20
        except ValueError:
            self.timeout = 20
        self.secrets = [self.env(key) for key in ('GITHUB_PERSONAL_ACCESS_TOKEN', 'GITHUB_MCP_PAT', 'ZAI_API_KEY', 'Z_AI_API_KEY', 'UNITY_MCP_TOKEN') if self.env(key)]

    def env(self, name: str) -> str:
        return self.environment.get(name) or self.local.get(name) or ''

    def report(self, kind: str, message: str) -> None:
        if kind == '❌':
            self.failures += 1
        elif kind == '⚠️':
            self.warnings += 1
        for secret in self.secrets:
            message = message.replace(secret, '[redacted]')
        print(f'{kind} {message}')

    def probe_remote(self, label: str, url: str, token: str) -> bool:
        headers = {'content-type': 'application/json', 'accept': 'application/json, text/event-stream'}
        if token:
            headers['authorization'] = f'Bearer {token}'
        try:
            try:
                with urlopen(Request(url, data=json.dumps(INITIALIZE).encode(), headers=headers), timeout=self.timeout) as response:
                    status = response.status
            except HTTPError as error:
                status = error.code
                error.close()
            if status in (401, 403):
                self.report('❌', f'{label}: HTTP {status} (auth rejected — token invalid or env var empty at client startup)')
                return False
            self.report('✅', f'{label}: HTTP {status}')
            return True
        except (OSError, ValueError) as error:
            self.report('❌', f'{label}: {error}')
            return False

    def rewrite_key(self, key: str, value: str) -> bool:
        path = self.root / '.env.local'
        try:
            text = path.read_bytes().decode('utf-8')
            pattern = rf'^(\ufeff?[ \t]*(?:export[ \t]+)?{re.escape(key)}[ \t]*=)[^\r\n]*'
            updated, count = re.subn(pattern, lambda match: match[1] + value, text, count=1, flags=re.MULTILINE)
            if not count:
                return False
            path.write_bytes(updated.encode('utf-8'))
            return True
        except (OSError, UnicodeError):
            return False

    def check_credentials(self, repair: bool, skip: bool) -> str:
        print('--- Credentials ---')
        for label, primary, alternate, default_url, url_key in [
            ('GitHub', 'GITHUB_PERSONAL_ACCESS_TOKEN', 'GITHUB_MCP_PAT', 'https://api.githubcopilot.com/mcp/', 'DATAVIZ_MCP_GITHUB_PROBE_URL'),
            ('Z.ai', 'ZAI_API_KEY', 'Z_AI_API_KEY', 'https://api.z.ai/api/mcp/web_search_prime/mcp', 'DATAVIZ_MCP_ZAI_PROBE_URL'),
        ]:
            first, second = self.env(primary), self.env(alternate)
            if first or second:
                self.report('✅', f'{label} key resolved from {primary if first else alternate} (names checked: {primary}, {alternate})')
            else:
                self.report('❌', f'No {label} key. Add {primary} (or {alternate}) to .env.local, run `npm run mcp:sync`, then restart agents from a fresh shell.')
            divergent = bool(first and second and first != second)
            if divergent:
                self.report('❌', f'{label} credentials divergent: {primary} and {alternate} are both set to different values. Run `npm run mcp:doctor --repair`.')
            url = self.environment.get(url_key) or default_url
            if not skip:
                self.probe_remote(label, url, first or second)
            if repair and divergent:
                first_ok = self.probe_remote(primary, url, first)
                second_ok = self.probe_remote(alternate, url, second)
                if not first_ok and not second_ok:
                    self.report('ℹ️', f'{label}: neither name authenticates — issue a new key and set both names in .env.local.')
                    continue
                live, stale, value = (primary, alternate, first) if first_ok else (alternate, primary, second)
                self.report('ℹ️', f'{label}: {live} authenticates. Repairing .env.local ({stale} := {live}).')
                if self.rewrite_key(stale, value):
                    self.report('ℹ️', f'{label}: wrote {stale} in .env.local. Restart agent CLIs from a fresh shell.')
                else:
                    self.report('ℹ️', f'{label}: could not update .env.local (key {stale} not present or file unwritable). Set it manually.')
        if not self.env('UNITY_MCP_TOKEN'):
            self.report('⚠️', 'UNITY_MCP_TOKEN not set — the unity bridge accepts unauthenticated calls.')
        return self.env('ZAI_API_KEY') or self.env('Z_AI_API_KEY')

    def check_config(self) -> None:
        print('--- Config flavor (shared machine-local files) ---')
        try:
            config = object_value(cast(Json, json.loads((self.root / 'opencode.json').read_text(encoding='utf-8'))))
            servers = object_value(object_value(config.get('mcp')).get('servers'))
        except (OSError, ValueError):
            servers = {}
        if not servers:
            self.report('⚠️', 'opencode.json not found or has no mcp.servers block; run `npm run mcp:sync`.')
            return
        url = object_value(servers.get('unity')).get('url')
        if isinstance(url, str):
            self.report('❌' if '127.0.0.1' in url else '✅', f'opencode.json unity URL: {url}; run `npm run mcp:sync` if unreachable from containers.')
        command = object_value(servers.get('zai-image')).get('command')
        text = ' '.join(str(item) for item in command) if isinstance(command, list) else str(command or '')
        if text:
            absolute = bool(re.match(r'^[A-Za-z]:\\|^/', text))
            self.report('❌' if absolute else '✅', f'opencode.json zai-image command: {text}; use an environment-agnostic binary with `npm run mcp:sync`.')

    def spawn_probe(self, label: str, command: List[str], overrides: Dict[str, str], timeout: float = 25) -> None:
        lines: queue.Queue[Optional[str]] = queue.Queue()
        shell = os.name == 'nt' and Path(command[0]).suffix.lower() in ('.cmd', '.bat')
        try:
            with subprocess.Popen(subprocess.list2cmdline(command) if shell else command, cwd=self.root,
                                  env={**self.environment, **overrides}, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL, text=True, encoding='utf-8', errors='replace', shell=shell, start_new_session=os.name != 'nt') as child:
                assert child.stdout is not None and child.stdin is not None
                def read_lines() -> None:
                    assert child.stdout is not None
                    for line in child.stdout:
                        lines.put(line)
                    lines.put(None)
                worker = threading.Thread(target=read_lines, daemon=True)
                worker.start()
                try:
                    child.stdin.write(json.dumps(INITIALIZE) + '\n')
                    child.stdin.flush()
                    deadline = time.monotonic() + timeout
                    while True:
                        line = lines.get(timeout=max(0, deadline - time.monotonic()))
                        if line is None:
                            raise ValueError('process exited before MCP initialize')
                        try:
                            message = object_value(cast(Json, json.loads(line)))
                        except ValueError:
                            continue
                        info = object_value(message.get('result')).get('serverInfo')
                        if info:
                            self.report('✅', f'{label}: serverInfo={json.dumps(info)}')
                            return
                        if message.get('error'):
                            raise ValueError(json.dumps(message['error'])[:200])
                finally:
                    if os.name == 'nt' and child.poll() is None:
                        subprocess.run(['taskkill', '/PID', str(child.pid), '/T', '/F'], capture_output=True, check=False)
                    elif os.name != 'nt':
                        try:
                            os.killpg(child.pid, signal.SIGKILL)
                        except ProcessLookupError:
                            pass
                    if child.poll() is None:
                        child.kill()
                    child.wait()
                    worker.join(timeout=1)
        except queue.Empty:
            self.report('❌', f'{label}: TIMEOUT (no response in {timeout:g}s)')
        except (OSError, ValueError) as error:
            self.report('❌', f'{label}: spawn failed ({error})')

    def check_bridge(self) -> None:
        print('--- Unity bridge ---')
        port = self.env('UNITY_MCP_HTTP_PORT') or '9020'
        url = (self.env('UNITY_MCP_HTTP_URL') or f'http://host.docker.internal:{port}/mcp').rstrip('/')
        base = url.rsplit('/', 1)[0]
        try:
            request_json(base + '/healthz', {}, None, timeout=5)
            token = self.env('UNITY_MCP_TOKEN')
            headers = {'content-type': 'application/json', 'accept': 'application/json, text/event-stream'}
            if token:
                headers['authorization'] = f'Bearer {token}'
            body = request_json(url, headers, INITIALIZE)
            info = object_value(body.get('result')).get('serverInfo')
            if not info:
                raise ValueError('MCP initialize failed: ' + json.dumps(body)[:200])
            self.report('✅', f'unity bridge: {url} initialize OK serverInfo={json.dumps(info)}')
        except (OSError, ValueError) as error:
            self.report('❌', f'unity bridge failed at {base}: {error}. On the host run: npm run unity:mcp:host (then unity:mcp:doctor for depth).')

    def run(self, repair: bool) -> int:
        container = self.environment.get('DATAVIZ_IN_CONTAINER') == '1' or Path('/.dockerenv').exists() or Path('/run/.containerenv').exists()
        print(f'Environment: {"devcontainer" if container else "host"} · workspace: {self.root}')
        skip = self.environment.get('DATAVIZ_MCP_STUB_SKIP_PROBES') == '1'
        zai = self.check_credentials(repair, skip)
        self.check_config()
        if skip:
            self.report('ℹ️', 'Skipping endpoint, stdio, and unity-bridge probes (DATAVIZ_MCP_STUB_SKIP_PROBES=1) — credential and config checks only.')
        else:
            print('--- stdio spawn probes (cwd = workspace root) ---')
            if zai:
                self.spawn_probe('zai-vision', [shutil.which('npx') or 'npx', '-y', '@z_ai/mcp-server'], {'ZAI_API_KEY': zai, 'Z_AI_MODE': self.env('Z_AI_MODE') or 'ZAI'})
            self.spawn_probe('zai-image', ['uv', 'run', '--no-project', '.llm/mcp/zai_image_mcp.py'], {'ZAI_API_KEY': zai})
            self.check_bridge()
        result = 'All MCP servers connectable.' if not self.failures else 'Address the ❌ items above.'
        print(f'\nDoctor summary: {self.failures} failure(s), {self.warnings} warning(s). {result}')
        if self.failures:
            self.report('ℹ️', 'Reminder: {env:VAR} references expand at AGENT STARTUP — after changing .env.local or running mcp:sync, restart agents from a fresh shell.')
        return 1 if self.failures else 0


if __name__ == '__main__':
    output = cast(ReconfigurableOutput, sys.stdout)
    if isinstance(sys.stdout, io.TextIOWrapper):
        output.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repair', action='store_true')
    options = parser.parse_args()
    root = Path(os.environ.get('DATAVIZ_MCP_WORKSPACE') or Path(__file__).resolve().parent.parent).resolve()
    sys.exit(Doctor(root, os.environ).run(options.repair))
