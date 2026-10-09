#!/usr/bin/env node
// MCP connectivity doctor — verifies every credential-dependent MCP server in
// this workspace from whichever environment it runs in (host or devcontainer).
//
// Failure classes encoded from real incidents:
//   1. Credentials exist in .env.local but the running agent predates them:
//      {env:VAR} / ${env:VAR} references expand at client startup, so agents
//      must be restarted from a fresh shell (bashrc loads .env.local).
//   2. VS Code extension hosts never run the shell loader; remoteEnv forwards
//      (${localEnv:VAR}) are empty unless the host exported the var. VS Code
//      entries are therefore generated as env-sourcing wrappers.
//   3. Shared machine-local configs must be environment-agnostic:
//      unity bridge URL uses host.docker.internal (reachable from host AND
//      container) and zai-image uses PATH-resolved `node` with a
//      workspace-relative script path.
//
// Exit 0 = healthy, 1 = action required. Never prints secret values.

import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const WORKSPACE_ROOT = process.env.DATAVIZ_MCP_WORKSPACE
    ? path.resolve(process.env.DATAVIZ_MCP_WORKSPACE)
    : path.resolve(SCRIPT_DIR, "..", "..");

const IN_CONTAINER =
    fs.existsSync("/.dockerenv") ||
    fs.existsSync("/run/.containerenv") ||
    process.env.DATAVIZ_IN_CONTAINER === "1";

// `--repair` may reconcile divergent credential pairs in .env.local after
// probing which one actually authenticates. Default stays read-only.
const REPAIR = process.argv.includes("--repair");

// Probe endpoints are overridable so the contract tests can drive the repair
// path against a local stub (deterministic, no network, no real credentials),
// and so a proxied or self-hosted MCP endpoint can be probed instead.
const PROBE_TIMEOUT_MS = Number(process.env.DATAVIZ_MCP_PROBE_TIMEOUT_MS) || 20000;
// Set by the credential contract tests: run the credential/config checks only,
// skipping the network and stdio probes that are covered by other suites.
const SKIP_NETWORK_PROBES = process.env.DATAVIZ_MCP_STUB_SKIP_PROBES === "1";
const GITHUB_PROBE_URL =
    process.env.DATAVIZ_MCP_GITHUB_PROBE_URL || "https://api.githubcopilot.com/mcp/";
const ZAI_PROBE_URL =
    process.env.DATAVIZ_MCP_ZAI_PROBE_URL ||
    "https://api.z.ai/api/mcp/web_search_prime/mcp";

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
const info = (message) => console.log(`ℹ️  ${message}`);

function readEnvLocal(filePath) {
    const vars = {};
    let text;
    try {
        text = fs.readFileSync(filePath, "utf8");
    } catch {
        return vars;
    }
    text = text.replace(/^\uFEFF/, "");
    for (const rawLine of text.split(/\r?\n/)) {
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
const env = (name) => process.env[name] || envLocal[name] || "";

// Which of two names actually supplied a value, and whether they DISAGREE.
// Presence checks are not enough: the incident this guards against was two
// names set to different values, where the presence check passed and only the
// endpoint probe failed with an opaque 401.
function resolveCredential(primaryName, alternateName) {
    const primary = env(primaryName);
    const alternate = env(alternateName);
    const source = primary ? primaryName : alternate ? alternateName : null;
    return {
        token: primary || alternate,
        source,
        primary,
        alternate,
        divergent: Boolean(primary && alternate && primary !== alternate),
    };
}

const ENV_LOCAL_PATH = path.join(WORKSPACE_ROOT, ".env.local");

// Rewrite ONE key in place so a reconcile never appends a duplicate key.
// .env.local is last-wins when parsed, so appending would work but grows the
// file on every run; in-place editing keeps it stable and reviewable.
function rewriteEnvLocalKey(key, value) {
    let text;
    let hadBom = false;
    try {
        text = fs.readFileSync(ENV_LOCAL_PATH, "utf8");
    } catch {
        return false;
    }
    // Strip the BOM only for matching, then restore it, so a repair leaves the
    // file byte-identical apart from the one rewritten value. A dropped BOM is
    // invisible here but breaks tools that are not BOM-tolerant.
    if (text.startsWith("\uFEFF")) {
        hadBom = true;
        text = text.slice(1);
    }
    const pattern = new RegExp(`^(\\s*(?:export\\s+)?${key}\\s*=).*$`, "m");
    if (!pattern.test(text)) return false;
    if (hadBom) text = `\uFEFF${text}`;
    fs.writeFileSync(ENV_LOCAL_PATH, text.replace(pattern, `$1${value}`), { mode: 0o600 });
    return true;
}

console.log(
    `Environment: ${IN_CONTAINER ? "devcontainer" : "host"} · workspace: ${WORKSPACE_ROOT}`,
);

// ---------------------------------------------------------------------------
// 1. Credential presence (env var, then .env.local — same precedence clients
//    effectively rely on)
// ---------------------------------------------------------------------------
console.log("--- Credentials ---");

const github = resolveCredential("GITHUB_PERSONAL_ACCESS_TOKEN", "GITHUB_MCP_PAT");
const githubToken = github.token;
if (githubToken) {
    ok(`GitHub PAT resolved from ${github.source} (names checked: GITHUB_PERSONAL_ACCESS_TOKEN, GITHUB_MCP_PAT)`);
} else {
    fail(
        "No GitHub PAT. Add GITHUB_PERSONAL_ACCESS_TOKEN (or GITHUB_MCP_PAT) to " +
            ".env.local, run `npm run mcp:sync`, then restart agents from a fresh shell.",
    );
}
if (github.divergent) {
    fail(
        "GitHub credentials divergent: GITHUB_PERSONAL_ACCESS_TOKEN and GITHUB_MCP_PAT " +
            "are both set to different values. Generated configs use " +
            "GITHUB_PERSONAL_ACCESS_TOKEN, so a stale value there makes the github MCP " +
            "return 401 even when GITHUB_MCP_PAT is valid. Run `npm run mcp:doctor --repair`.",
    );
}

const zai = resolveCredential("ZAI_API_KEY", "Z_AI_API_KEY");
const zaiToken = zai.token;
if (zaiToken) {
    ok(`Z.ai key resolved from ${zai.source} (names checked: ZAI_API_KEY, Z_AI_API_KEY)`);
} else {
    fail("No Z.ai key. Add ZAI_API_KEY to .env.local, run `npm run mcp:sync`, restart agents.");
}
if (zai.divergent) {
    fail(
        "Z.ai credentials divergent: ZAI_API_KEY and Z_AI_API_KEY are both set to " +
            "different values. Run `npm run mcp:doctor --repair`.",
    );
}
if (!env("UNITY_MCP_TOKEN")) {
    warn("UNITY_MCP_TOKEN not set — the unity bridge accepts unauthenticated calls.");
}

// ---------------------------------------------------------------------------
// 2. Generated config flavor (must be environment-agnostic)
// ---------------------------------------------------------------------------
console.log("--- Config flavor (shared machine-local files) ---");
let opencodeConfig = null;
try {
    opencodeConfig = JSON.parse(
        fs.readFileSync(path.join(WORKSPACE_ROOT, "opencode.json"), "utf8"),
    );
} catch {}
if (opencodeConfig?.mcp?.servers) {
    const unity = opencodeConfig.mcp.servers.unity;
    if (unity?.url?.includes("127.0.0.1")) {
        fail(
            `opencode.json unity URL uses 127.0.0.1 (${unity.url}) — unreachable from ` +
                "containers. Run `npm run mcp:sync` with the updated configurator.",
        );
    } else if (unity?.url) {
        ok(`opencode.json unity URL: ${unity.url}`);
    }
    const zaiImage = opencodeConfig.mcp.servers["zai-image"];
    const zaiImageCmd = Array.isArray(zaiImage?.command)
        ? zaiImage.command.join(" ")
        : String(zaiImage?.command ?? "");
    if (/^[A-Za-z]:\\|^\//.test(zaiImageCmd.split(" ")[0] ?? "")) {
        fail(
            `opencode.json zai-image uses an absolute binary path (${zaiImageCmd}) — breaks ` +
                "the other environment. Run `npm run mcp:sync` with the updated configurator.",
        );
    } else if (zaiImageCmd) {
        ok(`opencode.json zai-image command is environment-agnostic: ${zaiImageCmd}`);
    }
} else {
    warn("opencode.json not found or has no mcp.servers block; run `npm run mcp:sync`.");
}

// ---------------------------------------------------------------------------
// 3. Endpoint auth probes (status codes only)
// ---------------------------------------------------------------------------
async function probeRemote(label, url, token) {
    // Returns true when the endpoint accepted the credential, so callers can
    // compare two candidate credentials for the same server.
    try {
        const res = await fetch(url, {
            method: "POST",
            headers: {
                "content-type": "application/json",
                accept: "application/json, text/event-stream",
                ...(token ? { authorization: `Bearer ${token}` } : {}),
            },
            body: JSON.stringify({
                jsonrpc: "2.0",
                id: 1,
                method: "initialize",
                params: {
                    protocolVersion: "2025-03-26",
                    capabilities: {},
                    clientInfo: { name: "mcp-doctor", version: "0.1.0" },
                },
            }),
            signal: AbortSignal.timeout(PROBE_TIMEOUT_MS),
        });
        if (res.status === 401 || res.status === 403) {
            fail(`${label}: HTTP ${res.status} (auth rejected — token invalid or env var empty at client startup)`);
            return false;
        }
        ok(`${label}: HTTP ${res.status}`);
        return true;
    } catch (error) {
        fail(`${label}: ${error.message}`);
        return false;
    }
}

if (!SKIP_NETWORK_PROBES) {
console.log("--- Endpoint probes (status only) ---");
await probeRemote("github MCP", GITHUB_PROBE_URL, githubToken);
await probeRemote("zai-web-search", ZAI_PROBE_URL, zaiToken);
}

// Runs AFTER the probes above so the report always describes the state that was
// actually observed. Re-probing afterwards would report the repaired value and
// hide the failure that prompted the run.
// ---------------------------------------------------------------------------
// 3b. Repair mode: reconcile divergent credential pairs by probing them
// ---------------------------------------------------------------------------
// Writes only the gitignored .env.local, only when --repair is passed, and
// only after an endpoint probe proves which name authenticates. Without a
// proven-good value it refuses to guess — a wrong guess reintroduces the
// silent-401 this exists to prevent.
async function reconcile(label, primaryName, alternateName, url, resolved) {
    if (!resolved.divergent) return false;
    const [primaryOk, alternateOk] = await Promise.all([
        probeRemote(`  ↳ ${primaryName}`, url, resolved.primary),
        probeRemote(`  ↳ ${alternateName}`, url, resolved.alternate),
    ]);
    const liveName = primaryOk ? primaryName : alternateOk ? alternateName : null;
    if (!liveName) {
        info(
            `  ${label}: neither name authenticates — both PATs are likely expired or ` +
                "revoked. Issue a new one and set both names in .env.local.",
        );
        return false;
    }
    const staleName = liveName === primaryName ? alternateName : primaryName;
    const liveValue = liveName === primaryName ? resolved.primary : resolved.alternate;
    info(
        `  ${label}: ${liveName} authenticates, ${staleName} is rejected. ` +
            `Repairing .env.local (${staleName} := ${liveName}).`,
    );
    if (rewriteEnvLocalKey(staleName, liveValue)) {
        info(
            `  ${label}: wrote ${staleName} in .env.local. Restart agent CLIs from a ` +
                "fresh shell — {env:VAR} references expand at agent startup.",
        );
        return true;
    }
    info(
        `  ${label}: could not update .env.local (key ${staleName} not present or file ` +
            "unwritable). Set it manually, then restart agent CLIs.",
    );
    return false;
}

if (REPAIR) {
    let repaired = 0;
    if (
        (await reconcile(
            "GitHub",
            "GITHUB_PERSONAL_ACCESS_TOKEN",
            "GITHUB_MCP_PAT",
            GITHUB_PROBE_URL,
            github,
        ))
    )
        repaired++;
    if (
        (await reconcile(
            "Z.ai",
            "ZAI_API_KEY",
            "Z_AI_API_KEY",
            ZAI_PROBE_URL,
            zai,
        ))
    )
        repaired++;
    if (repaired > 0) {
        info(
            "Repair complete. In this devcontainer also restart any already-running " +
                "agent CLIs (they hold the stale value in their own process environment).",
        );
    }
}

if (SKIP_NETWORK_PROBES) {
    info(
        "Skipping endpoint, stdio, and unity-bridge probes (DATAVIZ_MCP_STUB_SKIP_PROBES=1) " +
            "— credential and config checks only.",
    );
} else {

// ---------------------------------------------------------------------------
// 4. stdio spawn probes — the exact path an MCP client takes
// ---------------------------------------------------------------------------
function spawnProbe(label, command, args, envOverrides) {
    return new Promise((resolve) => {
        // Node refuses to spawn .cmd/.bat shims without a shell (CVE-2024-27980);
        // under shell:true pass one command string to avoid DEP0190.
        const useShell =
            process.platform === "win32" && /\.(cmd|bat)$/i.test(command);
        const child = spawn(
            useShell ? `${command} ${args.join(" ")}` : command,
            useShell ? [] : args,
            {
                cwd: WORKSPACE_ROOT,
                env: { ...process.env, ...envOverrides },
                stdio: ["pipe", "pipe", "pipe"],
                shell: useShell,
            },
        );
        let settled = false;
        const done = (message) => {
            if (settled) return;
            settled = true;
            console.log(`${label}: ${message}`);
            try {
                child.kill();
            } catch {}
            setTimeout(resolve, 200);
        };
        const timer = setTimeout(() => done("❌ TIMEOUT (no response in 25s)"), 25000);
        child.stdout.setEncoding("utf8");
        child.stdout.on("data", (chunk) => {
            for (const line of chunk.split("\n")) {
                if (!line.trim()) continue;
                try {
                    const message = JSON.parse(line);
                    if (message.result?.serverInfo) {
                        clearTimeout(timer);
                        ok(
                            `${label}: serverInfo=${JSON.stringify(message.result.serverInfo)}`,
                        );
                        done("");
                    } else if (message.error) {
                        clearTimeout(timer);
                        fail(`${label}: ${JSON.stringify(message.error).slice(0, 200)}`);
                        done("");
                    }
                } catch {}
            }
        });
        child.on("error", (error) => {
            clearTimeout(timer);
            fail(`${label}: spawn failed (${error.message})`);
            done("");
        });
        child.stdin.write(
            `${JSON.stringify({
                jsonrpc: "2.0",
                id: 1,
                method: "initialize",
                params: {
                    protocolVersion: "2025-03-26",
                    capabilities: {},
                    clientInfo: { name: "mcp-doctor", version: "0.1.0" },
                },
            })}\n`,
        );
    });
}

console.log("--- stdio spawn probes (cwd = workspace root) ---");
// Windows: npx is a .cmd shim — spawn() resolves it only with the extension.
const NPX = process.platform === "win32" ? "npx.cmd" : "npx";
if (zaiToken) {
    await spawnProbe("zai-vision", NPX, ["-y", "@z_ai/mcp-server"], {
        ZAI_API_KEY: zaiToken,
        Z_AI_MODE: env("Z_AI_MODE") || "ZAI",
    });
}
await spawnProbe("zai-image", "node", [".llm/mcp/zai-image-mcp.mjs"], {
    ZAI_API_KEY: zaiToken,
});

// ---------------------------------------------------------------------------
// 5. Unity HTTP bridge reachability from THIS environment
// ---------------------------------------------------------------------------
console.log("--- Unity bridge ---");
const port = Number(env("UNITY_MCP_HTTP_PORT")) || 9020;
const bridgeBase = `http://host.docker.internal:${port}`;
try {
    const res = await fetch(`${bridgeBase}/healthz`, {
        signal: AbortSignal.timeout(5000),
    });
    const body = await res.json();
    if (body.ok) {
        ok(`unity bridge: ${bridgeBase}/healthz ok=true`);
    } else {
        // The CLI child is spawned lazily on the first request; drive one to
        // prove the stack end-to-end instead of warning on the idle state.
        const token = env("UNITY_MCP_TOKEN");
        const mcpRes = await fetch(`${bridgeBase}/mcp`, {
            method: "POST",
            headers: {
                "content-type": "application/json",
                accept: "application/json, text/event-stream",
                ...(token ? { authorization: `Bearer ${token}` } : {}),
            },
            body: JSON.stringify({
                jsonrpc: "2.0",
                id: 1,
                method: "initialize",
                params: {
                    protocolVersion: "2025-03-26",
                    capabilities: {},
                    clientInfo: { name: "mcp-doctor", version: "0.1.0" },
                },
            }),
            signal: AbortSignal.timeout(30000),
        });
        const mcpBody = await mcpRes.json();
        if (mcpBody?.result?.serverInfo) {
            ok(
                `unity bridge: ${bridgeBase}/mcp initialize OK ` +
                    `serverInfo=${JSON.stringify(mcpBody.result.serverInfo)} (child was lazily idle)`,
            );
        } else {
            fail(
                `unity bridge responded but MCP initialize failed: ${JSON.stringify(mcpBody).slice(0, 200)}`,
            );
        }
    }
} catch {
    fail(
        `unity bridge not reachable on host.docker.internal:${port}. On the host run: ` +
            "npm run unity:mcp:host (then unity:mcp:doctor for depth).",
    );
}

} // end SKIP_NETWORK_PROBES guard

// ---------------------------------------------------------------------------
console.log("");
console.log(
    `Doctor summary: ${failures} failure(s), ${warnings} warning(s). ` +
        (failures === 0 ? "All MCP servers connectable." : "Address the ❌ items above."),
);
if (failures > 0) {
    info(
        "Reminder: {env:VAR} references expand at AGENT STARTUP — after changing " +
            ".env.local or running mcp:sync, restart agents from a fresh shell " +
            "(interactive shells source .env.local automatically in the devcontainer).",
    );
}
// Give libuv a beat to close killed-children handles before exiting; exiting
// immediately trips a libuv assertion on Windows (win/async.c close race).
const exitCode = failures === 0 ? 0 : 1;
setTimeout(() => process.exit(exitCode), 250);