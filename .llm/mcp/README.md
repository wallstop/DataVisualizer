# MCP tooling (`.llm/mcp/`)

Shared MCP infrastructure for this package: the official Unity CLI MCP server
(exposed over HTTP for container agents) plus a universal configurator that
registers every MCP server into all supported agentic frontends (Claude Code,
OpenAI Codex, OpenCode, Nanocoder, VS Code, Cursor).

## Server fleet

| Name | Kind | Purpose | Auth |
| --- | --- | --- | --- |
| `unity` | HTTP (host bridge → official `unity mcp`) | ~140 Unity editor tools: scenes, GameObjects, prefabs, materials, animation, packages, tests, builds, C# eval | `UNITY_MCP_TOKEN` (bearer) |
| `github` | remote HTTP | GitHub platform: repos, PRs, issues, actions, security (hosted MCP, **all toolsets** ≈ 93 tools) | `GITHUB_PERSONAL_ACCESS_TOKEN` |
| `context7` | stdio (npx) | Up-to-date library documentation | `CONTEXT7_API_KEY` (optional) |
| `fetch` | stdio (uvx) | Fetch web pages as markdown | none |
| `git` | stdio (uvx) | Local git operations | none |
| `zai-vision` | stdio (npx) | Official Z.ai MCP: screenshot/diagram/image/video **analysis** | `ZAI_API_KEY` |
| `zai-web-search` | remote HTTP | Z.ai web-search-prime | `ZAI_API_KEY` |
| `zai-image` | stdio (local script) | Z.ai image **generation** (glm-image) → `.artifacts/zai-images/` | `ZAI_API_KEY` |

All credentials come from the environment with a gitignored `.env.local`
fallback (preferred). Config files contain env-var *references* — no secrets
on disk outside `.env.local`.

## One command to wire everything

```bash
npm run mcp:sync          # configure every frontend (idempotent)
npm run mcp:sync -- --check   # dry run
```

Outputs (all machine-local, gitignored, regenerated on every container start):

| Frontend | File |
| --- | --- |
| Claude Code | `.mcp.json` (project scope) + `.claude/settings.local.json` (auto-approval) |
| Codex | `${CODEX_HOME:-~/.codex}/config.toml` (marker-managed block) |
| OpenCode | `opencode.json` (`mcp` block, `{env:VAR}` refs) |
| Nanocoder | `.nanocoder/mcp.json` (via `NANOCODER_MCPSERVERS_FILE`) + directory trust seed + `agents.config.json` providers (zai via `api.z.ai/api/paas/v4`, openrouter) |
| VS Code | `.vscode/mcp.json` (`${env:VAR}` refs) |
| Cursor | `.cursor/mcp.json` (project scope, `${env:VAR}` refs) |

Codex ≥ 0.153 additionally deep-merges a *trusted* workspace
`.codex/config.toml` over the home config, so `npm run mcp:sync` also prunes
that file (removing it once nothing user-owned remains). Do not put MCP
servers there: a transport that conflicts with the home config hard-fails every
codex startup (`url is not supported for stdio`). Project-scoped MCP servers
for Codex live in the marker-managed block of `$CODEX_HOME/config.toml`.

Nanocoder additionally needs a configured *provider* before it will run (the
zai + openrouter entries above, whose API keys are `${VAR}` env references
that nanocoder substitutes at load) and directory trust — both are seeded by
`npm run mcp:sync`. Note Z.ai's OpenAI-compatible base URL is
`https://api.z.ai/api/paas/v4`; `https://api.z.ai/api/v1` only serves the
Responses API (403 `model_access_denied` for chat completions).

Claude project-scope servers also get their approval + folder trust recorded in
`~/.claude.json` (same state the interactive dialogs write), so servers are
usable without a first-run approval prompt. The isolated Claude state
directories used by the `claude-zai` / `claude-openrouter` launchers
(`~/.claude-zai`, `~/.claude-openrouter`) are seeded with the same trust +
approvals by `bash .devcontainer/ai-backends.sh install` (and on every
launcher start), because Claude only reads that state from the config dir its
`CLAUDE_CONFIG_DIR` points at.

Windsurf and other host GUI clients are not writable from the container; run
the official configurator on the host instead (see below).

`fetch`/`git` are only registered when `uvx` is on PATH (baked into the
devcontainer image). Cursor's project config only carries host-executable
entries (it omits `zai-image`, whose script path is container-local). Merging
preserves servers configured by other tools. The Z.ai credential is read from
`ZAI_API_KEY` or `Z_AI_API_KEY`; `npm run mcp:sync` mirrors whichever exists
into the other inside `.env.local`.

## Unity MCP server (official Unity CLI)

Unity's official CLI ships an MCP server (`unity mcp`) built on the
`com.unity.pipeline` package — ~140 built-in tools on a typical project
(scenes, GameObjects, prefabs, materials, animation, packages, tests, builds,
plus an arbitrary C# `eval` tool). It requires the Unity CLI (Unity Hub) and
Unity 6.0 LTS+.

### One-time setup (host)

```bash
# 1. Install the pipeline package into the Unity project
cd /path/to/UnityProject && unity pipeline install

# 2. Point host-side GUI clients (Cursor, Windsurf, Claude Desktop, …) at it
unity mcp configure <client>     # `unity mcp configure --list` for all
```

### HTTP bridge for devcontainer agents

The `unity` CLI is a host binary and the pipeline server inside the editor
binds to editor-localhost only, so container agents cannot launch it directly.
`npm run unity:mcp:host` (on the host) bridges the official MCP server to
streamable-HTTP JSON-RPC:

```bash
npm run unity:mcp:host   # host: spawns `unity mcp`, serves http://127.0.0.1:9020/mcp
```

Container agents connect to `http://host.docker.internal:9020/mcp` (written
automatically by `npm run mcp:sync`). The bridge is transparent: JSON-RPC
requests are forwarded to the CLI with per-request id remapping, notifications
are passed through, and a crashed CLI is respawned lazily on the next request.

Environment (prefer a gitignored `.env.local` at the workspace root):

| Variable | Purpose |
| --- | --- |
| `UNITY_CLI` | Unity CLI binary path (default `unity`) |
| `UNITY_MCP_CLI_ARGS` | Extra args appended after `mcp` (e.g. `--instance host:port`) |
| `UNITY_MCP_PROJECT` | `--project-path` value (falls back to `UNITY_PROJECT_PATH`) |
| `UNITY_MCP_HTTP_PORT` | Bridge port (default 9020) |
| `UNITY_MCP_TOKEN` | Optional bearer token required by the bridge when set |
| `UNITY_MCP_TIMEOUT_MS` | Per-request timeout (default 180000) |

Security: the bridge binds to loopback by default; `UNITY_MCP_TOKEN`
(`.env.local`) gates HTTP access when set. Only set `UNITY_MCP_TOKEN` if you
accept managing bearer headers in every frontend config.

### Removing the legacy custom bridge

The previous hand-rolled `UnityMcpBridge.cs` (10 tools) is deprecated. If it
was installed into the Unity project, delete
`<project>/Assets/Editor/UnityMcpBridge.cs` (and its `.meta`) — it is no
longer installed or served by anything in this repo.
