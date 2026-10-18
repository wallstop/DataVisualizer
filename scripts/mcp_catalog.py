#!/usr/bin/env python3
"""Build placeholder-only frontend server definitions for the MCP configurator."""
from __future__ import annotations

import json
import sys
from typing import Any, Dict

Definition = Dict[str, Any]
Catalog = Dict[str, Definition]
PREFIX = 'set -a; . .devcontainer/env-local.sh 2>/dev/null; load_env_local .env.local 2>/dev/null; set +a'


def wrapper(command: str) -> Definition:
    return {'command': ['bash', '-c', PREFIX + '; exec ' + command]}


def remote(url: str, key: str, timeout: int, summary: str) -> Definition:
    return {
        'summary': summary,
        'claude': {'type': 'http', 'url': url, 'headers': {'Authorization': 'Bearer ${' + key + ':-}'}},
        'codex': {'url': url, 'bearer_token_env_var': key},
        'opencode': {'type': 'remote', 'url': url, 'enabled': True, 'timeout': timeout,
                     'headers': {'Authorization': 'Bearer {env:' + key + '}'}},
        'nanocoder': {'transport': 'http', 'url': url, 'headers': {'Authorization': 'Bearer ${' + key + ':-}'}},
        'vscode': wrapper('npx -y mcp-remote ' + url + ' --header "Authorization:${' + key + '}"'),
        'cursor': {'url': url, 'headers': {'Authorization': 'Bearer ${env:' + key + '}'}}
    }


def local(command: str, args: list[str], timeout: int, summary: str) -> Definition:
    return {
        'summary': summary,
        'claude': {'type': 'stdio', 'command': command, 'args': list(args)},
        'codex': {'command': command, 'args': list(args)},
        'opencode': {'type': 'local', 'command': [command, *args], 'enabled': True, 'timeout': timeout},
        'nanocoder': {'transport': 'stdio', 'command': command, 'args': list(args)},
        'vscode': {'command': command, 'args': list(args)},
        'cursor': {'command': command, 'args': list(args)},
    }


def build_catalog(unity_url: str, unity_auth: bool, has_uv: bool, has_uvx: bool,
                  uv_bin: str, image_script: str) -> Catalog:
    unity = remote(unity_url, 'UNITY_MCP_TOKEN', 300000,
                   'Unity official CLI MCP (unity mcp, ~140 tools) via HTTP bridge ' + unity_url)
    unity['opencode']['oauth'] = False
    if not unity_auth:
        for frontend in ('claude', 'opencode', 'nanocoder', 'cursor'):
            del unity[frontend]['headers']
        del unity['codex']['bearer_token_env_var']
    quoted_url = "'" + unity_url.replace("'", "'\\''") + "'"
    unity['vscode'] = wrapper('npx -y mcp-remote ' + quoted_url +
                              (' --header "Authorization: Bearer ${UNITY_MCP_TOKEN}"' if unity_auth else ''))
    github = remote('https://api.githubcopilot.com/mcp/', 'GITHUB_PERSONAL_ACCESS_TOKEN', 0,
                    'GitHub platform (repos, PRs, issues, actions, security…) via hosted MCP, all toolsets')
    del github['opencode']['timeout']
    github['opencode']['oauth'] = False
    github['codex']['http_headers'] = {'X-MCP-Toolsets': 'all'}
    for frontend in ('claude', 'opencode', 'nanocoder', 'cursor'):
        github[frontend]['headers']['X-MCP-Toolsets'] = 'all'
    github['vscode']['command'][-1] += ' --header "X-MCP-Toolsets: all"'
    context = local('npx', ['-y', '@upstash/context7-mcp'], 30000, 'Up-to-date library docs for LLMs (Upstash)')
    for frontend in ('claude', 'nanocoder'):
        context[frontend]['env'] = {'CONTEXT7_API_KEY': '${CONTEXT7_API_KEY:-}'}
    context['codex']['env_vars'] = ['CONTEXT7_API_KEY']
    for frontend in ('vscode', 'cursor'):
        context[frontend]['env'] = {'CONTEXT7_API_KEY': '${env:CONTEXT7_API_KEY}'}
    servers = {'unity': unity, 'github': github, 'context7': context}
    if has_uvx:
        servers['fetch'] = local('uvx', ['mcp-server-fetch'], 30000, 'Fetch web pages as markdown (reference server)')
        servers['git'] = local('uvx', ['mcp-server-git'], 30000, 'Local git repository operations (reference server)')
    vision = local('npx', ['-y', '@z_ai/mcp-server'], 120000,
                   'Z.ai vision MCP: screenshot/diagram/image & video analysis')
    for frontend in ('claude', 'nanocoder'):
        vision[frontend]['env'] = {'ZAI_API_KEY': '${ZAI_API_KEY:-}', 'Z_AI_MODE': 'ZAI'}
    vision['codex']['env_vars'] = ['ZAI_API_KEY', 'Z_AI_MODE']
    vision['opencode']['environment'] = {'ZAI_API_KEY': '{env:ZAI_API_KEY}', 'Z_AI_MODE': 'ZAI'}
    vision['cursor']['env'] = {'ZAI_API_KEY': '${env:ZAI_API_KEY}', 'Z_AI_MODE': 'ZAI'}
    vision['vscode'] = wrapper('Z_AI_MODE="${Z_AI_MODE:-ZAI}"; exec npx -y @z_ai/mcp-server')
    servers['zai-vision'] = vision
    servers['zai-web-search'] = remote('https://api.z.ai/api/mcp/web_search_prime/mcp', 'ZAI_API_KEY', 120000,
                                      'Z.ai web-search-prime (remote streamable HTTP)')
    if has_uv:
        image = local('uv', ['run', '--no-project', '.llm/mcp/zai_image_mcp.py'], 300000,
                      'Z.ai image generation (glm-image) wrapped as local MCP')
        del image['cursor']
        for frontend in ('claude', 'nanocoder'):
            image[frontend]['env'] = {'ZAI_API_KEY': '${ZAI_API_KEY:-}'}
        image['codex'] = {'command': uv_bin, 'args': ['run', '--no-project', image_script], 'env_vars': ['ZAI_API_KEY']}
        image['opencode']['environment'] = {'ZAI_API_KEY': '{env:ZAI_API_KEY}'}
        image['vscode'] = wrapper('exec uv run --no-project .llm/mcp/zai_image_mcp.py')
        servers['zai-image'] = image
    return servers


def main() -> int:
    try:
        options: Dict[str, Any] = json.loads(sys.stdin.buffer.read().decode('utf-8'))
        result = build_catalog(**options)
        sys.stdout.buffer.write((json.dumps(result, ensure_ascii=False) + '\n').encode('utf-8'))
        return 0
    except (ValueError, TypeError):
        print('Cannot build the MCP server catalog; check catalog settings.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
