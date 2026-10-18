#!/usr/bin/env python3
"""Synchronize credential aliases without exposing values in output (issue #171)."""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from typing import Dict, Mapping, Optional, Sequence

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / '.devcontainer'))
from backend_state import write_private

PAIRS = (
    ('zai', 'ZAI_API_KEY', 'Z_AI_API_KEY'),
    ('github', 'GITHUB_PERSONAL_ACCESS_TOKEN', 'GITHUB_MCP_PAT'),
)


def read_values(text: str) -> Dict[str, str]:
    values: Dict[str, str] = {}
    for raw in text.removeprefix('\ufeff').replace('\r\n', '\n').replace('\r', '\n').split('\n'):
        line = raw.strip()
        if not line or line.startswith('#') or '=' not in line:
            continue
        key, value = (part.strip() for part in line.split('=', 1))
        if key.startswith('export '):
            key = key[7:].strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1]
        if key:
            values[key] = value
    return values


def synchronize(workspace: Path, environment: Mapping[str, str], check_only: bool = False) -> Dict[str, str]:
    path = workspace / '.env.local'
    try:
        text = path.read_bytes().decode('utf-8')
    except FileNotFoundError:
        text = '# Machine-local credentials (gitignored).\n'
    local = read_values(text)
    statuses: Dict[str, str] = {}
    pending: Dict[str, str] = {}
    for label, primary, alternate in PAIRS:
        first = environment.get(primary) or local.get(primary) or ''
        second = environment.get(alternate) or local.get(alternate) or ''
        if not first and not second:
            statuses[label] = 'missing'
        elif first and second:
            statuses[label] = 'configured' if first == second else 'divergent'
        else:
            statuses[label] = 'alias-pending' if check_only else 'aliased'
            pending[alternate if first else primary] = first or second
    if pending and not check_only:
        if any('\n' in value or '\r' in value or '\0' in value for value in pending.values()):
            raise ValueError('Credential aliases must contain one literal line')
        newline = next((ending for ending in ('\r\n', '\r', '\n') if ending in text), '\n')
        if text and not text.endswith(('\n', '\r')):
            text += newline
        text += newline + '# Keep both credential names available to clients.' + newline
        for key, value in pending.items():
            quoted = value != value.strip() or (value[0] in "\"'" and value[-1] == value[0])
            literal = "'" + value + "'" if quoted else value
            text += key + '=' + literal + newline
        path.parent.mkdir(parents=True, exist_ok=True)
        write_private(path, text)
    return statuses


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', required=True, type=Path)
    parser.add_argument('--check', action='store_true')
    options = parser.parse_args(arguments)
    try:
        statuses = synchronize(options.workspace, os.environ, options.check)
        sys.stdout.buffer.write((json.dumps(statuses) + '\n').encode('utf-8'))
        return 0
    except (OSError, ValueError):
        print('Cannot synchronize MCP credential aliases; check file access and literal UTF-8 values.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
