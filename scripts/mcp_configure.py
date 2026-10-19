#!/usr/bin/env python3
"""Configure every managed MCP frontend through typed Python (issue #171)."""
from __future__ import annotations

import json
import math
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, cast

sys.dont_write_bytecode = True
from mcp_aliases import read_values

ROOT = Path(__file__).resolve().parent.parent
JsonMap = Dict[str, Any]


def run_json(script: str, arguments: Sequence[str], payload: Optional[object] = None) -> JsonMap:
    result = subprocess.run([sys.executable, str(ROOT / 'scripts' / script), *arguments],
                            input=None if payload is None else json.dumps(payload, ensure_ascii=False).encode('utf-8'),
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    if result.returncode:
        raise ValueError(result.stderr.decode('utf-8', errors='replace').strip() or f'{script} failed')
    value: object = json.loads(result.stdout.decode('utf-8'))
    if not isinstance(value, dict):
        raise ValueError(f'{script} returned no configuration object')
    return cast(JsonMap, value)


def opencode_v2(definition: Mapping[str, Any]) -> JsonMap:
    value = {key: item for key, item in definition.items() if key not in ('enabled', 'timeout')}
    value['disabled'] = definition.get('enabled') is False
    timeout = definition.get('timeout')
    if isinstance(timeout, (int, float)) and not isinstance(timeout, bool):
        value['timeout'] = {'catalog': timeout, 'execution': timeout}
    return value


def configure(workspace: Path, check_only: bool) -> int:
    try:
        local = read_values((workspace / '.env.local').read_bytes().decode('utf-8', errors='replace'))
    except OSError:
        local = {}

    def env(name: str, default: str = '') -> str:
        return os.environ.get(name) or local.get(name) or default

    flags = ['--check'] if check_only else []
    workspace_args = ['--workspace', str(workspace)]
    aliases = run_json('mcp_aliases.py', [*workspace_args, *flags])
    uv = shutil.which('uv')
    uvx = shutil.which('uvx')
    try:
        port = float(env('UNITY_MCP_HTTP_PORT'))
        port_text = (str(int(port)) if port.is_integer() else str(port)) if math.isfinite(port) else '9020'
    except ValueError:
        port_text = '9020'
    if port_text == '0':
        port_text = '9020'
    unity_url = env('UNITY_MCP_HTTP_URL', f'http://host.docker.internal:{port_text}/mcp')
    token = env('UNITY_MCP_TOKEN')
    catalog = run_json('mcp_catalog.py', [], {
        'unity_url': unity_url, 'unity_auth': bool(token), 'has_uv': uv is not None,
        'has_uvx': uvx is not None, 'uv_bin': uv or 'uv',
        'image_script': str(workspace / '.llm' / 'mcp' / 'zai_image_mcp.py'),
    })
    managed: Dict[str, JsonMap] = {name: {} for name in ('claude', 'codex', 'opencode', 'nanocoder', 'vscode', 'cursor')}
    for name, definition in catalog.items():
        for frontend in ('claude', 'codex', 'nanocoder'):
            managed[frontend][name] = definition[frontend]
        managed['opencode'][name] = opencode_v2(definition['opencode'])
        vscode = dict(definition['vscode'])
        command = vscode.get('command')
        if isinstance(command, list):
            vscode.update(command=command[0], args=command[1:])
        managed['vscode'][name] = vscode
        if 'cursor' in definition:
            managed['cursor'][name] = definition['cursor']

    json_outcomes: JsonMap = {}
    json_failure = ''
    home = Path(os.environ.get('HOME') or Path.home())
    nano_home = Path(os.environ.get('NANOCODER_CONFIG_DIR') or Path.home() / '.config' / 'nanocoder')
    try:
        json_outcomes = run_json('mcp_json_config.py', [*workspace_args, '--home', str(home), '--nano-home', str(nano_home), *flags],
                                 {key: value for key, value in managed.items() if key != 'codex'})
    except (OSError, ValueError) as error:
        json_failure = str(error)

    plan = [
        ('Claude Code (.mcp.json + project approval)', 'claude'),
        (f"Codex ({os.environ.get('CODEX_HOME') or '~/.codex'}/config.toml managed block)", 'codex'),
        ('Codex workspace (.codex/config.toml pruned — Codex 0.153+ merges it)', 'prune'),
        ('OpenCode (opencode.json mcp.servers block)', 'opencode'),
        ('Nanocoder (.nanocoder/mcp.json + trust seed + providers)', 'nanocoder'),
        ('VS Code (.vscode/mcp.json)', 'vscode'),
        ('Cursor (.cursor/mcp.json, project scope)', 'cursor'),
    ]
    failed = False
    results: List[str] = []
    for label, frontend in plan:
        try:
            if frontend == 'codex':
                outcome = run_json('mcp_codex_config.py', ['write', *workspace_args, *flags], managed['codex'])
            elif frontend == 'prune':
                outcome = run_json('mcp_codex_config.py', ['prune', *workspace_args, *flags], {})
            else:
                if json_failure:
                    raise ValueError(json_failure)
                outcome = json_outcomes[frontend]
                if outcome['error']:
                    raise ValueError(outcome['error'])
            changed = outcome['changed']
            results.append(f"{'✅' if changed else '·'} {label}{' updated' if changed and not check_only else ''}")
            if frontend == 'prune' and outcome.get('note'):
                results.append(f"{'✅' if changed else '·'}   ↳ {outcome['note']}")
        except (OSError, ValueError, KeyError, TypeError) as error:
            failed = True
            results.append(f'❌ {label}: {error}')

    lines = ['', '╔════════════════════════════════════════════════════════════════╗',
             '║                MCP Server Configuration                        ║',
             '╚════════════════════════════════════════════════════════════════╝',
             *('  ' + result for result in results), '', '  Servers: ' + ', '.join(catalog)]
    if uvx is None:
        lines.append('  (fetch/git skipped: uvx not found on PATH)')
    if uv is None:
        lines.append('  (zai-image skipped: uv not found on PATH)')
    lines.append('  UNITY_MCP_TOKEN: ' + ('configured' if token else 'OPEN (no token)'))
    for label, key, primary, alternate in (
        ('ZAI_API_KEY', 'zai', 'ZAI_API_KEY', 'Z_AI_API_KEY'),
        ('GITHUB_PERSONAL_ACCESS_TOKEN', 'github', 'GITHUB_PERSONAL_ACCESS_TOKEN', 'GITHUB_MCP_PAT'),
    ):
        status = aliases[key]
        if status == 'missing':
            status = 'MISSING (zai-* servers will fail auth)' if key == 'zai' else 'MISSING (github MCP will fail auth with 401)'
        elif status == 'divergent':
            status = (f'DIVERGENT ({primary} != {alternate} — consumers read different values; '
                      'run `npm run mcp:doctor --repair`)')
        lines.append(f'  {label}: {status}')
    container = Path('/.dockerenv').exists() or Path('/run/.containerenv').exists() or os.environ.get('DATAVIZ_IN_CONTAINER') == '1'
    lines.extend([
        f"  Environment: {'devcontainer' if container else 'host'} (shared configs use environment-agnostic URLs; bridge: {unity_url})",
        f'  Unity MCP: official Unity CLI (~140 tools) via {unity_url} — start the bridge with `npm run unity:mcp:host` on the host.',
        '  Managed Cursor shares the bridge; unmanaged host clients can run `unity mcp configure <client>` for native stdio.',
        '', '  Restart your agent CLIs (or run /mcp) to pick up changes.', '',
    ])
    sys.stdout.buffer.write(('\n'.join(lines) + '\n').encode('utf-8'))
    return 1 if failed else 0


def main(arguments: Optional[Sequence[str]] = None) -> int:
    workspace = Path(os.path.abspath(os.environ.get('DATAVIZ_MCP_WORKSPACE') or ROOT))
    try:
        return configure(workspace, '--check' in (sys.argv[1:] if arguments is None else arguments))
    except (OSError, ValueError, KeyError, TypeError) as error:
        sys.stderr.buffer.write(f'Cannot sync MCP configuration: {error}\n'.encode('utf-8', errors='replace'))
        return 1


if __name__ == '__main__':
    sys.exit(main())
