#!/usr/bin/env python3
"""Install the current npm latest dist-tag for the coding CLIs as the container user.

Typed Python port of ``install-npm-tools.sh`` (issue #171): the verdicts,
messages, environment knobs, retry schedule, and exit codes are kept, and
only the driver language changed. The script never runs npm as root and
never needs sudo: the image makes the global prefix group-writable through
the stable nvm group, and this script refuses to proceed (with remediation
guidance) if any required path is not writable.

The lifecycle entry points invoke it with the interpreter the image
guarantees (``python3``), so container boot never depends on a uv
environment sync succeeding.
"""

from __future__ import annotations

import getpass
import json
import os
import re
import shutil
import subprocess
import sys
import time
from typing import List, Optional, Sequence, Tuple

NPM_TOOL_PACKAGES: Tuple[str, ...] = (
    "@nanocollective/nanocoder@latest",
    "@opencode/cli@latest",
    "@openai/codex@latest",
    "@anthropic-ai/claude-code@latest",
)
NPM_TOOL_COMMANDS: Tuple[str, ...] = (
    "nanocoder",
    "opencode",
    "codex",
    "claude",
)


def parse_offline_ok(argv: Sequence[str]) -> bool:
    """Accept no argument or ``--offline-ok``; anything else is a usage error."""
    if len(argv) > 1 and argv[1] != "--offline-ok":
        print(f"Usage: {argv[0]} [--offline-ok]", file=sys.stderr)
        raise SystemExit(2)
    return len(argv) > 1 and argv[1] == "--offline-ok"


def resolve_install_attempts() -> int:
    """Read ``DATAVIZ_NPM_INSTALL_ATTEMPTS`` as a positive integer, defaulting to 5."""
    raw = os.environ.get("DATAVIZ_NPM_INSTALL_ATTEMPTS") or "5"
    if re.fullmatch(r"[1-9][0-9]*", raw) is None:
        print("❌ DATAVIZ_NPM_INSTALL_ATTEMPTS must be a positive integer.", file=sys.stderr)
        raise SystemExit(2)
    return int(raw)


def run_with_retries(max_attempts: int, command: Sequence[str], capture: bool = False) -> Tuple[int, str]:
    """Run a command with the bash original's doubling backoff schedule.

    With ``capture`` the successful attempt's stdout is returned the way the
    bash command substitutions used it (``npm view``); without it the child
    streams live (``npm install``). Every failed attempt still costs one
    recorded retry, and the last exit code travels back to the caller.
    """
    attempt = 1
    delay_seconds = 2
    while True:
        if capture:
            completed = subprocess.run(
                list(command),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                check=False,
            )
            sys.stderr.write(completed.stderr or "")
            if completed.returncode == 0:
                return 0, (completed.stdout or "").strip()
        else:
            completed = subprocess.run(list(command), check=False)
            if completed.returncode == 0:
                return 0, ""
        exit_code = completed.returncode
        if attempt >= max_attempts:
            return exit_code, ""
        print(f"   npm request attempt {attempt}/{max_attempts} failed; retrying in {delay_seconds}s.", file=sys.stderr)
        time.sleep(delay_seconds)
        attempt += 1
        delay_seconds = delay_seconds * 2


def read_installed_tree() -> str:
    """Return the global dependency tree JSON, refusing an unparseable one.

    ``npm list`` exits nonzero when the tree holds problems but still prints
    valid JSON; the tree is kept with a warning, mirroring the bash
    original's node-based validation.
    """
    completed = subprocess.run(
        ["npm", "list", "--global", "--depth=0", "--json"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    tree = completed.stdout or ""
    sys.stderr.write(completed.stderr or "")
    try:
        json.loads(tree)
    except json.JSONDecodeError:
        print("❌ npm list did not return a valid global dependency tree.", file=sys.stderr)
        raise SystemExit(completed.returncode if completed.returncode != 0 else 1)
    if completed.returncode != 0:
        print(f"⚠️  npm list exited {completed.returncode}, but its valid dependency tree will be used.", file=sys.stderr)
    return tree


def installed_version_of(tree: str, package_name: str) -> str:
    """Read one package's installed version from the tree, blank when absent."""
    try:
        dependencies = json.loads(tree).get("dependencies") or {}
        entry = dependencies.get(package_name) or {}
        version = entry.get("version") if isinstance(entry, dict) else None
    except (json.JSONDecodeError, AttributeError):
        return ""
    return version if isinstance(version, str) else ""


def resolve_tool_cache() -> None:
    """Point npm at the dedicated tool cache kept out of the user's normal cache.

    Metadata can be reused between no-op startup checks; a successful update
    clears this cache at the end.
    """
    custom = os.environ.get("DATAVIZ_NPM_TOOL_CACHE")
    if custom:
        cache = custom
    else:
        base = os.environ.get("XDG_CACHE_HOME") or os.path.join(os.path.expanduser("~"), ".cache")
        cache = os.path.join(base, "dataviz-npm-tools")
    os.environ["NPM_CONFIG_CACHE"] = cache


def writable_paths_for(npm_prefix: str, npm_cache: str) -> List[str]:
    """List every path that must be writable: the prefix tree plus the cache."""
    paths = [npm_prefix, os.path.join(npm_prefix, "bin"), os.path.join(npm_prefix, "lib", "node_modules"), npm_cache]
    tree_root = os.path.join(npm_prefix, "lib", "node_modules")
    if os.path.isdir(tree_root):
        for directory, subdirectories, _files in os.walk(tree_root):
            paths.append(directory)
            paths.extend(os.path.join(directory, subdirectory) for subdirectory in subdirectories)
    return paths


def enforce_writable_paths(paths: Sequence[str]) -> None:
    """Refuse to proceed unless the current user can write every npm path."""
    user = getpass_user()
    for path in paths:
        if not os.path.isdir(path):
            try:
                os.makedirs(path, exist_ok=True)
            except OSError:
                print(f"❌ npm path cannot be created by {user}: {path}", file=sys.stderr)
                print("   Rebuild the devcontainer; do not rerun npm with sudo.", file=sys.stderr)
                raise SystemExit(1)
        if not os.access(path, os.W_OK):
            print(f"❌ npm path is not writable by {user}: {path}", file=sys.stderr)
            print("   Rebuild the devcontainer; do not rerun npm with sudo.", file=sys.stderr)
            raise SystemExit(1)


def getpass_user() -> str:
    """Return the current user name the way ``id -un`` did."""
    return getpass.getuser()


def clean_tool_cache_best_effort() -> None:
    """Run ``npm cache clean --force`` ignoring its outcome (bash ``|| true``)."""
    subprocess.run(["npm", "cache", "clean", "--force"], check=False)


def canonical_target(path: str) -> str:
    """Return the canonical path the bash ``readlink -f`` produced, blank for nothing."""
    if not path:
        return ""
    try:
        return os.path.realpath(path)
    except OSError:
        return ""


def verify_launchers(npm_prefix: str) -> List[str]:
    """Verify every CLI resolves from the managed prefix and collect its version.

    PATH must land on the prefix's own launcher for each command, and each
    launcher must answer ``--version`` within 30 seconds, so a half-installed
    tool fails here instead of failing an agent session later.
    """
    tool_versions: List[str] = []
    for command_name in NPM_TOOL_COMMANDS:
        expected_command = os.path.join(npm_prefix, "bin", command_name)
        resolved_command = shutil.which(command_name) or ""
        expected_target = canonical_target(expected_command)
        resolved_target = canonical_target(resolved_command)
        if not os.access(expected_command, os.X_OK) or not expected_target or resolved_target != expected_target:
            print(
                f"❌ Expected {command_name} from {expected_command}, "
                f"but PATH resolved {resolved_command or 'nothing'}.",
                file=sys.stderr,
            )
            raise SystemExit(1)
        try:
            completed = subprocess.run(
                [resolved_command, "--version"],
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=30,
                check=False,
            )
        except subprocess.TimeoutExpired:
            print(f"❌ {command_name} is installed but failed its version check:", file=sys.stderr)
            raise SystemExit(1)
        if completed.returncode != 0:
            print(f"❌ {command_name} is installed but failed its version check:", file=sys.stderr)
            print(((completed.stdout or "") + (completed.stderr or "")).rstrip(), file=sys.stderr)
            raise SystemExit(1)
        tool_versions.append(((completed.stdout or "") + (completed.stderr or "")).rstrip("\n"))
    return tool_versions


def main(argv: Optional[Sequence[str]] = None) -> int:
    """Refresh the npm coding tools, then verify every launcher resolves."""
    arguments = list(sys.argv[1:] if argv is None else argv)
    offline_ok = parse_offline_ok([sys.argv[0], *arguments])
    install_attempts = resolve_install_attempts()

    geteuid = getattr(os, "geteuid", None)
    if geteuid is not None and geteuid() == 0:
        print("❌ Refusing to install npm tools as root; run this script as the container user.", file=sys.stderr)
        return 1

    for command_name in ("node", "npm"):
        if shutil.which(command_name) is None:
            print(f"❌ Required command not found: {command_name}", file=sys.stderr)
            return 1

    node_version = subprocess.run(
        ["node", "--version"], capture_output=True, text=True, encoding="utf-8", errors="replace", check=False
    )
    node_version_text = (node_version.stdout or "").strip()
    major_text = node_version_text[1:] if node_version_text.startswith("v") else node_version_text
    try:
        node_major = int(major_text.split(".")[0])
    except ValueError:
        node_major = 0
    if node_version.returncode != 0 or node_major < 22:
        print(f"Node.js 22+ is required; found {node_version_text or 'nothing'}.", file=sys.stderr)
        return 1

    resolve_tool_cache()
    prefix_result = subprocess.run(
        ["npm", "prefix", "--global"], capture_output=True, text=True, encoding="utf-8", errors="replace", check=False
    )
    cache_result = subprocess.run(
        ["npm", "config", "get", "cache"], capture_output=True, text=True, encoding="utf-8", errors="replace", check=False
    )
    # The bash original ran under ``set -e``: a failed npm query aborted the
    # script instead of continuing with an empty prefix.
    if prefix_result.returncode != 0:
        return prefix_result.returncode
    if cache_result.returncode != 0:
        return cache_result.returncode
    npm_prefix = (prefix_result.stdout or "").strip()
    npm_cache = (cache_result.stdout or "").strip()
    enforce_writable_paths(writable_paths_for(npm_prefix, npm_cache))

    print("📦 Refreshing npm coding tools from the latest dist-tag...")
    npm_version = subprocess.run(
        ["npm", "--version"], capture_output=True, text=True, encoding="utf-8", errors="replace", check=False
    ).stdout.strip()
    print(f"   Node.js: {node_version_text}; npm: {npm_version}; prefix: {npm_prefix}")

    installed_tree = read_installed_tree()
    packages_to_install: List[str] = []
    registry_unverified = False
    clean_cache = False
    for package_spec, command_name in zip(NPM_TOOL_PACKAGES, NPM_TOOL_COMMANDS):
        package_name = package_spec.rsplit("@", 1)[0]
        installed_version = installed_version_of(installed_tree, package_name)
        expected_command = os.path.join(npm_prefix, "bin", command_name)
        latest_exit, latest_output = run_with_retries(install_attempts, ["npm", "view", package_spec, "version"], capture=True)
        if latest_exit != 0:
            if offline_ok and installed_version and os.access(expected_command, os.X_OK):
                print(f"⚠️  npm registry unavailable; keeping {package_name}@{installed_version}.", file=sys.stderr)
                registry_unverified = True
                continue
            print(f"❌ Unable to resolve {package_spec} from npm.", file=sys.stderr)
            return 1
        latest_version = latest_output
        if installed_version == latest_version and os.access(expected_command, os.X_OK):
            print(f"   Current: {package_name}@{installed_version}")
        else:
            print(f"   Update:  {package_name}@{installed_version or 'missing'} -> {latest_version}")
            packages_to_install.append(package_spec)

    # npm 11 blocks dependency lifecycle scripts unless explicitly trusted. OpenCode
    # and Claude Code use install scripts and are the only trusted global packages.
    install_exit_code = 0
    if packages_to_install:
        install_exit, _ = run_with_retries(
            install_attempts,
            [
                "npm",
                "install",
                "--global",
                "--allow-scripts=@opencode/cli,@anthropic-ai/claude-code",
                *packages_to_install,
            ],
        )
        install_exit_code = install_exit
        clean_cache = True
        if install_exit != 0:
            if not offline_ok:
                clean_tool_cache_best_effort()
                return install_exit
            fallback_tree = read_installed_tree()
            for package_spec, command_name in zip(NPM_TOOL_PACKAGES, NPM_TOOL_COMMANDS):
                package_name = package_spec.rsplit("@", 1)[0]
                fallback_version = installed_version_of(fallback_tree, package_name)
                expected_command = os.path.join(npm_prefix, "bin", command_name)
                if not fallback_version or not os.access(expected_command, os.X_OK):
                    print(f"❌ npm update failed and the installed {package_name} fallback is not intact.", file=sys.stderr)
                    clean_tool_cache_best_effort()
                    return install_exit
            print("⚠️  npm update failed; continuing with the intact installed tool versions.", file=sys.stderr)
    elif registry_unverified:
        print("   No npm changes applied; latest status could not be verified.")
    else:
        print("   All npm coding tools are current.")

    tool_versions = verify_launchers(npm_prefix)
    for label, version in zip(("nanocoder:", "opencode:", "codex:", "claude:"), tool_versions):
        print(f"   {label:<11} {version}")

    if clean_cache or os.environ.get("DATAVIZ_NPM_CLEAN_CACHE") == "1":
        completed = subprocess.run(["npm", "cache", "clean", "--force"], check=False)
        if completed.returncode != 0:
            return completed.returncode
    return 0


if __name__ == "__main__":
    sys.exit(main())
