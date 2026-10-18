#!/usr/bin/env python3
"""Resolve Unity HTTP bridge startup settings without starting Unity (issue #171)."""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path
from typing import Dict, List, Mapping, Optional, Sequence, TypedDict


class BridgeSettings(TypedDict):
    cliCommand: str
    cliArgs: List[str]
    projectPath: str
    token: str
    port: str
    timeout: str
    autoSync: str
    warnings: List[str]


def read_env_file(path: Path) -> Dict[str, str]:
    try:
        text = path.read_bytes().decode('utf-8', errors='replace').removeprefix('\ufeff')
    except OSError:
        return {}
    result: Dict[str, str] = {}
    for raw in text.split('\n'):
        line = raw.strip()
        if not line or line.startswith('#') or '=' not in line:
            continue
        key, value = (part.strip() for part in line.split('=', 1))
        key = re.sub(r'^export\s+', '', key)
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1]
        if key:
            result[key] = value
    return result


def is_project(path: Path) -> bool:
    try:
        return (path / 'ProjectSettings' / 'ProjectVersion.txt').is_file()
    except OSError:
        return False


def enclosing_project(path: Path) -> Optional[Path]:
    # Retain the logical workspace spelling; resolving symlinks changes the pin.
    directory = Path(os.path.abspath(path))
    for _ in range(32):
        if is_project(directory):
            return directory
        parent = directory.parent
        if parent == directory:
            break
        directory = parent
    return None


def resolve_settings(workspace: Path, home: Path, environment: Mapping[str, str]) -> BridgeSettings:
    host = read_env_file(home / '.unity-mcp' / '.env.local')
    local = {key: host[key] for key in ('UNITY_CLI', 'UNITY_MCP_PROJECT', 'UNITY_PROJECT_PATH', 'UNITY_MCP_CLI_ARGS') if key in host}
    workspace_values = read_env_file(workspace / '.env.local')
    local.update(workspace_values)

    def value(name: str, default: str = '') -> str:
        return environment.get(name) or local.get(name) or default

    configured = ''
    for source in (environment, workspace_values, host):
        configured = source.get('UNITY_MCP_PROJECT') or source.get('UNITY_PROJECT_PATH') or ''
        if configured:
            break
    warnings: List[str] = []
    project: Optional[Path] = None
    if configured:
        candidate = Path(os.path.abspath(configured))
        project = enclosing_project(candidate)
        if not is_project(candidate):
            if project is not None:
                warnings.append(
                    f"[unity-mcp] WARNING: UNITY_MCP_PROJECT '{configured}' is not a Unity "
                    f"project root (no ProjectSettings/ProjectVersion.txt); using enclosing project '{project}' instead."
                )
            else:
                warnings.append(
                    f"[unity-mcp] WARNING: UNITY_MCP_PROJECT '{configured}' is not a Unity "
                    "project root and no enclosing Unity project was found; starting WITHOUT "
                    "a project pin — the MCP server may attach to the wrong running Editor."
                )
    if project is None:
        project = enclosing_project(workspace)
        if project is not None:
            warnings.append(f"[unity-mcp] No project pin configured; using the enclosing Unity project of this workspace: '{project}'.")
    arguments = ['mcp']
    if project is not None:
        arguments.extend(('--project-path', str(project)))
    arguments.extend(part for part in value('UNITY_MCP_CLI_ARGS').split(' ') if part)
    return {
        'cliCommand': value('UNITY_CLI', 'unity'),
        'cliArgs': arguments,
        'projectPath': str(project) if project is not None else '',
        'token': value('UNITY_MCP_TOKEN'),
        'port': value('UNITY_MCP_HTTP_PORT', '9020'),
        'timeout': value('UNITY_MCP_TIMEOUT_MS', '180000'),
        'autoSync': value('UNITY_MCP_AUTO_SYNC'),
        'warnings': warnings,
    }


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--home', type=Path, required=True)
    options = parser.parse_args(arguments)
    try:
        settings = resolve_settings(options.workspace, options.home, os.environ)
        sys.stdout.buffer.write((json.dumps(settings, ensure_ascii=False) + '\n').encode('utf-8'))
        return 0
    except (OSError, ValueError):
        print('Cannot resolve Unity MCP startup settings; check environment-file access.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
