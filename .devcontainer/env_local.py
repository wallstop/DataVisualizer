#!/usr/bin/env python3
"""Load literal environment-file assignments for Python startup tools (issue #171)."""
from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path
from typing import Dict, Iterator, Mapping, Optional, Tuple

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
    for key, value, _ in loaded_assignments(path, environment, prefer_file):
        result[key] = value
    return result


def loaded_assignments(path: Path, environment: Mapping[str, str], prefer_file: Optional[bool] = None) -> Iterator[Tuple[str, str, bool]]:
    """Yield accepted assignments and whether they replace a non-empty value."""
    result = dict(environment)
    if not path.is_file():
        return
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
            yield key, value, file_wins and key in MCP_CREDENTIAL_KEYS


def main() -> int:
    """Emit NUL-delimited key/value/overwrite records and an empty success record.

    Build the complete payload before writing so a read or encoding failure
    cannot apply a partial environment in the calling shell. Never emit shell
    code: values such as command substitutions remain literal data.
    """
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', type=Path)
    args = parser.parse_args()
    try:
        inherited = dict(os.environ)
        records = []
        for key, value, overwrite in loaded_assignments(args.path, inherited):
            if '\0' in value:
                raise ValueError('Environment values cannot contain NUL bytes.')
            fields = (key.encode('utf-8'), value.encode('utf-8', errors='surrogateescape'), b'1' if overwrite else b'0')
            records.append(b'\0'.join(fields) + b'\0')
        sys.stdout.buffer.write(b''.join(records) + b'\0\0\0')
        sys.stdout.buffer.flush()
    except (OSError, ValueError):
        print('Cannot load environment assignments; check file access and encoding.', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
