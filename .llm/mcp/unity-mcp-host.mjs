#!/usr/bin/env node
// Preserve existing Node entry points; bridge is owned by typed Python.
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
const script = fileURLToPath(new URL("../../scripts/unity_mcp_host.py", import.meta.url));
for (const [command, prefix, probe] of [
    ["uv", ["run", "--no-project"], ["--version"]],
    ["python3", [], ["-c", "import sys; assert sys.version_info >= (3, 9); print('dataviz-python')"]],
    ["python", [], ["-c", "import sys; assert sys.version_info >= (3, 9); print('dataviz-python')"]],
]) {
    const available = spawnSync(command, probe, { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] });
    if (available.status !== 0) continue;
    const ready = command === "uv" ? available.stdout.startsWith("uv ") : available.stdout.trim() === "dataviz-python";
    if (!ready) continue;
    const result = spawnSync(command, [...prefix, script, ...process.argv.slice(2)], { stdio: "inherit" });
    if (result.error) console.error("Cannot start MCP bridge: " + result.error.message);
    process.exit(result.status ?? 1);
}
console.error("Python 3.9+ or uv is required for MCP bridge.");
process.exit(1);
