# MCP tooling (`.llm/mcp/`)

Shared MCP infrastructure for this package: the official Unity CLI MCP server (exposed over HTTP for container agents) plus a universal configurator that registers every MCP server
into all supported agentic frontends (Claude Code, OpenAI Codex, OpenCode, Nanocoder, VS Code, Cursor).

## Server fleet

| Name             | Kind                                      | Purpose                                                                                                       | Auth                                                                                                                                                             |
| ---------------- | ----------------------------------------- | ------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `unity`          | HTTP (host bridge → official `unity mcp`) | ~140 Unity editor tools: scenes, GameObjects, prefabs, materials, animation, packages, tests, builds, C# eval | `UNITY_MCP_TOKEN` (bearer)                                                                                                                                       |
| `github`         | remote HTTP                               | GitHub platform: repos, PRs, issues, actions, security (hosted MCP, **all toolsets** ≈ 95 tools)              | `GITHUB_PERSONAL_ACCESS_TOKEN` (`GITHUB_MCP_PAT` is reconciled into it — see [Credential pairs](#credential-pairs-github_personal_access_token--github_mcp_pat)) |
| `context7`       | stdio (npx)                               | Up-to-date library documentation                                                                              | `CONTEXT7_API_KEY` (optional)                                                                                                                                    |
| `fetch`          | stdio (uvx)                               | Fetch web pages as markdown                                                                                   | none                                                                                                                                                             |
| `git`            | stdio (uvx)                               | Local git operations                                                                                          | none                                                                                                                                                             |
| `zai-vision`     | stdio (npx)                               | Official Z.ai MCP: screenshot/diagram/image/video **analysis**                                                | `ZAI_API_KEY`                                                                                                                                                    |
| `zai-web-search` | remote HTTP                               | Z.ai web-search-prime                                                                                         | `ZAI_API_KEY`                                                                                                                                                    |
| `zai-image`      | stdio (local script)                      | Z.ai image **generation** (glm-image) → `.artifacts/zai-images/`                                              | `ZAI_API_KEY`                                                                                                                                                    |

All credentials come from the environment with a gitignored `.env.local` fallback (preferred). Config files contain env-var _references_ — no secrets on disk outside `.env.local`.

## Credential pairs (`GITHUB_PERSONAL_ACCESS_TOKEN` / `GITHUB_MCP_PAT`)

One GitHub PAT circulates under two names: generated configs reference `GITHUB_PERSONAL_ACCESS_TOKEN`, while some machines only define `GITHUB_MCP_PAT`. `ZAI_API_KEY` /
`Z_AI_API_KEY` is the same shape.

The incident this section exists for: `.env.local` ended up with **both** names set to **different** values — a revoked PAT under the canonical name and a valid one under the
alternate. Every generated config referenced the stale name, so the hosted GitHub MCP returned `401` while a perfectly good token sat unused one line away. Both `configure.mjs` and
`mcp-doctor.mjs` reported success because they only ever checked _presence_, never _agreement_.

Current behavior:

- Both set and **equal** → `configured`. Both set and **different** → `divergent`, reported by `mcp:sync --check` and as a failure by `mcp:doctor`. Only one set → still mirrored
  into the other as before.
- `npm run mcp:repair` reconciles a divergent pair: it probes each name against the real endpoint and copies the one that authenticates over the other, in place (never appending a
  duplicate key). It **refuses to write** when neither name authenticates rather than guessing, and never prints a credential.
- `post-start.sh` runs the repair automatically on container start, before `mcp:sync`, so a divergence is resolved on the next start instead of producing an opaque 401.

A credential rotated in `.env.local` also has to reach already-running agents. Two separate layers cache the old value, and both need handling:

1. **Your shell.** `{env:VAR}` expands once at agent startup. `env-local.sh` is sourced with `DATAVIZ_ENV_LOCAL_PRECEDENCE=file`, which lets credential names take the `.env.local`
   value over a stale exported copy. A new shell picks the change up with no rebuild.
2. **OpenCode's background service.** Every `opencode` session attaches to ONE shared server (`opencode serve --service`), discovered via `~/.local/state/opencode/service.json`. It
   inherits the environment of whichever client happened to start it and holds it for its entire life, so **new sessions stay broken too** — the session is fine, the singleton
   behind it is not. `.devcontainer/recycle-stale-opencode-service.sh` compares the running service's credentials against `.env.local` and, on a mismatch, recycles it so the next
   session respawns a healthy one. It is a no-op when the service matches, when there is no `.env.local`, or when no service is running. `post-start.sh` runs it on every container
   start.

```bash
bash .devcontainer/recycle-stale-opencode-service.sh --dry-run   # report only
bash .devcontainer/recycle-stale-opencode-service.sh             # recycle
```

Contract tests: `.devcontainer/tests/test-mcp-credentials.sh` (credential aliasing, divergence detection, repair), `.devcontainer/tests/test-env-local-precedence.sh` (loader
precedence), and `.devcontainer/tests/test-opencode-service-recycle.sh` (service recycling).

## One command to wire everything

```bash
npm run mcp:sync          # configure every frontend (idempotent)
npm run mcp:sync -- --check   # dry run
```

Outputs (all machine-local, gitignored, regenerated on every container start):

| Frontend    | File                                                                                                                                                         |
| ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Claude Code | `.mcp.json` (project scope) + `.claude/settings.local.json` (auto-approval)                                                                                  |
| Codex       | `${CODEX_HOME:-~/.codex}/config.toml` (marker-managed block)                                                                                                 |
| OpenCode    | `opencode.json` (V2 `mcp.servers` block, `{env:VAR}` refs)                                                                                                   |
| Nanocoder   | `.nanocoder/mcp.json` (via `NANOCODER_MCPSERVERS_FILE`) + directory trust seed + `agents.config.json` providers (zai via `api.z.ai/api/paas/v4`, openrouter) |
| VS Code     | `.vscode/mcp.json` (`${env:VAR}` refs)                                                                                                                       |
| Cursor      | `.cursor/mcp.json` (project scope, `${env:VAR}` refs)                                                                                                        |

Codex ≥ 0.153 additionally deep-merges a _trusted_ workspace `.codex/config.toml` over the home config, so `npm run mcp:sync` also prunes that file (removing it once nothing
user-owned remains). Do not put MCP servers there: a transport that conflicts with the home config hard-fails every codex startup (`url is not supported for stdio`). Project-scoped
MCP servers for Codex live in the marker-managed block of `$CODEX_HOME/config.toml`.

Nanocoder additionally needs a configured _provider_ before it will run (the zai + openrouter entries above, whose API keys are `${VAR}` env references that nanocoder substitutes
at load) and directory trust — both are seeded by `npm run mcp:sync`. Note Z.ai's OpenAI-compatible base URL is `https://api.z.ai/api/paas/v4`; `https://api.z.ai/api/v1` only
serves the Responses API (403 `model_access_denied` for chat completions).

Claude project-scope servers also get their approval + folder trust recorded in `~/.claude.json` (same state the interactive dialogs write), so servers are usable without a
first-run approval prompt. The isolated Claude state directories used by the `claude-zai` / `claude-openrouter` launchers (`~/.claude-zai`, `~/.claude-openrouter`) are seeded with
the same trust + approvals by `bash .devcontainer/ai-backends.sh install` (and on every launcher start), because Claude only reads that state from the config dir its
`CLAUDE_CONFIG_DIR` points at.

Windsurf and other host GUI clients are not writable from the container; run the official configurator on the host instead (see below).

`fetch`/`git` are only registered when `uvx` is on PATH (baked into the devcontainer image). Cursor's project config only carries host-executable entries (it omits `zai-image`,
whose script path is container-local). Merging preserves servers configured by other tools. The Z.ai credential is read from `ZAI_API_KEY` or `Z_AI_API_KEY`; `npm run mcp:sync`
mirrors whichever exists into the other inside `.env.local`.

## Unity MCP server (official Unity CLI)

Unity's official CLI ships an MCP server (`unity mcp`) built on the `com.unity.pipeline` package — ~140 built-in tools on a typical project (scenes, GameObjects, prefabs,
materials, animation, packages, tests, builds, plus an arbitrary C# `eval` tool). It requires the Unity CLI (Unity Hub) and Unity 6.0 LTS+.

### One-time setup (host)

```bash
# 1. Install the pipeline package into the Unity project
cd /path/to/UnityProject && unity pipeline install

# 2. Point host-side GUI clients (Cursor, Windsurf, Claude Desktop, …) at it
unity mcp configure <client>     # `unity mcp configure --list` for all
```

### HTTP bridge for devcontainer agents

The `unity` CLI is a host binary and the pipeline server inside the editor binds to editor-localhost only, so container agents cannot launch it directly. `npm run unity:mcp:host`
(on the host) bridges the official MCP server to streamable-HTTP JSON-RPC:

```bash
npm run unity:mcp:host   # host: spawns `unity mcp`, serves http://0.0.0.0:9020/mcp
```

Container agents connect to `http://host.docker.internal:9020/mcp` (written automatically by `npm run mcp:sync`). The bridge is transparent: JSON-RPC requests are forwarded to the
CLI with per-request id remapping, notifications are passed through, and a crashed CLI is respawned lazily on the next request.

Environment (prefer a gitignored `.env.local` at the workspace root):

| Variable               | Purpose                                                                                                                                                              |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `UNITY_CLI`            | Unity CLI binary path (default `unity`)                                                                                                                              |
| `UNITY_MCP_CLI_ARGS`   | Extra args appended after `mcp` (e.g. `--instance host:port`)                                                                                                        |
| `UNITY_MCP_PROJECT`    | `--project-path` value; must be the project **root** (`UNITY_PROJECT_PATH` alias honored; non-root values self-heal to the enclosing project)                        |
| `UNITY_MCP_HTTP_PORT`  | Bridge port (default 9020). When busy, the bridge binds the next free port, persists the choice here, and re-syncs client configs (`UNITY_MCP_AUTO_SYNC=0` disables) |
| `UNITY_MCP_TOKEN`      | Optional bearer token required by the bridge when set                                                                                                                |
| `UNITY_MCP_TIMEOUT_MS` | Per-request timeout (default 180000)                                                                                                                                 |

Security: the bridge listens on all interfaces by default (Linux Docker cannot reach a host-loopback bind via `host.docker.internal`); pass `--host 127.0.0.1` to restrict it.
`UNITY_MCP_TOKEN` (`.env.local`) gates HTTP access when set. Only set `UNITY_MCP_TOKEN` if you accept managing bearer headers in every frontend config.

### Troubleshooting

- **`unity run unity:mcp:host` fails with "Not a Unity project".** That invoked the Unity CLI, not the npm script: `unity run <project>` expects a project path, so it treats
  `unity:mcp:host` as one. The bridge is an npm script — start it with `npm run unity:mcp:host`.
- **Port already in use / dynamic ports.** Re-running the bridge is a no-op while a healthy instance holds the port; if something else owns it, the bridge binds the next free port,
  persists `UNITY_MCP_HTTP_PORT` to `.env.local`, and re-syncs every frontend config automatically. No fixed port is required anywhere: `configure.mjs`, the devcontainer health
  check, and the doctors all read the persisted value.
- **`npm run mcp:sync` on the host.** It runs `configure.mjs` directly (pure node, cross-platform); the bash wrapper `sync-mcp.sh` remains for container hooks. `hasCommand` uses
  `where` on Windows so `uvx`-gated servers register on the host too.
- **Shared configs are environment-agnostic.** Sync produces identical files from the host and the container (verified by hash): the unity bridge URL always uses
  `host.docker.internal` (resolves on the host under Docker Desktop too), and zai-image uses PATH-resolved `node` with a workspace-relative script. Never reintroduce `127.0.0.1` or
  absolute binaries into shared entries — they silently break the other environment.
- **MCP auth fails in the container after editing `.env.local`.** `{env:VAR}` references expand at AGENT STARTUP: restart the agent from a fresh shell (interactive shells source
  `.env.local` via the bashrc loader, which now runs with `DATAVIZ_ENV_LOCAL_PRECEDENCE=file` so a rotated credential wins over a stale value already exported into the session — no
  container rebuild needed). VS Code is special: its extension host inherits the server's frozen environment, and `remoteEnv` forwards (`${localEnv:VAR}`) are empty unless the host
  shell exported the variable — so VS Code entries for credential servers are generated as wrappers that source `.env.local` at spawn time.
- **GitHub MCP 401 in the devcontainer.** See [Credential pairs](#credential-pairs-github_personal_access_token--github_mcp_pat).
- **Health check:** `npm run mcp:doctor` verifies credentials, config flavor, endpoint auth (github, zai-web-search), stdio spawn paths (zai-vision, zai-image), and unity bridge
  reachability — from the host or the container. `npm run unity:mcp:doctor` goes deeper on the unity bridge (project pin, pipeline package, tools roundtrip, wrong-editor canary).
  Exit code 1 means action required.
- **Tools drive the wrong project.** An unpinned `unity mcp` attaches to whichever Editor happens to be running. `UNITY_MCP_PROJECT` must be the project **root** (contains
  `ProjectSettings/ProjectVersion.txt`); pointing it at `<project>/Packages` or the package repo silently unpins. The bridge now self-heals to the enclosing project with a warning,
  and the `/healthz` `cli` line shows the exact spawned command.
- **Editor must be running.** Editor-dependent tools fail until an Editor is open for the pinned project: `unity open <project>`.

### Removing the legacy custom bridge

The previous hand-rolled `UnityMcpBridge.cs` (10 tools) is deprecated. If it was installed into the Unity project, delete `<project>/Assets/Editor/UnityMcpBridge.cs` (and its
`.meta`) — it is no longer installed or served by anything in this repo.
