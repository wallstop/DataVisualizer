# Data Visualizer dev container

A fast, reliable VS Code devcontainer for working on this Unity UPM package,
with every agentic coding frontend pre-installed and wired to a shared MCP
server fleet.

## What's inside

| Layer | Tools |
| --- | --- |
| Runtimes | .NET SDK (base image), Node.js LTS via NVM, Python 3 + uv/uvx |
| Coding CLIs | `@nanocollective/nanocoder`, `opencode-ai`, `@openai/codex`, `@anthropic-ai/claude-code` (npm `latest`, refreshed on create and every start) |
| Gateway backends | `codex-zai`, `claude-zai` (Z.ai GLM Coding Plan) and `codex-openrouter`, `claude-openrouter` (OpenRouter) launchers |
| Repo tooling | PowerShell (lint scripts), CSharpier via `dotnet tool restore` |
| MCP servers | unity (official Unity CLI, ~140 tools), github (all toolsets), context7, fetch, git, zai-vision, zai-web-search, zai-image — auto-configured into Claude Code, Codex, OpenCode, Nanocoder, VS Code, and Cursor |
| CLI comfort | ripgrep, fd, bat, fzf, zoxide, jq, tree, htop, ncdu |

## Why `npm install` never needs sudo

The npm coding CLIs are installed **as the `vscode` user** during the image
build. The base image's NVM directory is group-writable through the stable
`nvm` group, and the global npm prefix is `chgrp nvm` + `chmod g+rws` so the
tree survives Dev Containers' UID remapping. `install-npm-tools.sh` verifies
every required path is writable before doing anything and **fails with a
remediation message instead of invoking sudo** or creating mixed-ownership
installs. If npm is temporarily unreachable at container start, the
image-baked versions keep working (bounded offline checks with
`--offline-ok`).

## Open speed and rebuilds

- The Dockerfile builds with Docker layer cache **enabled** (unlike NovaSharp's
  `--no-cache` setup): cold builds take a few minutes, rebuilds are seconds.
- The moving npm `latest` dist-tags are resolved at container create/start by
  `install-npm-tools.sh`, which only installs tools whose installed version
  differs — a no-op when everything is current.
- Named volumes persist npm + uv caches and every agent's auth home
  (`~/.codex`, `~/.claude`, `~/.claude-zai`, `~/.claude-openrouter`,
  `~/.local/share/opencode`, `~/.config/nanocoder`) across **full rebuilds**:
  log in once. The image pre-creates those mount targets with `vscode`
  ownership so fresh volumes start writable (Docker seeds a new volume from the
  image path when it exists, and from `root:root` when it does not);
  `post-start.sh` and the launchers additionally self-heal any root-owned
  volume in place via passwordless sudo.

## Credentials: `.env.local` first

Secrets live in a gitignored `.env.local` at the workspace root (template:
`.env.local.example`). `on-create.sh` bootstraps it (auto-generating
`UNITY_MCP_TOKEN`) and sources it into every interactive shell via a marker-
delimited `~/.bashrc` block. The gateway launchers additionally **self-load
`.env.local`** (workspace root beside the script, then the current directory,
or `AI_BACKENDS_ENV_FILE`), so agent Bash tools, tasks, and CI — which skip rc
files — resolve credentials too. Variables already exported in the
environment always win over the file. `remoteEnv` additionally forwards
`GITHUB_PERSONAL_ACCESS_TOKEN`, `GITHUB_TOKEN`, `ZAI_API_KEY`, and
`OPENROUTER_API_KEY` from the host shell as fallbacks. Prefer `.env.local`;
never commit it.

Regenerate the machine-local MCP configs any time with `npm run mcp:sync`.

## Native and gateway backends

`codex` and `claude` keep their native OpenAI/Anthropic backends. The gateway
launchers are per-process opt-ins installed by
`bash .devcontainer/ai-backends.sh install` (run at create and start):

- `codex-zai` uses a `devcontainer-zai` profile against Z.ai's Responses API
  with a pinned glm-5.3 model catalog.
- `claude-zai` uses Z.ai's Anthropic-compatible endpoint with a separate
  `~/.claude-zai` state directory, so native credentials and sessions stay
  isolated. The launcher keeps `ZAI_API_KEY` (and the `Z_AI_API_KEY` alias)
  exported — the same secret already present as `ANTHROPIC_AUTH_TOKEN` — so
  project `.mcp.json` `${ZAI_API_KEY}` expansion reaches MCP servers;
  `zai-web-search` sends an empty bearer and `zai-vision` refuses to start
  without it.
- `codex-openrouter` uses a `devcontainer-openrouter` profile against
  OpenRouter's OpenAI-compatible Responses API (`https://openrouter.ai/api/v1`)
  with command-backed auth, which unlocks OpenRouter's live model catalog.
- `claude-openrouter` uses OpenRouter's Anthropic skin
  (`ANTHROPIC_BASE_URL=https://openrouter.ai/api`, `ANTHROPIC_AUTH_TOKEN`,
  explicitly empty `ANTHROPIC_API_KEY`) with an isolated `~/.claude-openrouter`
  state directory and gateway model discovery enabled.
- Credentials come from `ZAI_API_KEY`/`Z_AI_API_KEY` and
  `OPENROUTER_API_KEY`/`OPEN_ROUTER_API_KEY` (`.env.local` is self-loaded) and
  are never written to a repository or generated profile.
- Z.ai overrides: `CODEX_ZAI_MODEL`, `CODEX_ZAI_REASONING_EFFORT`
  (`low|high|max`), `ZAI_API_TIMEOUT_MS`, `CLAUDE_ZAI_CONFIG_DIR`,
  `CLAUDE_ZAI_{SONNET,OPUS,HAIKU}_MODEL`.
- OpenRouter overrides: `CODEX_OPENROUTER_MODEL`,
  `CODEX_OPENROUTER_REASONING_EFFORT` (`low|medium|high|xhigh|max`),
  `OPENROUTER_API_TIMEOUT_MS`, `CLAUDE_OPENROUTER_CONFIG_DIR`,
  `CLAUDE_OPENROUTER_{FABLE,SONNET,OPUS,HAIKU}_MODEL`,
  `CLAUDE_OPENROUTER_SUBAGENT_MODEL`, `CLAUDE_OPENROUTER_GATEWAY_DISCOVERY`.
- Contract tests live in `.devcontainer/tests/test-ai-backends.sh` (stubs, no
  network) and `.devcontainer/tests/test-mcp-sync.sh` (syncs into a throwaway
  workspace; asserts fleet parity across frontends and that no secrets land on
  disk). Both run via `scripts/tests/run-all.ps1` in CI.

Inside the container Claude's experimental Linux subprocess scrubber is
disabled by default (`AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB=0`; legacy
`CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB` stays honored): Docker's seccomp
profile blocks bubblewrap's namespaces, and the outer devcontainer is already
the isolation boundary. Outside a container the stronger scrubber is retained.

## Unity workflow (host + container)

Unity's official CLI MCP server (`unity mcp`, ~140 tools) runs on the
**host**; agents in the container reach it over `host.docker.internal`:

```bash
# On the host (once per project):
cd /path/to/UnityProject && unity pipeline install

# On the host (keep running while agent containers are up):
npm run unity:mcp:host             # bridges `unity mcp` on http://127.0.0.1:9020/mcp

# Host-side GUI clients (Cursor, Windsurf, Claude Desktop):
unity mcp configure <client>
```

Container agents connect to `http://host.docker.internal:9020/mcp`
(configured automatically by `npm run mcp:sync`). Full details:
`.llm/mcp/README.md`.

## Host networking note (Linux)

On Linux hosts the container reaches host services via the `host-gateway`
add-host in `runArgs`. Keep wired Ethernet and Wi-Fi from being active
simultaneously on the same IPv4 subnet (weak-host ARP ambiguity breaks
long-lived model streams). macOS/Docker Desktop is unaffected.
