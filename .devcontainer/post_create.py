#!/usr/bin/env python3
"""Post-create script for the Data Visualizer dev container.

Typed Python port of ``post-create.sh`` (issue #171). Runs after the
container is created; handles verification, repo tooling, and hooks.

Steps: restore the .NET local tools behind the CSharpier formatting gate,
install the pre-commit hooks best-effort, and print the environment
verification banner including the Unity MCP host-bridge health probe.
"""

from __future__ import annotations

import os
import random
import shutil
import subprocess
import sys
import time
import urllib.request
from pathlib import Path
from typing import List, Sequence

sys.dont_write_bytecode = True

from _lifecycle_common import resolve_workspace


def run_with_retries(max_attempts: int, command: Sequence[str], cwd: Path) -> int:
    """Run a command with bounded retries, exponential backoff, and jitter.

    Mirrors the bash original exactly: two-second base delay, zero-to-two
    seconds of jitter, doubling up to a 30-second cap, the same retry message,
    and the last exit code when the attempts run out.
    """
    delay_seconds = 2
    attempt = 1
    while True:
        completed = subprocess.run(list(command), cwd=str(cwd), check=False)
        if completed.returncode == 0:
            return 0
        exit_code = completed.returncode
        if attempt >= max_attempts:
            return exit_code
        sleep_seconds = delay_seconds + random.randint(0, 2)
        next_attempt = attempt + 1
        print(f"   Retry {next_attempt}/{max_attempts} in {sleep_seconds}s: {' '.join(command)}")
        time.sleep(sleep_seconds)
        attempt = next_attempt
        if delay_seconds < 30:
            delay_seconds *= 2
            if delay_seconds > 30:
                delay_seconds = 30


def restore_dotnet_tools(workspace: Path) -> None:
    """Restore the .NET local tools (CSharpier formatting gate)."""
    print("🔧 Step 1/3: Restoring .NET local tools (CSharpier)...")
    if not (workspace / ".config" / "dotnet-tools.json").is_file():
        print("   Skipped (no .config/dotnet-tools.json)")
        return
    if shutil.which("dotnet") is None:
        print("   Warning: dotnet not found; CSharpier formatting gate unavailable.")
        return
    code = run_with_retries(3, ["dotnet", "tool", "restore", "--verbosity", "minimal"], workspace)
    if code != 0:
        print("   Warning: dotnet tool restore failed; run it manually before formatting.")


def install_pre_commit_hooks(workspace: Path) -> None:
    """Install the pre-commit hooks best-effort; pip fallback for missing CLIs."""
    print()
    print("🪝 Step 2/3: Installing pre-commit hooks...")
    if not (workspace / ".pre-commit-config.yaml").is_file():
        print("   Skipped (no .pre-commit-config.yaml)")
        return
    if shutil.which("pre-commit") is not None:
        if subprocess.run(["pre-commit", "install"], cwd=str(workspace), check=False).returncode != 0:
            print("   Warning: pre-commit install failed")
        return
    if shutil.which("python3") is None:
        print("   Skipped (python3 unavailable)")
        return
    print("   Installing pre-commit via pip (user scope)...")
    pip_ok = (
        subprocess.run(
            ["python3", "-m", "pip", "install", "--user", "--break-system-packages", "--quiet", "pre-commit"],
            cwd=str(workspace),
            capture_output=True,
            check=False,
        ).returncode
        == 0
    )
    pre_commit = Path.home() / ".local" / "bin" / "pre-commit"
    installed = pip_ok and subprocess.run(
        [str(pre_commit), "install"], cwd=str(workspace), capture_output=True, check=False
    ).returncode == 0
    if not installed:
        print("   Warning: could not install pre-commit; hooks not activated.")


def probe_version(command: Sequence[str]) -> str:
    """Version probe shaped like the bash ``$(cmd 2>&1 || echo NOT-FOUND)``."""
    resolved = shutil.which(command[0])
    if resolved is None:
        return "NOT-FOUND"
    completed = subprocess.run(list(command), capture_output=True, text=True, encoding="utf-8", errors="replace", check=False)
    output = ((completed.stdout or "") + (completed.stderr or "")).strip()
    if completed.returncode != 0:
        output = (output + "\n" if output else "") + "NOT-FOUND"
    return output


def probe_last_output_line(command: Sequence[str]) -> str:
    """Version probe shaped like ``$(cmd 2>&1 | tail -1)``: the last merged line.

    The pipeline's exit status is ``tail``'s, so a failing command still prints
    its own last line instead of ``NOT-FOUND``; only a missing binary reports
    ``NOT-FOUND``.
    """
    resolved = shutil.which(command[0])
    if resolved is None:
        return "NOT-FOUND"
    completed = subprocess.run(list(command), capture_output=True, text=True, encoding="utf-8", errors="replace", check=False)
    output = ((completed.stdout or "") + (completed.stderr or "")).strip()
    lines = output.splitlines() if output else []
    return lines[-1] if lines else ""


def probe_sdk_list() -> str:
    """SDK list probe shaped like ``$(dotnet --list-sdks 2>/dev/null | tr '\\n' ' ')``."""
    if shutil.which("dotnet") is None:
        return "NOT-FOUND"
    completed = subprocess.run(["dotnet", "--list-sdks"], capture_output=True, text=True, encoding="utf-8", errors="replace", check=False)
    output = ((completed.stdout or "") + (completed.stderr or "")).strip()
    if completed.returncode != 0:
        output = (output + "\n" if output else "") + "NOT-FOUND"
    return output.replace("\n", " ")


def probe_on_path(program: str) -> str:
    """Presence probe shaped like ``command -v X >/dev/null && echo ready || echo missing``."""
    return "ready" if shutil.which(program) is not None else "missing"


def banner_line(label: str, value: str) -> None:
    """Print one ``printf '   %-18s %s\\n'`` banner row."""
    print(f"   {label:<18} {value}")


def resolve_unity_mcp_port(workspace: Path) -> str:
    """Resolve the bridge port: environment first, then ``.env.local``, then 9020.

    The bridge picks a dynamic port when 9020 is busy and persists it to
    ``.env.local``; follow the same precedence the rest of the setup uses.
    """
    port = (os.environ.get("UNITY_MCP_HTTP_PORT") or "").strip()
    if not port:
        env_local = workspace / ".env.local"
        if env_local.is_file():
            for line in env_local.read_text(encoding="utf-8", errors="replace").splitlines():
                if line.startswith("UNITY_MCP_HTTP_PORT="):
                    port = line.split("=", 1)[1].replace('"', "").replace("'", "").strip()
                    break
    return port or "9020"


def unity_mcp_health(port: str) -> str:
    """Fetch the host bridge health endpoint, or an empty string when unreachable."""
    try:
        with urllib.request.urlopen(f"http://host.docker.internal:{port}/healthz", timeout=3) as response:
            return response.read().decode("utf-8", errors="replace")
    except OSError:
        return ""


def print_verification_banner(workspace: Path) -> None:
    """Print the toolchain verification banner (step 3)."""
    print()
    print("🔎 Step 3/3: Verifying toolchain...")
    print()

    print("📌 Node.js Tooling:")
    banner_line("Node.js:", probe_version(["node", "--version"]))
    banner_line("npm:", probe_version(["npm", "--version"]))
    banner_line("Nanocoder:", probe_version(["nanocoder", "--version"]))
    banner_line("OpenCode:", probe_version(["opencode", "--version"]))
    banner_line("Codex CLI:", probe_version(["codex", "--version"]))
    banner_line("Claude Code:", probe_version(["claude", "--version"]))
    banner_line("codex-zai:", probe_on_path("codex-zai"))
    banner_line("claude-zai:", probe_on_path("claude-zai"))

    print()
    print("📌 Repo Tooling:")
    banner_line("PowerShell:", probe_version(["pwsh", "--version"]))
    banner_line("dotnet SDKs:", probe_sdk_list())
    banner_line("CSharpier:", probe_last_output_line(["dotnet", "tool", "run", "csharpier", "--version"]))
    banner_line("uvx (fetch/git MCP):", probe_version(["uvx", "--version"]))

    print()
    print("📌 Unity MCP (official Unity CLI, ~140 tools):")
    port = resolve_unity_mcp_port(workspace)
    if '"ok":true' in unity_mcp_health(port):
        print(f"   Host bridge ready on host.docker.internal:{port}.")
    else:
        print("   Host bridge not reachable yet.")
        print("   On the host: npm run unity:mcp:host (and unity pipeline install once).")


def main(argv: List[str]) -> int:
    workspace = resolve_workspace(argv)
    os.chdir(workspace)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(line_buffering=True)  # type: ignore[union-attr]

    print()
    print("╔════════════════════════════════════════════════════════════════╗")
    print("║           Data Visualizer Post-Create Setup                    ║")
    print("╚════════════════════════════════════════════════════════════════╝")
    print()

    restore_dotnet_tools(workspace)
    install_pre_commit_hooks(workspace)
    print_verification_banner(workspace)

    print()
    print("╔════════════════════════════════════════════════════════════════╗")
    print("║           ✅ Setup Complete!                                    ║")
    print("╠════════════════════════════════════════════════════════════════╣")
    print("║  Quick commands:                                               ║")
    print("║    npm run mcp:sync              Reconfigure agent MCP servers ║")
    print("║    npm run unity:mcp:host        (Host) Unity MCP HTTP bridge  ║")
    print("║    npm run tools:install         Refresh coding CLIs           ║")
    print("║    codex-zai / claude-zai        Z.ai subscription backends    ║")
    print("║                                                                ║")
    print("║  Secrets live in .env.local (gitignored). IntelliSense loads   ║")
    print("║  automatically via the Roslyn LSP.                             ║")
    print("╚════════════════════════════════════════════════════════════════╝")
    print()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
