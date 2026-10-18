#!/usr/bin/env python3
"""Load literal environment-file assignments for Python startup tools (issue #171)."""
from __future__ import annotations

import re
from pathlib import Path
from typing import Dict, Mapping, Optional

MCP_CREDENTIAL_KEYS = frozenset({
    'GITHUB_PERSONAL_ACCESS_TOKEN', 'GITHUB_MCP_PAT', 'GITHUB_TOKEN',
    'ZAI_API_KEY', 'Z_AI_API_KEY', 'UNITY_MCP_TOKEN',
})


def load_environment(path: Path, environment: Mapping[str, str], prefer_file: Optional[bool] = None) -> Dict[str, str]:
    """Return a copy with file assignments merged using the sourced loader's rules.

    Values remain literal. Managed credentials may use file precedence; all
    other non-empty environment values win. Process entries in order so repeated
    keys follow the same precedence rule as repeated shell exports.
    """
    result = dict(environment)
    if not path.is_file():
        return result
    content = path.read_bytes().decode('utf-8', errors='surrogateescape')
    for raw in content.split('\n'):
        line = raw.removeprefix('\ufeff').strip()
        if not line or line.startswith('#') or '=' not in line:
            continue
        key, value = (part.strip() for part in line.split('=', 1))
        if key.startswith('export '):
            key = key[7:].strip()
        if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', key):
            continue
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1]
        file_wins = result.get('DATAVIZ_ENV_LOCAL_PRECEDENCE') == 'file' if prefer_file is None else prefer_file
        if not result.get(key) or (file_wins and key in MCP_CREDENTIAL_KEYS):
            result[key] = value
    return result
