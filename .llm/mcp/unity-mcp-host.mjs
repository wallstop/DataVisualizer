#!/usr/bin/env node
// Unity MCP HTTP host — bridges the official Unity CLI MCP server
// (`unity mcp`, stdio — built on com.unity.pipeline, ~140 tools) to a
// streamable-HTTP JSON-RPC endpoint so MCP clients that cannot run host
// binaries (devcontainer agents) can still drive the Unity editor.
//
// Usage:
//   unity-mcp-host.mjs [--port N] [--host H]
//
// On the host:
//   npm run unity:mcp:host        # proxies `unity mcp` on http://0.0.0.0:9020/mcp
// Container clients then connect to http://host.docker.internal:9020/mcp.
// Linux Docker cannot reach a host-loopback bind through host.docker.internal,
// so the bridge listens on all interfaces; UNITY_MCP_TOKEN gates access.
//
// Startup is idempotent and conflict-free: if a healthy unity-mcp bridge
// already owns the preferred port, re-running exits 0 without a second
// instance; if something else owns it, the next free port is bound, the choice
// is persisted to .env.local (UNITY_MCP_HTTP_PORT) and the MCP client configs
// for every agentic frontend are re-synced automatically.
//
// The bridge is transparent: JSON-RPC requests are forwarded verbatim to the
// CLI process (request ids are remapped so concurrent HTTP requests cannot
// collide), and responses/notifications stream back untouched.
//
// Environment (precedence: process env > .env.local):
//   UNITY_CLI            Path to the Unity CLI binary (default: `unity`)
//   UNITY_MCP_CLI_ARGS   Extra args appended to `unity mcp` (space-split)
//   UNITY_MCP_PROJECT    Project path passed as --project-path (UNITY_PROJECT_PATH
//                        is honored as an alias in both process env and .env.local)
//   UNITY_MCP_HTTP_PORT  Preferred port (default 9020); busy port → dynamic
//                        pick + persisted back to .env.local
//   UNITY_MCP_AUTO_SYNC  Set to 0 to skip the automatic `mcp:sync` after a
//                        dynamic port change
//   UNITY_MCP_TOKEN      Optional bearer token (also read from .env.local)
//   UNITY_MCP_TIMEOUT_MS Per-request timeout (default 180000)
//
// The project pin must be a Unity project root (contains
// ProjectSettings/ProjectVersion.txt). A configured value that is not one
// (e.g. `<project>/Packages`) self-heals to the enclosing Unity project: an
// unpinned `unity mcp` attaches to whichever Editor happens to be running,
// which silently drives the wrong project.

import net from "node:net";
import http from "node:http";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawn, spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const WORKSPACE_ROOT = path.resolve(SCRIPT_DIR, "..", "..");
const REQUEST_TIMEOUT_MS = Number(process.env.UNITY_MCP_TIMEOUT_MS || 180000);

function readEnvLocal(filePath) {
    const vars = {};
    let text;
    try {
        text = fs.readFileSync(filePath, "utf8");
    } catch {
        return vars;
    }
    for (const rawLine of text.split(/\r?\n/)) {
        const line = rawLine.trim();
        if (!line || line.startsWith("#")) continue;
        const eq = line.indexOf("=");
        if (eq <= 0) continue;
        const key = line.slice(0, eq).trim();
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

const envLocal = {
    ...readEnvLocal(path.join(WORKSPACE_ROOT, ".env.local")),
    ...readEnvLocal(path.join(os.homedir(), ".unity-mcp", ".env.local")),
};

// Preferred HTTP port: process env wins, then .env.local, then the historical
// default of 9020. When the preferred port is busy the bridge binds the next
// free port instead (see main), persists the chosen value back to .env.local,
// and re-syncs the MCP client configs so every frontend follows the new port.
const CONFIGURED_PORT = Number(
    process.env.UNITY_MCP_HTTP_PORT || envLocal.UNITY_MCP_HTTP_PORT || 9020,
);

function resolveToken() {
    return process.env.UNITY_MCP_TOKEN || envLocal.UNITY_MCP_TOKEN || "";
}

function timingSafeEqual(a, b) {
    const bufferA = Buffer.from(String(a));
    const bufferB = Buffer.from(String(b));
    if (bufferA.length !== bufferB.length) return false;
    return crypto.timingSafeEqual(bufferA, bufferB);
}

// ---------------------------------------------------------------------------
// CLI process management
// ---------------------------------------------------------------------------

const cliCommand = process.env.UNITY_CLI || "unity";
const cliArgs = ["mcp"];

function isUnityProjectRoot(dir) {
    try {
        return fs
            .statSync(path.join(dir, "ProjectSettings", "ProjectVersion.txt"))
            .isFile();
    } catch {
        return false;
    }
}

// Walk up from `start` (inclusive) to find the enclosing Unity project root.
function findEnclosingUnityProject(start) {
    let dir = path.resolve(start);
    for (let i = 0; i < 32; i++) {
        if (isUnityProjectRoot(dir)) return dir;
        const parent = path.dirname(dir);
        if (parent === dir) return null;
        dir = parent;
    }
    return null;
}

// Project pin precedence: process env (UNITY_MCP_PROJECT, then the Unity CLI's
// own UNITY_PROJECT_PATH alias) > .env.local (same two keys). A configured
// value that is not a project root self-heals to the enclosing Unity project
// so a stale or half-path config (e.g. `<project>/Packages`) cannot silently
// unpick the pin — an unpinned `unity mcp` attaches to whichever Editor
// happens to be running, which drives the wrong project.
const configuredProject =
    process.env.UNITY_MCP_PROJECT ||
    process.env.UNITY_PROJECT_PATH ||
    envLocal.UNITY_MCP_PROJECT ||
    envLocal.UNITY_PROJECT_PATH ||
    "";

let projectPath = "";
if (configuredProject) {
    projectPath = path.resolve(configuredProject);
    if (!isUnityProjectRoot(projectPath)) {
        const enclosing = findEnclosingUnityProject(projectPath);
        if (enclosing) {
            process.stderr.write(
                `[unity-mcp] WARNING: UNITY_MCP_PROJECT '${configuredProject}' is not a Unity ` +
                    `project root (no ProjectSettings/ProjectVersion.txt); using enclosing ` +
                    `project '${enclosing}' instead.\n`,
            );
            projectPath = enclosing;
        } else {
            process.stderr.write(
                `[unity-mcp] WARNING: UNITY_MCP_PROJECT '${configuredProject}' is not a Unity ` +
                    `project root and no enclosing Unity project was found; starting WITHOUT ` +
                    `a project pin — the MCP server may attach to the wrong running Editor.\n`,
            );
            projectPath = "";
        }
    }
}
if (!projectPath) {
    const enclosing = findEnclosingUnityProject(WORKSPACE_ROOT);
    if (enclosing) {
        process.stderr.write(
            `[unity-mcp] No project pin configured; using the enclosing Unity project of ` +
                `this workspace: '${enclosing}'.\n`,
        );
        projectPath = enclosing;
    }
}
if (projectPath) cliArgs.push("--project-path", projectPath);
if (process.env.UNITY_MCP_CLI_ARGS) {
    cliArgs.push(...process.env.UNITY_MCP_CLI_ARGS.split(" ").filter(Boolean));
}

let child = null;
let stdoutBuffer = "";
let nextRequestId = 0;
const pending = new Map(); // internal id -> {resolve, timer, originalId}
let lastSpawnAt = 0;

function jsonRpcError(id, code, message) {
    return { jsonrpc: "2.0", id: id === undefined ? null : id, error: { code, message } };
}

function failPending(message) {
    for (const [id, entry] of pending) {
        clearTimeout(entry.timer);
        entry.resolve(jsonRpcError(entry.originalId, -32000, message));
        pending.delete(id);
    }
}

function spawnChild() {
    // Guard against tight crash loops (e.g. CLI not installed): if the last
    // spawn was less than a second ago, fail fast instead of respawning.
    if (Date.now() - lastSpawnAt < 1000) {
        throw new Error(
            `Unity CLI is not startable (tried: ${cliCommand} ${cliArgs.join(" ")}). ` +
                "Install the Unity CLI on the host, or set UNITY_CLI to its path.",
        );
    }
    lastSpawnAt = Date.now();
    failPending("Unity CLI restarted; retry the request.");
    child = spawn(cliCommand, cliArgs, {
        stdio: ["pipe", "pipe", "pipe"],
        env: process.env,
    });
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", (chunk) => {
        stdoutBuffer += chunk;
        let newline;
        while ((newline = stdoutBuffer.indexOf("\n")) !== -1) {
            const line = stdoutBuffer.slice(0, newline).trim();
            stdoutBuffer = stdoutBuffer.slice(newline + 1);
            if (!line) continue;
            let message;
            try {
                message = JSON.parse(line);
            } catch {
                // Non-JSON output on stdout: surface it instead of swallowing.
                process.stderr.write(`[unity-mcp] non-JSON stdout: ${line}\n`);
                continue;
            }
            if (typeof message.id === "undefined" || message.id === null) {
                // Server->client notification; nothing to route.
                continue;
            }
            const entry = pending.get(message.id);
            if (entry) {
                pending.delete(message.id);
                clearTimeout(entry.timer);
                entry.resolve({ ...message, id: entry.originalId });
            } else if (message.method) {
                // Server->client request (e.g. sampling): decline politely so
                // the CLI does not stall waiting for a reply.
                try {
                    writeToChild(
                        jsonRpcError(message.id, -32601, `Method not supported by bridge: ${message.method}`),
                    );
                } catch {
                    // CLI died concurrently; the exit handler cleans up.
                }
            }
        }
    });
    child.stderr.on("data", (chunk) => {
        process.stderr.write(`[unity mcp] ${chunk}`);
    });
    child.on("exit", (code, signal) => {
        child = null;
        failPending(
            `Unity CLI exited (code=${code ?? "null"}${signal ? `, signal=${signal}` : ""}); retry the request.`,
        );
    });
    child.on("error", (error) => {
        process.stderr.write(`[unity-mcp] failed to spawn ${cliCommand}: ${error.message}\n`);
        const failedChild = child;
        child = null;
        failPending(`Cannot start Unity CLI (${cliCommand}): ${error.message}`);
        try {
            failedChild?.kill();
        } catch {
            // already dead
        }
    });
    return child;
}

function writeToChild(message) {
    if (!child || !child.stdin.writable) {
        spawnChild();
    }
    child.stdin.write(`${JSON.stringify(message)}\n`);
}

function forwardRequest(message) {
    return new Promise((resolve) => {
        let internalId;
        try {
            if (!child || !child.stdin.writable) spawnChild();
            internalId = ++nextRequestId;
            const timer = setTimeout(() => {
                pending.delete(internalId);
                resolve(
                    jsonRpcError(
                        message.id,
                        -32000,
                        `Timed out after ${REQUEST_TIMEOUT_MS}ms waiting for Unity CLI.`,
                    ),
                );
            }, REQUEST_TIMEOUT_MS);
            pending.set(internalId, { resolve, timer, originalId: message.id });
            child.stdin.write(`${JSON.stringify({ ...message, id: internalId })}\n`);
        } catch (error) {
            if (internalId) pending.delete(internalId);
            resolve(jsonRpcError(message.id, -32000, error?.message || String(error)));
        }
    });
}

function ensureChild() {
    if (!child || !child.stdin.writable) spawnChild();
    return child;
}

// ---------------------------------------------------------------------------
// HTTP MCP endpoint (stateless streamable-HTTP subset: POST + DELETE)
// ---------------------------------------------------------------------------

function handleRpcMessage(message) {
    if (Array.isArray(message)) {
        return Promise.all(message.map((entry) => handleRpcMessage(entry))).then((results) => {
            const responses = results.filter((entry) => entry !== null);
            if (responses.length === 0) return null;
            return responses.length === 1 ? responses[0] : responses;
        });
    }
    if (!message || typeof message !== "object") {
        return Promise.resolve(jsonRpcError(null, -32600, "Invalid JSON-RPC message."));
    }
    if (typeof message.id === "undefined" || message.id === null) {
        // Notification (initialized, cancelled, ...): forward, no response body.
        try {
            writeToChild(message);
        } catch (error) {
            process.stderr.write(`[unity-mcp] dropped notification: ${error.message}\n`);
        }
        return Promise.resolve(null);
    }
    return forwardRequest(message);
}

function startHttpServer(port, host, onListening) {
    const token = resolveToken();
    const server = http.createServer((request, response) => {
        const url = new URL(request.url, `http://${request.headers.host || "localhost"}`);
        if (url.pathname === "/healthz") {
            response.writeHead(200, { "content-type": "application/json" });
            response.end(
                JSON.stringify({
                    ok: Boolean(child && child.exitCode === null),
                    cli: `${cliCommand} ${cliArgs.join(" ")}`.trim(),
                    pid: child?.pid ?? null,
                }),
            );
            return;
        }
        if (url.pathname !== "/mcp") {
            response.writeHead(404).end();
            return;
        }
        if (request.method === "DELETE") {
            response.writeHead(204).end();
            return;
        }
        if (request.method !== "POST") {
            response.writeHead(405, { allow: "POST, DELETE" }).end();
            return;
        }
        if (token) {
            const authorization = request.headers.authorization || "";
            const provided = authorization.startsWith("Bearer ")
                ? authorization.slice(7)
                : "";
            if (!provided || !timingSafeEqual(provided, token)) {
                response.writeHead(401, { "content-type": "application/json" });
                response.end(JSON.stringify({ error: "unauthorized" }));
                return;
            }
        }
        const chunks = [];
        request.on("data", (chunk) => chunks.push(chunk));
        request.on("end", () => {
            let message;
            try {
                message = JSON.parse(Buffer.concat(chunks).toString("utf8"));
            } catch (error) {
                response.writeHead(400, { "content-type": "application/json" });
                response.end(
                    JSON.stringify(jsonRpcError(null, -32700, `Parse error: ${error.message}`)),
                );
                return;
            }
            try {
                ensureChild();
            } catch (error) {
                response.writeHead(200, { "content-type": "application/json" });
                response.end(JSON.stringify(jsonRpcError(null, -32000, error.message)));
                return;
            }
            handleRpcMessage(message)
                .then((result) => {
                    if (result === null) {
                        response.writeHead(202, { "content-type": "application/json" });
                        response.end("{}");
                        return;
                    }
                    response.writeHead(200, { "content-type": "application/json" });
                    response.end(JSON.stringify(result));
                })
                .catch((error) => {
                    response.writeHead(200, { "content-type": "application/json" });
                    response.end(
                        JSON.stringify(jsonRpcError(null, -32603, error?.message || String(error))),
                    );
                });
        });
    });
    server.listen(port, host, () => {
        const displayedHost = host === "0.0.0.0" ? "<all interfaces>" : host;
        console.log(`[unity-mcp] Official Unity CLI MCP bridge: ${cliCommand} ${cliArgs.join(" ")}`.trim());
        console.log(`[unity-mcp] HTTP MCP server listening on http://${displayedHost}:${port}/mcp`);
        console.log(`[unity-mcp] Health: http://${displayedHost}:${port}/healthz`);
        console.log(`[unity-mcp] Auth: ${token ? "bearer token required" : "OPEN (no UNITY_MCP_TOKEN configured)"}`);
        if (onListening) onListening();
    });
    const shutdown = () => {
        try {
            child?.kill();
        } catch {
            // already dead
        }
        server.close(() => process.exit(0));
        setTimeout(() => process.exit(0), 2000).unref();
    };
    process.on("SIGINT", shutdown);
    process.on("SIGTERM", shutdown);
}

// ---------------------------------------------------------------------------
// Startup: idempotent start, dynamic port fallback, persistence, client sync
// ---------------------------------------------------------------------------

function isPortFree(port, host) {
    return new Promise((resolve) => {
        const probe = net.createServer();
        probe.once("error", () => resolve(false));
        probe.once("listening", () => probe.close(() => resolve(true)));
        probe.listen(port, host);
    });
}

// Detect an already-running unity-mcp bridge (ours) so re-running the npm
// script is a no-op instead of a second instance or an EADDRINUSE crash.
async function probeExistingBridge(port) {
    try {
        const response = await fetch(`http://127.0.0.1:${port}/healthz`, {
            signal: AbortSignal.timeout(1500),
        });
        if (!response.ok) return false;
        const body = await response.json();
        return typeof body?.cli === "string" && body.cli.startsWith("unity mcp");
    } catch {
        return false;
    }
}

// Persist the bound port to .env.local so configure.mjs (`npm run mcp:sync`)
// and every generated frontend config follow it. Idempotent; a no-op when the
// recorded value already matches or when the port is the 9020 default and no
// override exists.
function persistPort(port) {
    const envLocalPath = path.join(WORKSPACE_ROOT, ".env.local");
    const line = `UNITY_MCP_HTTP_PORT=${port}`;
    let content = "";
    try {
        content = fs.readFileSync(envLocalPath, "utf8");
    } catch {
        content = "# Machine-local credentials (gitignored).\n";
    }
    const existing = content.match(/^UNITY_MCP_HTTP_PORT=.*$/m);
    if (existing) {
        if (existing[0].trim() === line) return false;
        content = content.replace(/^UNITY_MCP_HTTP_PORT=.*$/m, line);
    } else {
        if (port === 9020) return false;
        if (content.length > 0 && !content.endsWith("\n")) content += "\n";
        content += `\n# Auto-persisted by unity-mcp-host.mjs (dynamic port) on ${new Date().toISOString()}\n${line}\n`;
    }
    try {
        fs.writeFileSync(envLocalPath, content, "utf8");
    } catch (error) {
        process.stderr.write(
            `[unity-mcp] Could not persist port to .env.local: ${error.message}\n`,
        );
        return false;
    }
    return true;
}

// Re-run the universal configurator so every agentic frontend config picks up
// the new unity bridge port. Cross-platform: runs configure.mjs directly.
function syncClientConfigs() {
    const result = spawnSync(process.execPath, [path.join(SCRIPT_DIR, "configure.mjs")], {
        cwd: WORKSPACE_ROOT,
        stdio: "inherit",
    });
    return !result.error && result.status === 0;
}

async function main() {
    const argv = process.argv.slice(2);
    if (argv.includes("--help") || argv.includes("-h")) {
        console.log(`Usage:
  node .llm/mcp/unity-mcp-host.mjs [--port N] [--host H]

Bridges the official Unity CLI MCP server (\`unity mcp\`) to HTTP for MCP
clients that cannot run host binaries (devcontainer agents).

Startup semantics:
  - If a healthy unity-mcp bridge already serves the preferred port, exit 0
    (idempotent — safe to re-run).
  - If another process owns the port, bind the next free port, persist
    UNITY_MCP_HTTP_PORT to .env.local, and re-sync MCP client configs
    (disable with UNITY_MCP_AUTO_SYNC=0).

Environment:
  UNITY_CLI             Unity CLI binary (default: unity)
  UNITY_MCP_CLI_ARGS    Extra args appended after \`mcp\`
  UNITY_MCP_PROJECT     --project-path value (UNITY_PROJECT_PATH alias honored;
                        non-root values self-heal to the enclosing project)
  UNITY_MCP_HTTP_PORT   Preferred port (default 9020; dynamic fallback applies)
  UNITY_MCP_AUTO_SYNC   0 disables automatic client config re-sync
  UNITY_MCP_TOKEN       Optional bearer token (.env.local is honored)
  UNITY_MCP_TIMEOUT_MS  Per-request timeout (default 180000)`);
        return;
    }
    const portFlag = argv.indexOf("--port");
    const hostFlag = argv.indexOf("--host");
    const host = hostFlag !== -1 && argv[hostFlag + 1] ? argv[hostFlag + 1] : "0.0.0.0";
    let preferred = CONFIGURED_PORT;
    if (portFlag !== -1 && argv[portFlag + 1]) preferred = Number(argv[portFlag + 1]);
    if (!Number.isInteger(preferred) || preferred < 1 || preferred > 65535) {
        console.error("❌ --port must be an integer between 1 and 65535.");
        process.exit(1);
    }

    if (await probeExistingBridge(preferred)) {
        console.log(
            `[unity-mcp] A healthy unity-mcp bridge is already running on port ${preferred}; nothing to do.`,
        );
        return;
    }

    let port = preferred;
    if (!(await isPortFree(port, host))) {
        let allocated = 0;
        for (let candidate = port + 1; candidate < port + 101; candidate++) {
            if (await isPortFree(candidate, host)) {
                allocated = candidate;
                break;
            }
        }
        if (!allocated) {
            console.error(
                `❌ Port ${preferred} is busy and no free port was found in ` +
                    `${preferred + 1}-${preferred + 100}. Free the port or pass ` +
                    "--port <n> / set UNITY_MCP_HTTP_PORT.",
            );
            process.exit(1);
        }
        console.log(
            `[unity-mcp] Port ${preferred} is busy; dynamically selected port ${allocated}.`,
        );
        port = allocated;
    }

    startHttpServer(port, host, () => {
        if (!persistPort(port)) return;
        console.log(
            `[unity-mcp] Persisted UNITY_MCP_HTTP_PORT=${port} to .env.local so client configs follow.`,
        );
        if (process.env.UNITY_MCP_AUTO_SYNC === "0") {
            console.log("[unity-mcp] AUTO_SYNC disabled; run `npm run mcp:sync` to update MCP clients.");
            return;
        }
        console.log("[unity-mcp] Syncing MCP client configs across agentic frontends…");
        if (syncClientConfigs()) {
            console.log("[unity-mcp] MCP client configs updated (unity URL follows the new port).");
        } else {
            console.log("[unity-mcp] ⚠️ Config sync failed; run `npm run mcp:sync` manually.");
        }
    });
}

main();
