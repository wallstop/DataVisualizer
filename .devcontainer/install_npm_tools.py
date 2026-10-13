#!/usr/bin/env python3
"""Refresh npm coding tools as the container user (typed Python, issue #171).

Keep retry budgets, intact offline fallbacks, dedicated cache ownership,
trusted install scripts, and PATH/version checks in one stdlib-only owner.
The image and lifecycle launch this directly; ``uv run`` is the local entry.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple, cast

TOOLS: Tuple[Tuple[str, str], ...] = (
    ("@nanocollective/nanocoder", "nanocoder"),
    ("@opencode/cli", "opencode"),
    ("@openai/codex", "codex"),
    ("@anthropic-ai/claude-code", "claude"),
)


class ToolFailure(Exception):
    """Carry the original failure exit code and one console explanation."""
    def __init__(self, message: str, exit_code: int = 1) -> None:
        super().__init__(message)
        self.exit_code = exit_code


def run(command: Sequence[str], timeout: Optional[int] = None) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(list(command), capture_output=True, text=True, encoding="utf-8", errors="replace", check=False, timeout=timeout)
    if result.returncode < 0:
        result.returncode = 128 - result.returncode
    return result


def replay(result: subprocess.CompletedProcess[str]) -> None:
    if result.stdout:
        print(result.stdout, end="" if result.stdout.endswith("\n") else "\n")
    if result.stderr:
        print(result.stderr, file=sys.stderr, end="" if result.stderr.endswith("\n") else "\n")


def checked(command: Sequence[str]) -> str:
    result = run(command)
    if result.returncode:
        replay(result)
        raise ToolFailure(f"Command failed: {' '.join(command)}", result.returncode)
    return result.stdout.strip()


def retry(command: Sequence[str], attempts: int) -> subprocess.CompletedProcess[str]:
    for attempt in range(1, attempts + 1):
        result = run(command)
        if result.returncode == 0 or attempt == attempts:
            return result
        replay(result)
        delay = 2 ** attempt
        print(f"   npm request attempt {attempt}/{attempts} failed; retrying in {delay}s.", file=sys.stderr)
        time.sleep(delay)
    raise AssertionError("attempts must be positive")


def installed_versions() -> Dict[str, str]:
    result = run(["npm", "list", "--global", "--depth=0", "--json"])
    try:
        parsed: object = json.loads(result.stdout)
        if not isinstance(parsed, dict):
            raise ValueError("dependency tree must be an object")
        dependencies = cast(Dict[str, object], parsed).get("dependencies", {})
        if not isinstance(dependencies, dict):
            raise ValueError("dependencies must be an object")
    except (ValueError, TypeError) as error:
        raise ToolFailure("npm list did not return a valid global dependency tree.", result.returncode or 1) from error
    if result.returncode:
        print(f"⚠️  npm list exited {result.returncode}, but its valid dependency tree will be used.", file=sys.stderr)
    versions: Dict[str, str] = {}
    for name, entry in cast(Dict[str, object], dependencies).items():
        if isinstance(entry, dict):
            version = cast(Dict[str, object], entry).get("version")
            if isinstance(version, str):
                versions[name] = version
    return versions


def executable(path: Path) -> bool:
    return path.is_file() and os.access(path, os.X_OK)


def check_writable_paths(prefix: Path, cache: Path) -> None:
    modules = prefix / "lib" / "node_modules"
    paths = [prefix, prefix / "bin", modules, cache]
    # Do not follow symlinked package directories, matching find's default.
    for parent, directories, _ in os.walk(modules):
        paths.extend(Path(parent) / name for name in directories)
    for path in paths:
        try:
            path.mkdir(parents=True, exist_ok=True)
        except OSError as error:
            raise ToolFailure(f"npm path cannot be created: {path}. Rebuild the devcontainer; do not rerun npm with sudo.") from error
        if not os.access(path, os.W_OK):
            raise ToolFailure(f"npm path is not writable: {path}. Rebuild the devcontainer; do not rerun npm with sudo.")


def clean_cache(required: bool) -> None:
    result = run(["npm", "cache", "clean", "--force"])
    replay(result)
    if required and result.returncode:
        raise ToolFailure("npm cache cleanup failed.", result.returncode)


def refresh(offline_ok: bool, attempts: int) -> None:
    if os.name == "posix" and os.geteuid() == 0:
        raise ToolFailure("Refusing to install npm tools as root; run this script as the container user.")
    for command in ("node", "npm"):
        if shutil.which(command) is None:
            raise ToolFailure(f"Required command not found: {command}")
    node_version = checked(["node", "--version"])
    major = re.match(r"^v?(\d+)\.", node_version)
    if major is None or int(major.group(1)) < 22:
        raise ToolFailure(f"Node.js 22+ is required; found {node_version}.")
    # Keep the large CLI tarballs out of the ordinary npm cache. npm children
    # inherit this setting; successful updates clear only the dedicated cache.
    cache_base = os.environ.get("XDG_CACHE_HOME") or str(Path.home() / ".cache")
    os.environ["NPM_CONFIG_CACHE"] = os.environ.get("DATAVIZ_NPM_TOOL_CACHE") or str(Path(cache_base) / "dataviz-npm-tools")
    prefix = Path(checked(["npm", "prefix", "--global"]))
    cache = Path(checked(["npm", "config", "get", "cache"]))
    check_writable_paths(prefix, cache)
    print("📦 Refreshing npm coding tools from the latest dist-tag...")
    print(f"   Node.js: {node_version}; npm: {checked(['npm', '--version'])}; prefix: {prefix}")
    installed = installed_versions()
    updates: List[str] = []
    registry_unverified = False
    for package, command in TOOLS:
        version = installed.get(package, "")
        result = retry(["npm", "view", f"{package}@latest", "version"], attempts)
        if result.returncode:
            replay(result)
            if offline_ok and version and executable(prefix / "bin" / command):
                print(f"⚠️  npm registry unavailable; keeping {package}@{version}.", file=sys.stderr)
                registry_unverified = True
                continue
            raise ToolFailure(f"Unable to resolve {package}@latest from npm.")
        latest = result.stdout.strip()
        if version == latest and executable(prefix / "bin" / command):
            print(f"   Current: {package}@{version}")
        else:
            print(f"   Update:  {package}@{version or 'missing'} -> {latest}")
            updates.append(f"{package}@latest")
    changed = bool(updates)
    if updates:
        # npm 11 requires explicit trust for these two packages' install scripts.
        result = retry(["npm", "install", "--global", "--allow-scripts=@opencode/cli,@anthropic-ai/claude-code", *updates], attempts)
        replay(result)
        if result.returncode:
            if not offline_ok:
                clean_cache(required=False)
                raise ToolFailure("npm tool update failed.", result.returncode)
            fallback = installed_versions()
            for package, command in TOOLS:
                if not fallback.get(package) or not executable(prefix / "bin" / command):
                    clean_cache(required=False)
                    raise ToolFailure(f"npm update failed and the installed {package} fallback is not intact.", result.returncode)
            print("⚠️  npm update failed; continuing with the intact installed tool versions.", file=sys.stderr)
    elif registry_unverified:
        print("   No npm changes applied; latest status could not be verified.")
    else:
        print("   All npm coding tools are current.")
    for _, command in TOOLS:
        expected = prefix / "bin" / command
        resolved = shutil.which(command)
        if not executable(expected) or resolved is None or Path(resolved).resolve() != expected.resolve():
            raise ToolFailure(f"Expected {command} from {expected}, but PATH resolved {resolved or 'nothing'}.")
        try:
            result = run([resolved, "--version"], timeout=30)
        except subprocess.TimeoutExpired as error:
            raise ToolFailure(f"{command} is installed but its version check timed out after 30s.") from error
        if result.returncode:
            replay(result)
            raise ToolFailure(f"{command} is installed but failed its version check.")
        print(f"   {command + ':':<11} {(result.stdout + result.stderr).strip()}")
    if changed or os.environ.get("DATAVIZ_NPM_CLEAN_CACHE") == "1":
        clean_cache(required=True)


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Refresh npm coding tools as the non-root container user.")
    parser.add_argument("--offline-ok", action="store_true", help="keep intact installed tools when npm is unavailable")
    options = parser.parse_args(argv)
    raw_attempts = os.environ.get("DATAVIZ_NPM_INSTALL_ATTEMPTS", "5")
    if re.fullmatch(r"[1-9][0-9]*", raw_attempts) is None:
        print("❌ DATAVIZ_NPM_INSTALL_ATTEMPTS must be a positive integer.", file=sys.stderr)
        return 2
    try:
        refresh(options.offline_ok, int(raw_attempts))
        return 0
    except ToolFailure as error:
        print(f"❌ {error}", file=sys.stderr)
        return error.exit_code
    except OSError as error:
        print(f"❌ {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
