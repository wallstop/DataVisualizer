#!/usr/bin/env python3
"""Persist frontend MCP JSON state using private atomic replacement (issue #171)."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence, TypedDict, cast

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / '.devcontainer'))
from backend_state import object_value, read_object, write_atomic

JsonMap = Dict[str, Any]
PROVIDERS: List[JsonMap] = [
    {'name': 'zai', 'baseUrl': 'https://api.z.ai/api/paas/v4', 'apiKey': '${ZAI_API_KEY}',
     'models': ['glm-5.3', 'glm-5.3-flash'], 'timeout': 300000},
    {'name': 'openrouter', 'baseUrl': 'https://openrouter.ai/api/v1', 'apiKey': '${OPENROUTER_API_KEY}',
     'models': ['openai/gpt-5-mini'], 'timeout': 300000},
]


class Outcome(TypedDict):
    changed: bool
    error: str


def as_object(value: object) -> JsonMap:
    return cast(JsonMap, object_value(value, 'Configuration field'))


def encode(value: object, pretty: bool = False) -> str:
    return json.dumps(value, ensure_ascii=False, allow_nan=False, indent=2 if pretty else None,
                      separators=None if pretty else (',', ':'))


class JsonWriter:
    def __init__(self, workspace: Path, home: Path, nano_home: Path, check_only: bool = False) -> None:
        self.workspace = workspace
        self.home = home
        self.nano_home = nano_home
        self.check_only = check_only

    def read(self, path: Path) -> JsonMap:
        return cast(JsonMap, read_object(path)) if path.exists() else {}

    def persist(self, path: Path, before: JsonMap, after: JsonMap, mode: int = 0o600) -> bool:
        changed = encode(before) != encode(after)
        if changed and not self.check_only:
            path.parent.mkdir(parents=True, exist_ok=True)
            write_atomic(path, encode(after, pretty=True) + '\n', mode)
        return changed

    def merge_servers(self, path: Path, key: str, servers: JsonMap, mode: int = 0o600) -> bool:
        existing = self.read(path)
        bucket = {**as_object(existing.get(key)), **servers}
        return self.persist(path, existing, {**existing, key: bucket}, mode)

    def claude(self, servers: JsonMap) -> bool:
        mcp = self.merge_servers(self.workspace / '.mcp.json', 'mcpServers', servers)
        path = self.workspace / '.claude' / 'settings.local.json'
        state = self.read(path)
        preferences = self.persist(path, state, {**state, 'enableAllProjectMcpServers': True})
        path = self.home / '.claude.json'
        state = self.read(path)
        projects = as_object(state.get('projects'))
        entry = as_object(projects.get(str(self.workspace)))
        names: object = entry.get('enabledMcpjsonServers') or []
        if not isinstance(names, list) or not all(isinstance(name, str) for name in cast(Sequence[object], names)):
            raise ValueError('enabledMcpjsonServers must be an array of names')
        desired = {**entry, 'hasTrustDialogAccepted': True, 'enabledMcpjsonServers': sorted(set(cast(Sequence[str], names)) | set(servers))}
        approvals = self.persist(path, state, {**state, 'projects': {**projects, str(self.workspace): desired}})
        return any((mcp, preferences, approvals))

    def opencode(self, servers: JsonMap) -> bool:
        path = self.workspace / 'opencode.json'
        existing = self.read(path)
        mcp = dict(as_object(existing.get('mcp')))
        converted: JsonMap = {}
        for key in list(mcp):
            value = mcp[key]
            if key in ('servers', 'timeout') or not isinstance(value, dict):
                continue
            entry = cast(JsonMap, value)
            next_entry = {name: item for name, item in entry.items() if name not in ('enabled', 'timeout')}
            next_entry['disabled'] = entry.get('enabled') is False
            timeout = entry.get('timeout')
            if isinstance(timeout, (int, float)) and not isinstance(timeout, bool):
                next_entry['timeout'] = {'catalog': timeout, 'execution': timeout}
            converted[key] = next_entry
            del mcp[key]
        mcp['servers'] = {**converted, **as_object(mcp.get('servers')), **servers}
        return self.persist(path, existing, {**existing, 'mcp': mcp})

    def nanocoder(self, servers: JsonMap) -> bool:
        mcp = self.merge_servers(self.workspace / '.nanocoder' / 'mcp.json', 'mcpServers', servers)
        path = self.nano_home / 'nanocoder-preferences.json'
        state = self.read(path)
        directories: object = state.get('trustedDirectories') or []
        if not isinstance(directories, list) or not all(isinstance(item, str) for item in cast(Sequence[object], directories)):
            raise ValueError('trustedDirectories must be an array of paths')
        preferences = self.persist(path, state, {**state, 'trustedDirectories': sorted(set(cast(Sequence[str], directories)) | {str(self.workspace)})})
        path = self.workspace / 'agents.config.json'
        state = self.read(path)
        nano = as_object(state.get('nanocoder'))
        providers: object = nano.get('providers') or []
        if not isinstance(providers, list):
            raise ValueError('nanocoder providers must be an array')
        raw_providers = cast(Sequence[object], providers)
        if not all(isinstance(provider, dict) for provider in raw_providers):
            raise ValueError('nanocoder providers must contain only JSON objects')
        managed_names = {provider['name'] for provider in PROVIDERS}
        kept = [provider for provider in raw_providers if as_object(provider).get('name') not in managed_names]
        providers_changed = self.persist(path, state, {**state, 'nanocoder': {**nano, 'providers': [*PROVIDERS, *kept]}})
        return any((mcp, preferences, providers_changed))

    def write_all(self, managed: Mapping[str, JsonMap]) -> Dict[str, Outcome]:
        outcomes: Dict[str, Outcome] = {}
        for frontend in ('claude', 'opencode', 'nanocoder', 'vscode', 'cursor'):
            try:
                servers = managed[frontend]
                if frontend == 'claude':
                    changed = self.claude(servers)
                elif frontend == 'opencode':
                    changed = self.opencode(servers)
                elif frontend == 'nanocoder':
                    changed = self.nanocoder(servers)
                else:
                    path = self.workspace / ('.vscode' if frontend == 'vscode' else '.cursor') / 'mcp.json'
                    changed = self.merge_servers(path, 'servers' if frontend == 'vscode' else 'mcpServers', servers,
                                                 0o600 if frontend == 'vscode' else 0o644)
                outcomes[frontend] = {'changed': changed, 'error': ''}
            except (OSError, ValueError, TypeError, KeyError) as error:
                outcomes[frontend] = {'changed': False, 'error': str(error)}
        return outcomes


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--home', type=Path, required=True)
    parser.add_argument('--nano-home', type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    options = parser.parse_args(arguments)
    try:
        managed = as_object(json.loads(sys.stdin.buffer.read().decode('utf-8')))
        if set(managed) != {'claude', 'opencode', 'nanocoder', 'vscode', 'cursor'}:
            raise ValueError('Expected managed entries for all five JSON frontends')
        writer = JsonWriter(options.workspace, options.home, options.nano_home, options.check)
        outcomes = writer.write_all({name: as_object(value) for name, value in managed.items()})
        sys.stdout.buffer.write((encode(outcomes) + '\n').encode('utf-8'))
        return 0
    except (OSError, ValueError, TypeError) as error:
        sys.stderr.buffer.write(f'Cannot write MCP JSON configuration: {error}\n'.encode('utf-8', errors='replace'))
        return 1


if __name__ == '__main__':
    sys.exit(main())
