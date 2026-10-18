#!/usr/bin/env node
// Keep older frontend and lifecycle launchers working during the Python migration.
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
const script = fileURLToPath(new URL("../../scripts/mcp_doctor.py", import.meta.url));
const result = spawnSync("uv", ["run", "--no-project", script, ...process.argv.slice(2)], { stdio: "inherit" });
if (result.error) console.error("Cannot start the MCP doctor through uv; verify uv is on PATH.");
process.exit(result.status ?? 1);
