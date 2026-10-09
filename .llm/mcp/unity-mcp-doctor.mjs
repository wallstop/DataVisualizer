#!/usr/bin/env node
// Unity MCP doctor — end-to-end health check for the official Unity CLI MCP
// bridge (`npm run unity:mcp:host` → `unity mcp` → HTTP :9020).
//
// Encodes the failure modes observed in the wild:
//   1. `unity run unity:mcp:host` is NOT a valid command: `unity run` takes a
//      project path; the bridge is an npm script (`npm run unity:mcp:host`).
//   2. A project pin that is not a Unity project root (e.g. `<root>/Packages`)
//      silently unpins the server; an unpinned `unity mcp` attaches to
//      whichever Editor happens to be running — the wrong project.
//   3. The pipeline package must be installed in the pinned project.
//   4. Tools only drive a running Editor for the pinned project.
//
// Exit code 0 = healthy, 1 = action required. Never prints secret values.

import { execFileSync, spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const WORKSPACE_ROOT = path.resolve(SCRIPT_DIR, "..", "..");

let failures = 0;
let warnings = 0;
const fail = (message) => {
    failures += 1;
    console.log(`❌ ${message}`);
};
const warn = (message) => {
    warnings += 1;
    console.log(`⚠️  ${message}`);
};
const ok = (message) => console.log(`✅ ${message}`);

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

// Same port precedence as unity-mcp-host.mjs (dynamic ports get persisted to
// .env.local, so the doctor must follow the persisted value too).
const PORT = Number(
    process.env.UNITY_MCP_HTTP_PORT || envLocal.UNITY_MCP_HTTP_PORT || 9020,
);
const BASE = `http://127.0.0.1:${PORT}`;

function isUnityProjectRoot(dir) {
    try {
        return fs
            .statSync(path.join(dir, "ProjectSettings", "ProjectVersion.txt"))
            .isFile();
    } catch {
        return false;
    }
}

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

// ---------------------------------------------------------------------------
// 1. Unity CLI
// ---------------------------------------------------------------------------
let cliVersion = null;
try {
    cliVersion = execFileSync("unity", ["--version"], { encoding: "utf8" }).trim();
    ok(`Unity CLI on PATH: ${cliVersion}`);
} catch {
    fail(
        "Unity CLI not runnable (`unity --version` failed). Install it or set UNITY_CLI; " +
            "the bridge spawns `unity mcp`.",
    );
}

// ---------------------------------------------------------------------------
// 2. Project pin resolution (mirrors unity-mcp-host.mjs precedence)
// ---------------------------------------------------------------------------
const configured =
    process.env.UNITY_MCP_PROJECT ||
    process.env.UNITY_PROJECT_PATH ||
    envLocal.UNITY_MCP_PROJECT ||
    envLocal.UNITY_PROJECT_PATH ||
    "";

let project = "";
if (configured) {
    project = path.resolve(configured);
    if (isUnityProjectRoot(project)) {
        ok(`Project pin resolves to a Unity project root: ${project}`);
    } else {
        const enclosing = findEnclosingUnityProject(project);
        if (enclosing) {
            warn(
                `Configured project '${configured}' is not a project root; the bridge ` +
                    `self-heals to the enclosing project '${enclosing}'. Fix the config value.`,
            );
            project = enclosing;
        } else {
            fail(
                `Configured project '${configured}' is not a Unity project root and no ` +
                    "enclosing project exists; the bridge would start UNPINNED (wrong-editor risk).",
            );
        }
    }
} else {
    const enclosing = findEnclosingUnityProject(WORKSPACE_ROOT);
    if (enclosing) {
        warn(
            `No project pin configured; the bridge falls back to the enclosing Unity ` +
                `project '${enclosing}'. Set UNITY_MCP_PROJECT in .env.local to be explicit.`,
        );
        project = enclosing;
    } else {
        fail(
            "No project pin configured and no enclosing Unity project found; set " +
                "UNITY_MCP_PROJECT in .env.local to the Unity project root.",
        );
    }
}

// ---------------------------------------------------------------------------
// 3. Pipeline package in the pinned project
// ---------------------------------------------------------------------------
if (project) {
    try {
        const manifest = JSON.parse(
            fs.readFileSync(path.join(project, "Packages", "manifest.json"), "utf8"),
        );
        const pipelineKey = Object.keys(manifest.dependencies ?? {}).find((key) =>
            key.includes("pipeline"),
        );
        if (pipelineKey) {
            ok(`Pipeline package present in project manifest: ${pipelineKey}`);
        } else {
            fail(
                "com.unity.pipeline missing from the project manifest; run " +
                    `unity pipeline install --project-path ${project} (unity mcp tools need it).`,
            );
        }
    } catch (error) {
        warn(`Could not read project Packages/manifest.json: ${error.message}`);
    }

    // 4. Running editors for the pinned project
    try {
        const status = JSON.parse(
            execFileSync("unity", ["status", "--format", "json"], { encoding: "utf8" }),
        );
        const instances = status?.data?.instances ?? [];
        const editors = instances.map((instance) => instance.project);
        if (editors.length === 0) {
            warn(
                "No Unity Editor is running; editor-dependent tools will fail until one is. " +
                    `Start it with: unity open ${project}`,
            );
        } else if (!editors.some((editor) => path.resolve(editor) === project)) {
            console.log(
                `ℹ️  \`unity status\` only lists editor(s) for: ${editors.join(", ")}. It does ` +
                    "not list every reachable Editor; the editor_status canary below is " +
                    "authoritative for whether the pinned project is drivable.",
            );
        } else {
            ok(`A running Editor belongs to the pinned project.`);
        }
    } catch {
        warn("Could not query `unity status` for running editors.");
    }
}

// ---------------------------------------------------------------------------
// 5. Bridge HTTP endpoint + MCP roundtrip + wrong-editor canary
// ---------------------------------------------------------------------------
async function main() {
    let health;
    try {
        const response = await fetch(`${BASE}/healthz`);
        health = await response.json();
        ok(`Bridge healthz: ok=${health.ok} cli=${JSON.stringify(health.cli)}`);
    } catch {
        console.log(
            `ℹ️  Bridge not reachable on ${BASE} — start it on the host with: ` +
                "npm run unity:mcp:host",
        );
        return finish();
    }

    const headers = { "content-type": "application/json" };
    const token = process.env.UNITY_MCP_TOKEN || envLocal.UNITY_MCP_TOKEN || "";
    if (token) headers.authorization = `Bearer ${token}`;

    if (health.cli && !health.cli.includes("--project-path")) {
        warn(
            "Bridge CLI command has no --project-path pin; it may attach to whichever " +
                "Editor is running. Restart the bridge with UNITY_MCP_PROJECT set.",
        );
    }

    async function rpc(id, method, params) {
        const response = await fetch(`${BASE}/mcp`, {
            method: "POST",
            headers,
            body: JSON.stringify({ jsonrpc: "2.0", id, method, params }),
        });
        return response.json();
    }

    try {
        const init = await rpc(1, "initialize", {
            protocolVersion: "2025-03-26",
            capabilities: {},
            clientInfo: { name: "unity-mcp-doctor", version: "0.1.0" },
        });
        ok(`MCP initialize: server=${JSON.stringify(init.result?.serverInfo ?? init.error)}`);
    } catch (error) {
        fail(`MCP initialize failed: ${error.message}`);
        return finish();
    }

    try {
        const tools = await rpc(2, "tools/list", {});
        const count = tools.result?.tools?.length ?? 0;
        if (count === 0) fail("tools/list returned 0 tools.");
        else ok(`tools/list: ${count} tools available.`);
    } catch (error) {
        fail(`tools/list failed: ${error.message}`);
    }

    try {
        const call = await rpc(3, "tools/call", { name: "editor_status", arguments: {} });
        const text = call.result?.content?.[0]?.text ?? JSON.stringify(call.error);
        let status;
        try {
            status = JSON.parse(text);
        } catch {
            status = null;
        }
        if (status?.projectPath) {
            const driven = path.resolve(status.projectPath);
            if (project && driven === project) {
                ok(`editor_status drives the pinned project: ${driven}`);
            } else {
                fail(
                    `WRONG EDITOR: the MCP server drives '${driven}' but the pinned project ` +
                        `is '${project}'. Restart the bridge after fixing UNITY_MCP_PROJECT.`,
                );
            }
        } else {
            warn(
                `editor_status did not return a project (editor for the pinned project ` +
                    "likely not running): " +
                    text.slice(0, 200).replace(/\s+/g, " "),
            );
        }
    } catch (error) {
        warn(`editor_status canary failed: ${error.message}`);
    }

    finish();
}

function finish() {
    console.log(
        `\nDoctor summary: ${failures} failure(s), ${warnings} warning(s). ` +
            (failures === 0 ? "Bridge stack healthy." : "Address the ❌ items above."),
    );
    process.exit(failures === 0 ? 0 : 1);
}

main();