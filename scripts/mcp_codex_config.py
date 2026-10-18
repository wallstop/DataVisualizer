#!/usr/bin/env python3
"""Persist Codex MCP state for the universal configurator (issue #171)."""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path
from typing import Any, Dict, Mapping, Optional, Sequence, Tuple, cast

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / '.devcontainer'))
from backend_state import write_private

TOML_BEGIN = '# >>> dataviz-mcp (managed by npm run mcp:sync) >>>'
TOML_END = '# <<< dataviz-mcp <<<'
MANAGED_BLOCK = re.compile(r'\n?' + re.escape(TOML_BEGIN) + r'[\s\S]*?' + re.escape(TOML_END) + r'\n?')


def read_text_or_empty(path: Path) -> str:
    try:
        return path.read_bytes().decode('utf-8')
    except OSError:
        return ''


def strip_managed_blocks(text: str) -> str:
    return MANAGED_BLOCK.sub('\n', text).rstrip()


def toml_string(value: str) -> str:
    """JSON basic-string escapes also encode the strings used in these TOML tables."""
    return json.dumps(value, ensure_ascii=False).replace('\x7f', r'\u007f')


def server_table(name: str, definition: Mapping[str, Any]) -> str:
    lines = [f'[mcp_servers.{name}]']
    if definition.get('url'):
        lines.append('url = ' + toml_string(definition['url']))
        if definition.get('bearer_token_env_var'):
            lines.append('bearer_token_env_var = ' + toml_string(definition['bearer_token_env_var']))
        if definition.get('http_headers'):
            lines.extend(('', f'[mcp_servers.{name}.http_headers]'))
            for header, value in definition['http_headers'].items():
                lines.append(toml_string(header) + ' = ' + toml_string(value))
    else:
        lines.append('command = ' + toml_string(definition['command']))
        lines.append('args = [' + ', '.join(toml_string(arg) for arg in definition['args']) + ']')
        if definition.get('env_vars'):
            lines.append('env_vars = [' + ', '.join(toml_string(value) for value in definition['env_vars']) + ']')
        if definition.get('startup_timeout_sec'):
            lines.append(f"startup_timeout_sec = {definition['startup_timeout_sec']}")
    return '\n'.join(lines)


def write_config(workspace: Path, home: Path, entries: Mapping[str, Mapping[str, Any]], check_only: bool = False) -> bool:
    """Replace only the managed block and this workspace's trust entry."""
    path = home / 'config.toml'
    existing = read_text_or_empty(path)
    trust_header = '[projects.' + toml_string(str(workspace)) + ']'
    trust_block = trust_header + '\ntrust_level = "trusted"'
    trust_entry = re.compile(r'\n?' + re.escape(trust_header) + r'(?:[^[]*)')
    stripped = trust_entry.sub('\n', strip_managed_blocks(existing)).rstrip()
    block = '\n'.join(('', TOML_BEGIN, *(server_table(name, definition) for name, definition in entries.items()), TOML_END, ''))
    next_text = stripped + block + trust_block + '\n'
    changed = next_text != existing
    if changed and not check_only:
        path.parent.mkdir(parents=True, exist_ok=True)
        write_private(path, next_text)
    return changed


def prune_workspace(workspace: Path, check_only: bool = False) -> Tuple[bool, str]:
    """Remove stale generated MCP tables while preserving user-owned workspace state."""
    path = workspace / '.codex' / 'config.toml'
    try:
        existing = path.read_bytes().decode('utf-8')
    except OSError:
        return False, ''
    remainder = strip_managed_blocks(existing)
    if not remainder.strip():
        if not check_only:
            path.unlink(missing_ok=True)
            try:
                path.parent.rmdir()
            except OSError:
                pass
        return True, 'stale managed block removed (MCP lives in $CODEX_HOME/config.toml; Codex 0.153+ merges this file)'
    if MANAGED_BLOCK.search(existing) is not None:
        if not check_only:
            write_private(path, remainder + '\n')
        return True, 'managed block stripped; user content kept'
    note = ('has user-defined [mcp_servers.*] entries — transports must match $CODEX_HOME/config.toml or Codex 0.153+ merge hard-fails startup'
            if '[mcp_servers.' in remainder else '')
    return False, note


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=('write', 'prune'))
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    options = parser.parse_args(arguments)
    try:
        if options.operation == 'prune':
            changed, note = prune_workspace(options.workspace, options.check)
        else:
            data: object = json.loads(sys.stdin.buffer.read().decode('utf-8'))
            if not isinstance(data, dict):
                raise ValueError('Managed servers must be a JSON object.')
            entries = cast(Dict[str, Any], data)
            if not all(isinstance(definition, dict) for definition in entries.values()):
                raise ValueError('Each server must be a JSON object.')
            home = Path(os.environ.get('CODEX_HOME') or Path.home() / '.codex')
            changed = write_config(options.workspace, home, entries, options.check)
            note = ''
        response = json.dumps({'changed': changed, 'note': note}, ensure_ascii=False) + '\n'
        sys.stdout.buffer.write(response.encode('utf-8'))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        sys.stderr.buffer.write(f'Cannot update Codex MCP configuration: {error}\n'.encode('utf-8', errors='replace'))
        return 1


if __name__ == '__main__':
    sys.exit(main())
