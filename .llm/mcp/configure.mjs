#!/usr/bin/env node
// Preserve existing Node entry points; configuration is owned by typed Python.
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
const script = fileURLToPath(new URL("../../scripts/mcp_configure.py", import.meta.url));
for (const [command, prefix, probe] of [
    ["uv", ["run", "--no-project"], ["--version"]],
    ["python3", [], ["-c", "import sys; assert sys.version_info >= (3, 9)"]],
    ["python", [], ["-c", "import sys; assert sys.version_info >= (3, 9)"]],
]) {
    const available = spawnSync(command, probe, { stdio: "ignore" });
    if (available.status !== 0) continue;
    const result = spawnSync(command, [...prefix, script, ...process.argv.slice(2)], { stdio: "inherit" });
    if (result.error) console.error("Cannot start MCP configuration: " + result.error.message);
    process.exit(result.status ?? 1);
}
console.error("Python 3.9+ or uv is required for MCP configuration.");
process.exit(1);
