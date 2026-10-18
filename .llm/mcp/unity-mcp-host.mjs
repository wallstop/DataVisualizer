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
import { spawn, execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const WORKSPACE_ROOT = path.resolve(SCRIPT_DIR, "..", "..");
const INSTANCE_ID = crypto.randomUUID();

function loadSettings() {
    const script = path.join(WORKSPACE_ROOT, "scripts", "unity_mcp_settings.py");
    for (const [command, prefix] of [["uv", ["run", "--no-project"]], ["python3", []], ["python", []]]) {
        try {
            return JSON.parse(execFileSync(command, [...prefix, script, "--workspace", WORKSPACE_ROOT, "--home", os.homedir()], {
                encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
            }));
        } catch {
            // Settings resolution is read-only; another available interpreter may work.
        }
    }
    throw new Error("Cannot load Unity MCP startup settings; verify Python 3.9+ or uv and scripts/unity_mcp_settings.py.");
}

const settings = loadSettings();
const REQUEST_TIMEOUT_MS = Number(settings.timeout);
const CONFIGURED_PORT = Number(settings.port);
const cliCommand = settings.cliCommand;
const cliArgs = settings.cliArgs;
const projectPath = settings.projectPath;
for (const warning of settings.warnings) process.stderr.write(warning + "\n");

function resolveToken() {
    return settings.token;
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
    stdoutBuffer = "";
    const spawned = spawn(cliCommand, cliArgs, {
        stdio: ["pipe", "pipe", "pipe"],
        env: process.env,
    });
    child = spawned;
    // A closed pipe must fail the current requests instead of crashing the
    // HTTP bridge with an unhandled EPIPE. Old children cannot clear a newer
    // child's state when their exit events arrive late.
    spawned.stdin.on("error", (error) => {
        if (child !== spawned) return;
        child = null;
        stdoutBuffer = "";
        failPending(`Unity CLI input closed: ${error.message}; retry the request.`);
        spawned.kill();
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
        if (child !== spawned) return;
        child = null;
        stdoutBuffer = "";
        failPending(
            `Unity CLI exited (code=${code ?? "null"}${signal ? `, signal=${signal}` : ""}); retry the request.`,
        );
    });
    child.on("error", (error) => {
        if (child !== spawned) return;
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
            if (internalId) {
                const entry = pending.get(internalId);
                if (entry) clearTimeout(entry.timer);
                pending.delete(internalId);
            }
            resolve(jsonRpcError(message.id, -32000, error?.message || String(error)));
        }
    });
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

function sendEarlyResponse(request, response, status, headers = {}, body = "") {
    // Send the complete reply now, but let the client finish its request before
    // ending the response. Closing over an unread body can reset Windows clients.
    if (status !== 204) {
        response.setHeader("content-length", Buffer.byteLength(body));
    }
    response.writeHead(status, headers);
    response.flushHeaders();
    if (body) response.write(body);
    if (request.readableEnded) {
        response.end();
        return;
    }
    request.once("end", () => response.end());
    request.resume();
}

function startHttpServer(port, host, onListening) {
    const token = resolveToken();
    const server = http.createServer((request, response) => {
        const url = new URL(request.url, `http://${request.headers.host || "localhost"}`);
        if (url.pathname === "/healthz") {
            sendEarlyResponse(
                request,
                response,
                200,
                { "content-type": "application/json" },
                JSON.stringify({
                    bridge: "dataviz-unity-mcp",
                    instanceId: INSTANCE_ID,
                    bridgePid: process.pid,
                    ok: Boolean(child && child.exitCode === null),
                    cli: `${cliCommand} ${cliArgs.join(" ")}`.trim(),
                    projectPath,
                    authRequired: Boolean(token),
                    pid: child?.pid ?? null,
                }),
            );
            return;
        }
        if (url.pathname !== "/mcp") {
            sendEarlyResponse(request, response, 404);
            return;
        }
        if (token) {
            const authorization = request.headers.authorization || "";
            const provided = authorization.startsWith("Bearer ")
                ? authorization.slice(7)
                : "";
            if (!provided || !timingSafeEqual(provided, token)) {
                sendEarlyResponse(request, response, 401, { "content-type": "application/json" }, JSON.stringify({ error: "unauthorized" }));
                return;
            }
        }
        if (request.method === "DELETE") {
            sendEarlyResponse(request, response, 204);
            return;
        }
        if (request.method !== "POST") {
            sendEarlyResponse(request, response, 405, { allow: "POST, DELETE" });
            return;
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
            handleRpcMessage(message)
                .then((result) => {
                    if (result === null) {
                        response.writeHead(202).end();
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
        // A listen callback proves only that a socket was bound. Check the
        // actual HTTP response and instance identity before publishing configs.
        verifyLocalHttp(port, host)
            .then(async () => {
                console.log(`[unity-mcp] Local HTTP self-check passed (bridge PID ${process.pid}, instance ${INSTANCE_ID}).`);
                if (onListening) await onListening();
                console.log("[unity-mcp] Startup complete.");
            })
            .catch((error) => {
                console.error(`[unity-mcp] Startup failed: ${error.message}`);
                console.error("[unity-mcp] Check the port's owning process and VS Code Ports view for a reverse tunnel. Unity has not been verified.");
                process.exitCode = 1;
                failPending("Bridge startup failed.");
                child?.kill();
                server.closeAllConnections();
                server.close();
            });
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

function verifyLocalHttp(port, host) {
    const hostname = host === "0.0.0.0" ? "127.0.0.1" : host === "::" ? "::1" : host;
    return new Promise((resolve, reject) => {
        // Core http bypasses HTTP_PROXY, ALL_PROXY, and fetch dispatchers.
        const request = http.get({
            hostname, port, path: "/healthz", agent: false,
            signal: AbortSignal.timeout(3000),
        }, (response) => {
            let body = "";
            response.setEncoding("utf8");
            response.on("error", reject);
            response.on("data", (chunk) => { body += chunk; });
            response.on("end", () => {
                try {
                    const health = JSON.parse(body);
                    if (response.statusCode !== 200 || health.instanceId !== INSTANCE_ID) {
                        throw new Error("the loopback endpoint did not answer as this bridge instance");
                    }
                    resolve();
                } catch (error) {
                    reject(new Error(`Local HTTP self-check failed on ${hostname}:${port}: ${error.message}`));
                }
            });
        });
        request.on("error", (error) => reject(new Error(
            `Local HTTP self-check failed on ${hostname}:${port}: ${error.message}`,
        )));
    });
}

function canBindPort(port, host) {
    return new Promise((resolve) => {
        const probe = net.createServer();
        probe.once("error", () => resolve(false));
        probe.once("listening", () => probe.close(() => resolve(true)));
        probe.listen({ port, host, exclusive: true });
    });
}

// Windows can bind a wildcard socket beside a loopback-only listener. The
// specific listener then receives loopback traffic instead of this bridge.
// Match DxCommandTerminal's check: reserve both addresses before choosing a port.
async function isPortFree(port, host) {
    if (!(await canBindPort(port, host))) return false;
    if (host === "0.0.0.0" || host === "::" || host === "localhost") {
        return canBindPort(port, "127.0.0.1");
    }
    return true;
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
        return body?.bridge === "dataviz-unity-mcp" ||
            (typeof body?.cli === "string" && /(?:^|[/\\])unity(?:\.exe)? mcp(?: |$)/.test(body.cli));
    } catch {
        return false;
    }
}

// Persist the bound port to .env.local so configure.mjs (`npm run mcp:sync`)
// and every generated frontend config follow it. Idempotent; a no-op when the
// recorded value already matches or when the port is the 9020 default and no
// override exists.
async function persistPort(port) {
    const envLocalPath = path.join(WORKSPACE_ROOT, ".env.local");
    const line = `UNITY_MCP_HTTP_PORT=${port}`;
    let content = "";
    try {
        content = await fs.promises.readFile(envLocalPath, "utf8");
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
        await fs.promises.writeFile(envLocalPath, content, "utf8");
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
function syncClientConfigs(port) {
    // Configuration can stall on host filesystem or command discovery. Keep
    // the HTTP event loop responsive while it runs, with a bounded lifetime.
    return new Promise((resolve) => {
        const sync = spawn(process.execPath, [path.join(SCRIPT_DIR, "configure.mjs")], {
            cwd: WORKSPACE_ROOT,
            stdio: "inherit",
            env: { ...process.env, UNITY_MCP_HTTP_PORT: String(port) },
            timeout: 30000,
        });
        sync.once("error", () => resolve(false));
        sync.once("exit", (code) => resolve(code === 0));
    });
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

    startHttpServer(port, host, async () => {
        if (!(await persistPort(port))) return;
        console.log(
            `[unity-mcp] Persisted UNITY_MCP_HTTP_PORT=${port} to .env.local so client configs follow.`,
        );
        if (settings.autoSync === "0") {
            console.log("[unity-mcp] AUTO_SYNC disabled; run `npm run mcp:sync` to update MCP clients.");
            return;
        }
        console.log("[unity-mcp] Syncing MCP client configs across agentic frontends…");
        if (await syncClientConfigs(port)) {
            console.log("[unity-mcp] MCP client configs updated (unity URL follows the new port).");
        } else {
            console.log("[unity-mcp] ⚠️ Config sync failed; run `npm run mcp:sync` manually.");
        }
    });
}

main();
