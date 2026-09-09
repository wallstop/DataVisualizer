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
//   npm run unity:mcp:host        # proxies `unity mcp` on http://127.0.0.1:9020/mcp
// Container clients then connect to http://host.docker.internal:9020/mcp.
//
// The bridge is transparent: JSON-RPC requests are forwarded verbatim to the
// CLI process (request ids are remapped so concurrent HTTP requests cannot
// collide), and responses/notifications stream back untouched.
//
// Environment:
//   UNITY_CLI            Path to the Unity CLI binary (default: `unity`)
//   UNITY_MCP_CLI_ARGS   Extra args appended to `unity mcp` (space-split)
//   UNITY_MCP_PROJECT    Project path passed as --project-path (falls back to
//                        UNITY_PROJECT_PATH when set)
//   UNITY_MCP_HTTP_PORT  Port (default 9020)
//   UNITY_MCP_TOKEN      Optional bearer token (also read from .env.local)
//   UNITY_MCP_TIMEOUT_MS Per-request timeout (default 180000)

import net from "node:net";
import http from "node:http";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const WORKSPACE_ROOT = path.resolve(SCRIPT_DIR, "..", "..");
const DEFAULT_PORT = Number(process.env.UNITY_MCP_HTTP_PORT || 9020);
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
const projectPath = process.env.UNITY_MCP_PROJECT || envLocal.UNITY_PROJECT_PATH || "";
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

function startHttpServer(port, host) {
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

const argv = process.argv.slice(2);
if (argv.includes("--help") || argv.includes("-h")) {
    console.log(`Usage:
  node .llm/mcp/unity-mcp-host.mjs [--port N] [--host H]

Bridges the official Unity CLI MCP server (\`unity mcp\`) to HTTP on :9020 for
MCP clients that cannot run host binaries (devcontainer agents).

Environment:
  UNITY_CLI             Unity CLI binary (default: unity)
  UNITY_MCP_CLI_ARGS    Extra args appended after \`mcp\`
  UNITY_MCP_PROJECT     --project-path value (falls back to UNITY_PROJECT_PATH)
  UNITY_MCP_HTTP_PORT   Port (default 9020)
  UNITY_MCP_TOKEN       Optional bearer token (.env.local is honored)
  UNITY_MCP_TIMEOUT_MS  Per-request timeout (default 180000)`);
} else {
    const portFlag = argv.indexOf("--port");
    const hostFlag = argv.indexOf("--host");
    const port =
        portFlag !== -1 && argv[portFlag + 1] ? Number(argv[portFlag + 1]) : DEFAULT_PORT;
    const host = hostFlag !== -1 && argv[hostFlag + 1] ? argv[hostFlag + 1] : "127.0.0.1";
    if (!Number.isInteger(port) || port < 1 || port > 65535) {
        console.error("❌ --port must be an integer between 1 and 65535.");
        process.exit(1);
    }
    startHttpServer(port, host);
}
