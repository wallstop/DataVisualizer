#!/usr/bin/env python3
"""mcp-sync contract tests (typed Python port of ``test-mcp-sync.sh``, issue #171).

Every case from the bash original is kept with the same assertions.
The Python sync entry drives the unchanged Node configurator
(``sync_mcp.py`` driving ``configure.mjs``), so each case still syncs into
a throwaway workspace with throwaway HOME and CODEX_HOME, and asserts
that every supported agent frontend receives the same server fleet with
placeholder-only credentials (no secrets on disk). The bash suite's ``jq``
probes become stdlib ``json`` reads, which also removes the jq dependency
the old suite silently required.

Integration cases require node; the wrapper itself requires only Python.
"""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any, List, Tuple

sys.dont_write_bytecode = True

from _test_helpers import assert_true, failure_count, new_temp_root, remove_temp_root, run_case

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
SYNC = REPO_ROOT / ".llm" / "mcp" / "sync_mcp.py"

# The unity bridge URL port is configurable: unity-mcp-host.mjs binds a dynamic
# port when 9020 is busy and persists UNITY_MCP_HTTP_PORT to .env.local. Export
# it so the sandboxed configure.mjs run and these assertions stay in lockstep.
UNITY_MCP_HTTP_PORT = os.environ.get("UNITY_MCP_HTTP_PORT") or "9020"

SCRUBBED_KEYS = ("GITHUB_PERSONAL_ACCESS_TOKEN", "GITHUB_MCP_PAT", "GITHUB_TOKEN", "ZAI_API_KEY", "Z_AI_API_KEY")


LEGACY_OPENCODE_JSON = """{
  "$schema": "https://opencode.ai/config.json",
  "theme": "system",
  "mcp": {
    "legacy-local": { "type": "local", "command": ["x"], "enabled": true },
    "unity": { "type": "remote", "url": "http://stale:1/mcp", "enabled": false, "timeout": 1000 },
    "timeout": { "startup": 45000 },
    "servers": {
      "my-tools": { "type": "remote", "url": "https://example.com/mcp" }
    }
  }
}
"""


class Sandbox:
    """Hermetic fixture workspace for one sync case."""

    def __init__(self) -> None:
        self.root = new_temp_root(prefix="mcp-sync-test-")
        self.home = self.root / "home"
        self.codex_home = self.root / "codex-home"
        self.home.mkdir()
        self.codex_home.mkdir()
        self.last_exit = -1
        self.stdout = ""
        self.stderr = ""

    def run_sync(self) -> None:
        env = dict(os.environ)
        for key in SCRUBBED_KEYS:
            env.pop(key, None)
        env.update(
            {
                "DATAVIZ_MCP_WORKSPACE": str(self.root),
                "DATAVIZ_IN_CONTAINER": "1",
                "CODEX_HOME": str(self.codex_home),
                "HOME": str(self.home),
                "UNITY_MCP_HTTP_PORT": UNITY_MCP_HTTP_PORT,
            }
        )
        if os.name == "nt":
            # Windows tools resolve the home through USERPROFILE; keep both
            # names pointing inside the sandbox.
            env["USERPROFILE"] = str(self.home)
        completed = subprocess.run(
            [sys.executable, str(SYNC), str(self.root)],
            cwd=str(self.root),
            env=env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        self.last_exit = completed.returncode
        self.stdout = completed.stdout or ""
        self.stderr = completed.stderr or ""

    def run_failure_context(self) -> str:
        """Carry the subject's own output in failure messages.

        The bash original tee'd sandbox output into files beside the case; a
        failing sync with no captured output is undebuggable from CI, so every
        assertion about a run can attach what the subject printed.
        """
        return f"(exit {self.last_exit}; stdout: {self.stdout!r}; stderr: {self.stderr!r})"

    def path(self, relative: str) -> Path:
        return self.root / relative

    def load_json(self, relative: str) -> Any:
        return json.loads(self.path(relative).read_text(encoding="utf-8"))

    def destroy(self) -> None:
        remove_temp_root(self.root)


def assert_eq(description: str, expected: Any, actual: Any, suffix: str = "") -> None:
    assert_true(expected == actual, f"{description} (expected: [{expected}]; actual: [{actual}]){suffix}")


def assert_file_contains(sandbox: Sandbox, relative: str, needle: str, description: str) -> None:
    path = sandbox.path(relative)
    assert_true(path.is_file(), f"{description} (missing file {path})")
    content = path.read_text(encoding="utf-8", errors="replace")
    assert_true(needle in content, f"{description} (missing [{needle}] in {relative})")


def expected_fleet() -> str:
    # The managed fleet; fetch/git only register when uvx exists (mirrors the
    # configurator), so the expected set is derived from the same condition.
    fleet = ["context7", "fetch", "git", "github", "unity", "zai-image", "zai-vision", "zai-web-search"]
    if shutil.which("uvx") is None:
        fleet = [name for name in fleet if name not in ("fetch", "git")]
    return " ".join(fleet)


def codex_server_names(sandbox: Sandbox) -> str:
    config = sandbox.path("codex-home/config.toml").read_text(encoding="utf-8", errors="replace")
    names = re.findall(r"^\[mcp_servers\.([a-z0-9-]+)\]", config, flags=re.MULTILINE)
    return " ".join(sorted(names))


def json_server_names(sandbox: Sandbox, relative: str, keys: List[str]) -> str:
    document = sandbox.load_json(relative)
    for key in keys:
        document = document[key]
    return " ".join(sorted(document))


def case_sync_generates_all_frontends() -> None:
    sandbox = Sandbox()
    sandbox.run_sync()
    assert_eq("sync exits zero", 0, sandbox.last_exit, sandbox.run_failure_context())
    for relative in (".mcp.json", "opencode.json", ".vscode/mcp.json", ".nanocoder/mcp.json", ".cursor/mcp.json"):
        assert_true(sandbox.path(relative).is_file(), f"sync generated {relative}")
    assert_true(sandbox.path("codex-home/config.toml").is_file(), "sync generated codex config.toml")
    sandbox.destroy()


def case_server_fleet_is_identical_across_frontends() -> None:
    sandbox = Sandbox()
    sandbox.run_sync()
    expected = expected_fleet()
    assert_eq("claude .mcp.json fleet matches catalog", expected, json_server_names(sandbox, ".mcp.json", ["mcpServers"]))
    assert_eq("opencode fleet matches catalog", expected, json_server_names(sandbox, "opencode.json", ["mcp", "servers"]))
    assert_eq(
        "nanocoder fleet matches catalog", expected, json_server_names(sandbox, ".nanocoder/mcp.json", ["mcpServers"])
    )
    assert_eq("vscode fleet matches catalog", expected, json_server_names(sandbox, ".vscode/mcp.json", ["servers"]))
    assert_eq("codex fleet matches catalog", expected, codex_server_names(sandbox))
    # Cursor runs on the host and only carries host-executable entries, so
    # zai-image (container-local script path) is intentionally absent.
    cursor_expected = " ".join(name for name in expected.split(" ") if name != "zai-image")
    assert_eq("cursor fleet matches host-executable catalog", cursor_expected, json_server_names(sandbox, ".cursor/mcp.json", ["mcpServers"]))
    sandbox.destroy()


def case_opencode_v2_shape_and_legacy_migration() -> None:
    # Sync must emit the native OpenCode V2 shape (servers under mcp.servers,
    # `disabled` instead of `enabled`, per-server timeout split into
    # catalog/execution), strip legacy V1 flat entries on re-sync, and
    # preserve unrelated user settings and a valid V2 mcp.timeout block.
    sandbox = Sandbox()
    sandbox.path("opencode.json").write_text(LEGACY_OPENCODE_JSON, encoding="utf-8", newline="")
    sandbox.run_sync()
    assert_eq("sync migrates legacy v1 opencode.json without error", 0, sandbox.last_exit, sandbox.run_failure_context())
    document = sandbox.load_json("opencode.json")
    mcp = document["mcp"]
    assert_eq(
        "no mixed v1/v2 map remains",
        [],
        [key for key in mcp if key not in ("servers", "timeout")],
    )
    servers = mcp["servers"]
    assert_eq("user-added v1 server is converted and preserved", "x", servers["legacy-local"]["command"][0])
    assert_eq("converted v1 server carries the v2 disabled field", False, servers["legacy-local"]["disabled"])
    assert_eq("user-added v2 server survives sync", "https://example.com/mcp", servers["my-tools"]["url"])
    assert_eq(
        "stale legacy entry replaced by the catalog definition",
        f"http://host.docker.internal:{UNITY_MCP_HTTP_PORT}/mcp",
        servers["unity"]["url"],
    )
    assert_eq(
        "no v2 entry keeps the v1 enabled field",
        False,
        any("enabled" in server for server in servers.values()),
    )
    assert_eq("managed v2 entries carry the disabled field", True, "disabled" in servers["unity"])
    assert_eq("v1 timeout splits into catalog/execution", [300000, 300000], [servers["unity"]["timeout"]["catalog"], servers["unity"]["timeout"]["execution"]])
    assert_eq("valid v2 mcp.timeout block survives sync", 45000, mcp["timeout"]["startup"])
    assert_eq("unrelated settings survive migration", "system", document["theme"])
    first_pass = sandbox.path("opencode.json").read_bytes()
    sandbox.run_sync()
    assert_eq("re-sync exits zero", 0, sandbox.last_exit, sandbox.run_failure_context())
    assert_eq("re-sync is idempotent", first_pass, sandbox.path("opencode.json").read_bytes())
    sandbox.destroy()


def case_claude_project_servers_are_preapproved() -> None:
    # Sync must record approval state the way the interactive dialogs do so
    # project-scope servers are usable without a first-run prompt.
    sandbox = Sandbox()
    sandbox.run_sync()
    assert_file_contains(
        sandbox,
        ".claude/settings.local.json",
        '"enableAllProjectMcpServers": true',
        "claude settings auto-approve project servers",
    )
    assert_file_contains(
        sandbox,
        "home/.claude.json",
        '"hasTrustDialogAccepted": true',
        "claude home state records folder trust",
    )
    sandbox.destroy()


def case_generated_configs_contain_no_literal_secrets() -> None:
    # Config files must carry env-var REFERENCES only; real credentials live in
    # .env.local. A literal key in any generated file fails the suite.
    sandbox = Sandbox()
    sandbox.run_sync()
    secret_pattern = re.compile(r"sk-(or-)?[A-Za-z0-9_-]{16,}|ghp_[A-Za-z0-9]{20,}|Bearer [A-Za-z0-9._-]{16,}")
    generated = (
        ".mcp.json",
        "opencode.json",
        ".vscode/mcp.json",
        ".nanocoder/mcp.json",
        ".cursor/mcp.json",
        "codex-home/config.toml",
    )
    hits = [relative for relative in generated if secret_pattern.search(sandbox.path(relative).read_text(encoding="utf-8", errors="replace"))]
    assert_eq("no literal bearer/secret tokens in generated configs", [], hits)
    assert_file_contains(sandbox, ".mcp.json", "Bearer ${GITHUB_PERSONAL_ACCESS_TOKEN", "claude github auth uses env placeholder")
    assert_file_contains(
        sandbox,
        ".mcp.json",
        f'"url": "http://host.docker.internal:{UNITY_MCP_HTTP_PORT}/mcp"',
        "claude unity uses the official CLI HTTP bridge",
    )
    assert_file_contains(sandbox, ".mcp.json", '"X-MCP-Toolsets": "all"', "claude github enables all toolsets")
    assert_file_contains(sandbox, "codex-home/config.toml", '"X-MCP-Toolsets" = "all"', "codex github enables all toolsets")
    assert_file_contains(sandbox, ".cursor/mcp.json", '"command": "unity"', "cursor unity launches the official CLI (host stdio)")
    assert_file_contains(sandbox, "opencode.json", "Bearer {env:ZAI_API_KEY}", "opencode zai auth uses env placeholder")
    assert_file_contains(
        sandbox,
        "codex-home/config.toml",
        'bearer_token_env_var = "GITHUB_PERSONAL_ACCESS_TOKEN"',
        "codex github auth uses env var name",
    )
    assert_file_contains(
        sandbox,
        "codex-home/config.toml",
        'env_vars = ["ZAI_API_KEY", "Z_AI_MODE"]',
        "codex zai-vision passes Z_AI_MODE through (every other frontend sets it)",
    )
    sandbox.destroy()


CASES = (
    case_sync_generates_all_frontends,
    case_server_fleet_is_identical_across_frontends,
    case_opencode_v2_shape_and_legacy_migration,
    case_claude_project_servers_are_preapproved,
    case_generated_configs_contain_no_literal_secrets,
)


def case_wrapper_forwards_workspace_flags_and_exit_status() -> None:
    root = new_temp_root(prefix="mcp sync wrapper-")
    try:
        script_dir = root / ".llm" / "mcp"
        script_dir.mkdir(parents=True)
        wrapper = script_dir / SYNC.name
        shutil.copyfile(SYNC, wrapper)
        (script_dir / "configure.mjs").write_text(
            "console.log(JSON.stringify({args:process.argv.slice(2),cwd:process.cwd(),"
            "workspace:process.env.DATAVIZ_MCP_WORKSPACE}));process.exit(7);\n",
            encoding="utf-8",
        )
        other = root / "other workspace"
        other.mkdir()
        cases: List[Tuple[Path, List[str], Path, Path, List[str]]] = [
            (wrapper, [], other, root, []),
            (wrapper, ["--check"], other, root, ["--check"]),
            (wrapper, ["other workspace", "--check", "two words"], root, other, ["--check", "two words"]),
            (wrapper, [""], other, root, [""]),
        ]
        if os.name == "posix":
            alias = root / "workspace alias"
            alias.symlink_to(root, target_is_directory=True)
            cases.extend([
                (wrapper, [str(alias), "--check"], other, alias, ["--check"]),
                (alias / ".llm" / "mcp" / SYNC.name, ["--check"], other, alias, ["--check"]),
            ])
        else:
            print("    SKIP: workspace alias cases require a POSIX symlink fixture.")
        for entry, arguments, working_directory, expected_workspace, flags in cases:
            completed = subprocess.run(
                [sys.executable, str(entry), *arguments],
                cwd=working_directory,
                env={**os.environ, "DATAVIZ_MCP_WORKSPACE": "ignored inherited workspace"},
                capture_output=True,
                text=True,
                encoding="utf-8",
                check=False,
            )
            assert_eq(f"wrapper exit for {arguments}", 7, completed.returncode, completed.stderr)
            record = json.loads(completed.stdout)
            assert_eq("configurator flags", flags, record["args"])
            assert_eq("configurator cwd", expected_workspace.resolve(), Path(record["cwd"]).resolve())
            assert_eq("workspace environment preserves logical path", os.path.abspath(expected_workspace), record["workspace"])
        failed = subprocess.run(
            [sys.executable, str(wrapper), str(root / "missing workspace")],
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=False,
        )
        assert_eq("missing workspace exits one", 1, failed.returncode)
        assert_true("Cannot sync MCP configuration:" in failed.stderr, failed.stderr)
        assert_true("Traceback" not in failed.stderr, failed.stderr)
    finally:
        remove_temp_root(root)


def case_wrapper_fails_without_node() -> None:
    completed = subprocess.run(
        [sys.executable, str(SYNC), "--check"],
        env={**os.environ, "PATH": ""},
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
    )
    assert_eq("missing node exits one", 1, completed.returncode)
    assert_true("node is required to sync MCP configuration." in completed.stderr, completed.stderr)


def main() -> int:
    assert_true(SYNC.is_file(), f"sync script not found at {SYNC}")
    print("== mcp-sync contract tests ==")
    run_case("case_wrapper_fails_without_node", case_wrapper_fails_without_node)
    missing = shutil.which("node") is None
    if missing:
        print("    SKIP: node is required to drive the configurator.")
        return 1 if failure_count() else 0
    run_case("case_wrapper_forwards_workspace_flags_and_exit_status", case_wrapper_forwards_workspace_flags_and_exit_status)
    for case in CASES:
        run_case(case.__name__, case)
    failures = failure_count()
    print(f"== mcp-sync: {failures} failure(s) ==")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
