#!/usr/bin/env node
// Universal MCP configurator: registers the project's MCP servers into every
// supported agentic frontend (Claude Code, OpenAI Codex, OpenCode, Nanocoder,
// VS Code, Cursor) with per-CLI native syntax. Machine-local configs are
// regenerated (idempotently, transactionally, chmod 600) so they survive
// devcontainer rebuilds and fresh clones without committing secrets.
//
// Usage:
//   node .llm/mcp/configure.mjs [--check]
//     --check  Print the plan without writing files.
//
// Credentials come from the environment with .env.local fallback (the
// config files reference env vars; nothing secret is written to disk).
//
// Unity uses the official Unity CLI MCP server (`unity mcp`, ~140 tools on a
// typical project). Container frontends reach it through the host HTTP bridge
// (npm run unity:mcp:host → http://host.docker.internal:<port>/mcp, port from
// UNITY_MCP_HTTP_PORT). Every managed frontend shares the pinned bridge;
// unmanaged host GUI clients can use `unity mcp configure <client>`.

import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const WORKSPACE_ROOT = process.env.DATAVIZ_MCP_WORKSPACE
    ? path.resolve(process.env.DATAVIZ_MCP_WORKSPACE)
    : path.resolve(SCRIPT_DIR, "..", "..");
const CHECK_ONLY = process.argv.includes("--check");

// ---------------------------------------------------------------------------
// Environment
// ---------------------------------------------------------------------------

function readEnvLocal(filePath) {
    const vars = {};
    let text;
    try {
        text = fs.readFileSync(filePath, "utf8");
    } catch {
        return vars;
    }
    // Tolerate UTF-8 BOM, CRLF endings, and `export ` prefixes.
    text = text.replace(/^\uFEFF/, "").replace(/\r\n/g, "\n").replace(/\r/g, "\n");
    for (const rawLine of text.split("\n")) {
        const line = rawLine.trim();
        if (!line || line.startsWith("#")) continue;
        const eq = line.indexOf("=");
        if (eq <= 0) continue;
        let key = line.slice(0, eq).trim();
        if (key.startsWith("export ")) key = key.slice(7).trim();
        let value = line.slice(eq + 1).trim();
        if (
            (value.startsWith('"') && value.endsWith('"')) ||
            (value.startsWith("'") && value.endsWith("'"))
        ) {
            value = value.slice(1, -1);
        }
        if (key) vars[key] = value;
    }
    return vars;
}

const envLocal = readEnvLocal(path.join(WORKSPACE_ROOT, ".env.local"));
function env(name) {
    return process.env[name] || envLocal[name] || "";
}

function isContainer() {
    return (
        fs.existsSync("/.dockerenv") ||
        fs.existsSync("/run/.containerenv") ||
        process.env.DATAVIZ_IN_CONTAINER === "1"
    );
}

function hasCommand(command) {
    try {
        if (process.platform === "win32") {
            // Git Bash is not guaranteed on Windows hosts; `where` resolves
            // PATH + PATHEXT the same way cmd does.
            execFileSync("where", [command], { stdio: "ignore" });
        } else {
            execFileSync(
                "/bin/bash",
                ["-c", `command -v ${(command)} >/dev/null 2>&1`],
                { stdio: "ignore" },
            );
        }
        return true;
    } catch {
        return false;
    }
}

function resolveCommand(command) {
    // The absolute twin of hasCommand: same PATH + PATHEXT rules, falling
    // back to the bare name when the lookup fails.
    try {
        if (process.platform === "win32") {
            const output = execFileSync("where", [command], { encoding: "utf8" });
            const first = String(output)
                .split(/\r?\n/)
                .find((line) => line.trim());
            return first ? first.trim() : command;
        }
        return execFileSync("/bin/bash", ["-c", `command -v ${command}`], { encoding: "utf8" }).trim() || command;
    } catch {
        return command;
    }
}

function ensureUnityMcpToken() {
    // Authentication is a host choice. Sync must not introduce a token after
    // the bridge has already started without one, or require an absent env
    // variable in clients that reject missing bearer_token_env_var values.
    return env("UNITY_MCP_TOKEN");
}

function synchronizeAliases() {
    let launcher = HAS_UV ? [UV_BIN, "run", "--no-project"] : null;
    if (!launcher) {
        for (const command of ["python3", "python"]) {
            try {
                const probe = execFileSync(command, ["-c", "import sys; assert sys.version_info >= (3, 9); print('mcp-alias-python')"], {
                    encoding: "utf8", stdio: ["ignore", "pipe", "ignore"],
                });
                if (probe.trim() === "mcp-alias-python") { launcher = [command]; break; }
            } catch {
                // Validate an interpreter before any credential-file writes.
            }
        }
    }
    if (!launcher) throw new Error("Python 3.9+ or uv is required for MCP credential aliases.");
    const [command, ...prefix] = launcher;
    const script = path.resolve(SCRIPT_DIR, "..", "..", "scripts", "mcp_aliases.py");
    const args = [...prefix, script, "--workspace", WORKSPACE_ROOT];
    if (CHECK_ONLY) args.push("--check");
    return JSON.parse(execFileSync(command, args, { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }));
}

// ---------------------------------------------------------------------------
// Managed server catalog
// ---------------------------------------------------------------------------

const ZAI_IMAGE_SCRIPT = path.join(WORKSPACE_ROOT, ".llm", "mcp", "zai_image_mcp.py");
const NODE_BIN = process.execPath;
const HAS_UVX = hasCommand("uvx");
const HAS_UV = hasCommand("uv");
const UV_BIN = HAS_UV ? resolveCommand("uv") : "uv";
// Container agents reach the host HTTP bridge via host.docker.internal; when
// the configurator runs on the host itself, the bridge is plain loopback. The
// port is configurable (UNITY_MCP_HTTP_PORT, process env or .env.local) and is
// dynamically chosen + persisted by unity-mcp-host.mjs when 9020 is busy.
const UNITY_MCP_PORT = Number(env("UNITY_MCP_HTTP_PORT")) || 9020;
// The bridge host must be reachable from BOTH host-side and container-side
// clients reading the same shared machine-local configs. `host.docker.internal`
// resolves on the host itself under Docker Desktop (verified) and inside
// containers, while 127.0.0.1 only works host-side and silently breaks
// container agents — so it is never the default. Override the whole URL with
// UNITY_MCP_HTTP_URL for exotic topologies.
const UNITY_MCP_URL =
    env("UNITY_MCP_HTTP_URL") ||
    `http://host.docker.internal:${UNITY_MCP_PORT}/mcp`;

function buildCatalog() {
    let launcher = HAS_UV ? [UV_BIN, "run", "--no-project"] : null;
    if (!launcher) {
        for (const command of ["python3", "python"]) {
            try {
                const result = execFileSync(command, ["-c", "import sys; assert sys.version_info >= (3, 9); print('mcp-catalog-python')"], {
                    encoding: "utf8", stdio: ["ignore", "pipe", "ignore"],
                });
                if (result.trim() === "mcp-catalog-python") { launcher = [command]; break; }
            } catch { /* Try the next interpreter before building definitions. */ }
        }
    }
    if (!launcher) throw new Error("Python 3.9+ or uv is required for the MCP catalog.");
    const [command, ...prefix] = launcher;
    const script = path.resolve(SCRIPT_DIR, "..", "..", "scripts", "mcp_catalog.py");
    const options = { unity_url: UNITY_MCP_URL, unity_auth: Boolean(env("UNITY_MCP_TOKEN")),
        has_uv: HAS_UV, has_uvx: HAS_UVX, uv_bin: UV_BIN, image_script: ZAI_IMAGE_SCRIPT };
    const output = execFileSync(command, [...prefix, script], {
        input: Buffer.from(JSON.stringify(options), "utf8"), encoding: "utf8", stdio: ["pipe", "pipe", "pipe"],
    });
    return new Map(Object.entries(JSON.parse(output)));
}

// OpenCode 2.x reads V1 config, but the native V2 shape is canonical:
// servers live under mcp.servers, `enabled: true` inverts to `disabled: false`,
// and the single request timeout splits into mcp.timeout.catalog/execution
// (mapping the old value onto both, per the V1→V2 migration guide).
function opencodeServerV2(opencodeDefinition) {
    const { enabled, timeout, ...rest } = opencodeDefinition;
    const next = { ...rest, disabled: enabled === false ? true : false };
    if (typeof timeout === "number") {
        next.timeout = { catalog: timeout, execution: timeout };
    }
    return next;
}

// ---------------------------------------------------------------------------
// Writers
// ---------------------------------------------------------------------------

function writeAtomic(filePath, contents, mode = 0o600) {
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    const temp = `${filePath}.tmp-${process.pid}`;
    fs.writeFileSync(temp, contents, { mode });
    fs.renameSync(temp, filePath);
    try {
        fs.chmodSync(filePath, mode);
    } catch {
        // chmod best-effort (Windows).
    }
}

function writeJsonConfigs(managed) {
    let launcher = HAS_UV ? [UV_BIN, "run", "--no-project"] : null;
    if (!launcher) {
        for (const command of ["python3", "python"]) {
            try {
                const probe = execFileSync(command, ["-c", "import sys; assert sys.version_info >= (3, 9); print('mcp-json-python')"], {
                    encoding: "utf8", stdio: ["ignore", "pipe", "ignore"],
                });
                if (probe.trim() === "mcp-json-python") {
                    launcher = [command];
                    break;
                }
            } catch {
                // Try the next interpreter before any configuration writes.
            }
        }
    }
    if (!launcher) throw new Error("Python 3.9+ or uv is required for MCP JSON configuration.");
    const [command, ...prefix] = launcher;
    const script = path.resolve(SCRIPT_DIR, "..", "..", "scripts", "mcp_json_config.py");
    const args = [...prefix, script, "--workspace", WORKSPACE_ROOT,
        "--home", process.env.HOME || os.homedir(),
        "--nano-home", process.env.NANOCODER_CONFIG_DIR || path.join(os.homedir(), ".config", "nanocoder")];
    if (CHECK_ONLY) args.push("--check");
    return JSON.parse(execFileSync(command, args, { input: JSON.stringify(managed), encoding: "utf8", stdio: ["pipe", "pipe", "pipe"] }));
}

// Codex persistence is typed Python; keep the Node server catalog until its port.
let codexWriterLauncher;
function codexConfig(operation, entries = {}) {
    if (!codexWriterLauncher) {
        if (HAS_UV) {
            codexWriterLauncher = [UV_BIN, "run", "--no-project"];
        } else {
            // uv-gated servers remain optional. Probe execution because Windows
            // may expose a python3 App Execution Alias without an interpreter.
            for (const command of ["python3", "python"]) {
                try {
                    execFileSync(command, ["-c", "import sys; assert sys.version_info >= (3, 9)"], { stdio: "ignore" });
                    codexWriterLauncher = [command];
                    break;
                } catch {
                    // Try the next interpreter before reporting a missing tool.
                }
            }
        }
    }
    if (!codexWriterLauncher) throw new Error("Python 3.9+ or uv is required for Codex MCP configuration.");
    const [command, ...prefix] = codexWriterLauncher;
    const script = path.resolve(SCRIPT_DIR, "..", "..", "scripts", "mcp_codex_config.py");
    const args = [...prefix, script, operation, "--workspace", WORKSPACE_ROOT];
    if (CHECK_ONLY) args.push("--check");
    return JSON.parse(execFileSync(command, args, {
        input: JSON.stringify(entries),
        encoding: "utf8",
        stdio: ["pipe", "pipe", "pipe"],
    }));
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

function main() {
    const unityToken = ensureUnityMcpToken();
    const aliases = synchronizeAliases();
    const zaiKeyStatus = aliases.zai;
    const githubPatStatus = aliases.github;
    const catalog = buildCatalog();

    const managedClaude = {};
    const managedCodex = {};
    const managedOpencode = {};
    const managedNanocoder = {};
    const managedVscode = {};
    const managedCursor = {};
    for (const [name, definition] of catalog) {
        managedClaude[name] = definition.claude;
        managedCodex[name] = definition.codex;
        managedOpencode[name] = opencodeServerV2(definition.opencode);
        managedNanocoder[name] = definition.nanocoder;
        // VS Code requires a command string plus args. OpenCode's command
        // array shape is not a valid VS Code stdio server definition.
        const vscode = definition.vscode;
        managedVscode[name] = Array.isArray(vscode.command)
            ? { ...vscode, command: vscode.command[0], args: vscode.command.slice(1) }
            : vscode;
        if (definition.cursor) managedCursor[name] = definition.cursor;
    }

    let jsonOutcomes;
    let jsonFailure;
    const writeJson = (frontend) => {
        if (jsonFailure) throw jsonFailure;
        if (!jsonOutcomes) {
            try {
                jsonOutcomes = writeJsonConfigs({
                    claude: managedClaude, opencode: managedOpencode, nanocoder: managedNanocoder,
                    vscode: managedVscode, cursor: managedCursor,
                });
            } catch (error) {
                jsonFailure = error;
                throw error;
            }
        }
        const outcome = jsonOutcomes[frontend];
        if (outcome.error) throw new Error(outcome.error);
        return outcome.changed;
    };

    const results = [];
    let codexWorkspaceOutcome = { changed: false, note: "" };
    const plan = [
        {
            label: "Claude Code (.mcp.json + project approval)",
            write: () => writeJson("claude"),
        },
        {
            label: `Codex (${process.env.CODEX_HOME || "~/.codex"}/config.toml managed block)`,
            write: () => codexConfig("write", managedCodex).changed,
        },
        {
            label: "Codex workspace (.codex/config.toml pruned — Codex 0.153+ merges it)",
            write: () => {
                codexWorkspaceOutcome = codexConfig("prune");
                return codexWorkspaceOutcome.changed;
            },
            after: () => {
                if (codexWorkspaceOutcome.note) {
                    results.push(
                        `${codexWorkspaceOutcome.changed ? "✅" : "·"}   ↳ ${codexWorkspaceOutcome.note}`,
                    );
                }
            },
        },
        {
            label: "OpenCode (opencode.json mcp.servers block)",
            write: () => writeJson("opencode"),
        },
        {
            label: "Nanocoder (.nanocoder/mcp.json + trust seed + providers)",
            write: () => writeJson("nanocoder"),
        },
        {
            label: "VS Code (.vscode/mcp.json)",
            write: () => writeJson("vscode"),
        },
        {
            // Project scope: Cursor on the host reads this shared file. It
            // launches commands on the host, so stdio servers must reference
            // host-visible binaries (`unity`, npx, uvx) — container-absolute
            // paths (zai-image) are intentionally omitted here.
            label: "Cursor (.cursor/mcp.json, project scope)",
            write: () => writeJson("cursor"),
        },
    ];

    let failed = false;
    for (const entry of plan) {
        try {
            const changed = entry.write();
            results.push(`${changed ? "✅" : "·"} ${entry.label}${changed && !CHECK_ONLY ? " updated" : ""}`);
            if (entry.after) entry.after();
        } catch (error) {
            failed = true;
            results.push(`❌ ${entry.label}: ${error.message}`);
        }
    }
    if (failed) process.exitCode = 1;

    const container = isContainer();
    console.log("");
    console.log("╔════════════════════════════════════════════════════════════════╗");
    console.log("║                MCP Server Configuration                        ║");
    console.log("╚════════════════════════════════════════════════════════════════╝");
    for (const line of results) console.log(`  ${line}`);
    console.log("");
    console.log(`  Servers: ${[...catalog.keys()].join(", ")}`);
    if (!HAS_UVX) {
        console.log("  (fetch/git skipped: uvx not found on PATH)");
    }
    if (!HAS_UV) {
        console.log("  (zai-image skipped: uv not found on PATH)");
    }
    console.log(
        `  UNITY_MCP_TOKEN: ${unityToken ? "configured" : "OPEN (no token)"}`,
    );
    const describeZai = (status) => {
        if (status === "missing") return "MISSING (zai-* servers will fail auth)";
        if (status === "divergent") {
            return (
                "DIVERGENT (ZAI_API_KEY != Z_AI_API_KEY — consumers read different " +
                "values; run `npm run mcp:doctor --repair`)"
            );
        }
        return status;
    };
    const describeGithub = (status) => {
        if (status === "missing") return "MISSING (github MCP will fail auth with 401)";
        if (status === "divergent") {
            return (
                "DIVERGENT (GITHUB_PERSONAL_ACCESS_TOKEN != GITHUB_MCP_PAT — configs use " +
                "GITHUB_PERSONAL_ACCESS_TOKEN, so a stale value there makes github MCP " +
                "return 401; run `npm run mcp:doctor --repair`)"
            );
        }
        return status;
    };
    console.log(`  ZAI_API_KEY: ${describeZai(zaiKeyStatus)}`);
    console.log(`  GITHUB_PERSONAL_ACCESS_TOKEN: ${describeGithub(githubPatStatus)}`);
    console.log(
        `  Environment: ${container ? "devcontainer" : "host"} (shared configs use environment-agnostic URLs; bridge: ${UNITY_MCP_URL})`,
    );
    console.log(
        `  Unity MCP: official Unity CLI (~140 tools) via ${UNITY_MCP_URL} — start the bridge with \`npm run unity:mcp:host\` on the host.`,
    );
    console.log(
        "  Managed Cursor shares the bridge; unmanaged host clients can run `unity mcp configure <client>` for native stdio.",
    );
    console.log("");
    console.log("  Restart your agent CLIs (or run /mcp) to pick up changes.");
    console.log("");
}

main();
