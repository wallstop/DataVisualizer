#!/usr/bin/env python3
"""devcontainer lifecycle contract tests for the typed Python port (issue #171).

The four lifecycle entry points (``on_create.py``, ``post_create.py``,
``post_start.py``, ``update_content.py``) boot the container, so their
contracts run here in sandbox workspaces and homes with stub children on
PATH: no real npm install, backend install, MCP sync, or pip install is
touched, and the real ``.env.local`` is never read. The suite skips on
non-POSIX hosts, where the bash children and permission bits cannot exist.
"""

from __future__ import annotations

import os
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path
from typing import Dict, List, Sequence, Tuple

sys.dont_write_bytecode = True

from _test_helpers import assert_exit_code, assert_true, failure_count, run_cases

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
LIFECYCLE_DIR = REPO_ROOT / ".devcontainer"

BEGIN = "# >>> DataVisualizer devcontainer env >>>"
END = "# <<< DataVisualizer devcontainer env <<<"

STUB_RECORD = (
    "#!/usr/bin/env bash\n"
    '{ printf "STUB %s|" "$(basename "$0")"; printf " %s" "$@"; '
    'printf " |ENV:%s\\n" "$(env | grep -E \'^(GITHUB_PERSONAL_ACCESS_TOKEN)=\' | tr \'\\n\' \';\')"; } '
    '>> "$MARKER"\n'
    "exit 0\n"
)
FAILING_STUB = STUB_RECORD.replace("exit 0\n", "exit 1\n")

DOTNET_STUB = (
    "#!/usr/bin/env bash\n"
    'if [ "$1" = "tool" ] && [ "$2" = "restore" ]; then\n'
    '  count="$(cat "$DOTNET_STUB_COUNT" 2>/dev/null || echo 0)"; count=$((count+1)); echo "$count" > "$DOTNET_STUB_COUNT"\n'
    '  printf "dotnet tool restore attempt %s\\n" "$count" >> "$MARKER"\n'
    '  [ "$count" -ge 2 ] && exit 0\n'
    "  exit 1\n"
    "fi\n"
    "exit 0\n"
)

PYTHON3_FAILS_STUB = "#!/usr/bin/env bash\nexit 1\n"


def posix_available() -> bool:
    """The lifecycle contracts need bash children and POSIX permission bits."""
    return os.name == "posix" and shutil.which("bash") is not None


def stat_mode(path: Path) -> int:
    return stat.S_IMODE(os.stat(path).st_mode)


class Sandbox:
    """One isolated workspace plus home, with stub children and a marker log."""

    def __init__(self) -> None:
        self.root = Path(tempfile.gettempdir()) / f"lifecycle-{uuid.uuid4().hex}"
        self.ws = self.root / "ws"
        self.home = self.root / "home"
        self.stubs = self.root / "stubs"
        for directory in (self.ws / ".devcontainer", self.ws / ".llm/mcp", self.home, self.stubs):
            directory.mkdir(parents=True)
        self.marker = self.root / "marker.log"
        shutil.copy(LIFECYCLE_DIR / "env-local.sh", self.ws / ".devcontainer" / "env-local.sh")
        for relative in (".devcontainer/install_npm_tools.py", ".devcontainer/ai_backends.py", ".llm/mcp/sync_mcp.py"):
            self.write_stub(self.ws / relative, STUB_RECORD)
        self.write_stub(self.stubs / "node", STUB_RECORD)
        # Credential-shaped variables are scrubbed from the child environment:
        # a devcontainer session forwards real secrets, and the stubs record
        # what they saw, so only sandbox-seeded values may ever be recorded.
        credential_keys = (
            "GITHUB_PERSONAL_ACCESS_TOKEN",
            "GITHUB_MCP_PAT",
            "GITHUB_TOKEN",
            "ZAI_API_KEY",
            "Z_AI_API_KEY",
            "OPENROUTER_API_KEY",
            "OPEN_ROUTER_API_KEY",
            "CONTEXT7_API_KEY",
            "UNITY_MCP_TOKEN",
            "ANTHROPIC_AUTH_TOKEN",
            "ANTHROPIC_API_KEY",
            "DATAVIZ_ENV_LOCAL_PRECEDENCE",
        )
        self.env: Dict[str, str] = {key: value for key, value in os.environ.items() if key not in credential_keys}
        self.env["HOME"] = str(self.home)
        self.env["PATH"] = f"{self.stubs}:/usr/bin:/bin"
        self.env["MARKER"] = str(self.marker)
        self.env["UNITY_MCP_HTTP_PORT"] = "1"

    def write_stub(self, path: Path, content: str) -> Path:
        if path.suffix == ".py":
            exit_code = 1 if content == FAILING_STUB else 0
            content = (
                "import os, sys\nfrom pathlib import Path\n"
                "value = os.environ.get('GITHUB_PERSONAL_ACCESS_TOKEN')\n"
                "env = 'GITHUB_PERSONAL_ACCESS_TOKEN=' + value + ';' if value is not None else ''\n"
                "with Path(os.environ['MARKER']).open('a') as marker:\n"
                "    marker.write('STUB ' + Path(__file__).name + '|' + ''.join(' ' + arg for arg in sys.argv[1:]) + ' |ENV:' + env + '\\n')\n"
                f"sys.exit({exit_code})\n"
            )
        path.write_text(content)
        path.chmod(0o755)
        return path

    def dotnet_stub(self, count_file: Path) -> None:
        self.env["DOTNET_STUB_COUNT"] = str(count_file)
        self.write_stub(self.stubs / "dotnet", DOTNET_STUB)

    def run(self, entry: str) -> Tuple[int, str]:
        completed = subprocess.run(
            [sys.executable, str(LIFECYCLE_DIR / entry), str(self.ws)],
            env=self.env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        return completed.returncode, (completed.stdout or "") + (completed.stderr or "")

    def marker_lines(self) -> List[str]:
        if not self.marker.is_file():
            return []
        return self.marker.read_text().splitlines()

    def bashrc(self) -> str:
        return (self.home / ".bashrc").read_text()

    def cleanup(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)


def expected_block_lines(ws: Path, home: Path, precedence: bool) -> List[str]:
    """The bashrc block the ported scripts write, line for line."""
    ws_arg = str(ws)
    home_arg = str(home)
    lines = [
        BEGIN,
        "# Sourced via .devcontainer/env-local.sh (BOM/CRLF-tolerant; fills empty vars).",
    ]
    if precedence:
        lines += [
            "# precedence=file lets MCP credential names in .env.local override a stale",
            "# copy already exported into this session, so a rotated PAT applies without",
            "# rebuilding the container. PATH and other vars keep the default rule.",
        ]
    lines += [
        f'if [ -f "{ws_arg}/.devcontainer/env-local.sh" ]; then',
        f'    . "{ws_arg}/.devcontainer/env-local.sh"',
    ]
    if precedence:
        lines += ["    DATAVIZ_ENV_LOCAL_PRECEDENCE=file"]
    lines += [
        f'    load_env_local "{ws_arg}/.env.local"',
        "fi",
        f'export NANOCODER_MCPSERVERS_FILE="{ws_arg}/.nanocoder/mcp.json"',
        f'export PATH="{home_arg}/.local/bin:${{PATH}}"',
        END,
    ]
    return lines


def assert_bashrc(bashrc: str, ws: Path, home: Path, precedence: bool, above_lines: Sequence[str]) -> None:
    """Pin the whole bashrc result: the kept content, a fresh line, then the block.

    ``above_lines`` are the stripped lines that survive above the block (none
    on a fresh home). The scripts write ``stripped + newline + block`` and the
    append adds the same fresh line the bash originals added, so a re-run over
    a bashrc that already ends with the block passes the previous blank
    separator line in ``above_lines``.
    """
    block = "\n".join(expected_block_lines(ws, home, precedence)) + "\n"
    stripped = "\n".join(above_lines) + "\n" if above_lines else ""
    expected = stripped + "\n" + block
    assert_true(bashrc == expected, f"bashrc should be exactly the kept content plus the block, got:\n{bashrc!r}")


def stub_calls(lines: Sequence[str]) -> List[str]:
    """The recorded child calls in order (marker lines without the env halves)."""
    return [line.split(" |ENV:", 1)[0].strip() for line in lines if "|ENV:" in line]


def stub_envs(lines: Sequence[str]) -> List[str]:
    """The recorded child environments in order."""
    return [line.split(" |ENV:", 1)[1] for line in lines if "|ENV:" in line]


def case_on_create_bootstraps_and_is_idempotent() -> None:
    sandbox = Sandbox()
    try:
        code, output = sandbox.run("on_create.py")
        assert_exit_code(0, "on_create should succeed in a sandbox", code)
        env_local = sandbox.ws / ".env.local"
        assert_true(env_local.is_file(), "on_create should bootstrap .env.local")
        mode = stat_mode(env_local)
        assert_true(mode == 0o600, f".env.local should be mode 600, got {oct(mode)}")
        content = env_local.read_text()
        match = re.search(r"UNITY_MCP_TOKEN=([0-9a-f]{48})\n", content)
        assert_true(match is not None, f".env.local should carry a 48-hex UNITY_MCP_TOKEN, got:\n{content}")
        for key in ("ZAI_API_KEY=", "OPENROUTER_API_KEY=", "GITHUB_PERSONAL_ACCESS_TOKEN=", "CONTEXT7_API_KEY="):
            assert_true(key in content, f".env.local should list {key}")
        # A fresh home yields a bashrc that is exactly the fresh line plus the block.
        assert_bashrc(sandbox.bashrc(), sandbox.ws, sandbox.home, precedence=False, above_lines=[])
        assert_true(
            stub_calls(sandbox.marker_lines())
            == [
                "STUB install_npm_tools.py| --offline-ok",
                "STUB ai_backends.py| install",
                f"STUB sync_mcp.py| {sandbox.ws}",
            ],
            f"on_create should call the three children with the documented arguments, got {stub_calls(sandbox.marker_lines())}",
        )

        before = env_local.read_text()
        code, output = sandbox.run("on_create.py")
        assert_exit_code(0, "a second on_create run should stay green", code)
        assert_true(env_local.read_text() == before, "a second run must keep the existing .env.local byte for byte")
        # The replacement keeps the previous block's blank separator line, so a
        # re-run's bashrc is the separator, a fresh line, then the block.
        assert_bashrc(sandbox.bashrc(), sandbox.ws, sandbox.home, precedence=False, above_lines=[""])
        assert_true(sandbox.bashrc().count(BEGIN) == 1, "a second run must not duplicate the bashrc block")
        print(output)
    finally:
        sandbox.cleanup()


def case_on_create_tolerates_npm_failure_only() -> None:
    sandbox = Sandbox()
    try:
        sandbox.write_stub(sandbox.ws / ".devcontainer/install_npm_tools.py", FAILING_STUB)
        code, output = sandbox.run("on_create.py")
        assert_exit_code(0, "on_create should tolerate the npm tool refresh failing", code)
        assert_true(
            "⚠️  npm tool refresh failed at create time; image-baked versions remain installed." in output,
            f"the npm failure should warn, got:\n{output}",
        )
        assert_true("STUB sync_mcp.py" in "\n".join(sandbox.marker_lines()), "the sync should still run after the npm warning")

        sandbox.write_stub(sandbox.ws / ".devcontainer/ai_backends.py", FAILING_STUB)
        code, output = sandbox.run("on_create.py")
        assert_exit_code(1, "a backend install failure must abort on_create with its exit code", code)
        print(output)
    finally:
        sandbox.cleanup()


def case_post_create_retries_until_success() -> None:
    """The bounded retry loop runs in a child; one backoff sleep lands under suite concurrency."""
    sandbox = Sandbox()
    try:
        (sandbox.ws / ".config").mkdir()
        (sandbox.ws / ".config" / "dotnet-tools.json").write_text("{}")
        sandbox.dotnet_stub(sandbox.root / "dotnet-count")
        code, output = sandbox.run("post_create.py")
        assert_exit_code(0, "post_create should succeed when the second retry lands", code)
        attempts = [line for line in sandbox.marker_lines() if line.startswith("dotnet tool restore attempt")]
        assert_true(
            attempts == ["dotnet tool restore attempt 1", "dotnet tool restore attempt 2"],
            f"expected two restore attempts, got {attempts}",
        )
        assert_true("Retry 2/3 in" in output, f"the second attempt should be announced, got:\n{output}")
        assert_true("Warning: dotnet tool restore failed" not in output, "a landed retry must not warn")
        assert_true("Data Visualizer Post-Create Setup" in output, "the banner should still print")
        print(output)
    finally:
        sandbox.cleanup()


def case_post_create_installs_pre_commit_hooks() -> None:
    sandbox = Sandbox()
    try:
        sandbox.write_stub(sandbox.stubs / "pre-commit", STUB_RECORD)
        sandbox.ws.joinpath(".pre-commit-config.yaml").write_text("repos: []\n")
        code, output = sandbox.run("post_create.py")
        assert_exit_code(0, "post_create should succeed with the hooks installing", code)
        assert_true("STUB pre-commit| install" in "\n".join(sandbox.marker_lines()), f"pre-commit install should run, got:\n{output}")

        sandbox.stubs.joinpath("pre-commit").unlink()
        sandbox.write_stub(sandbox.stubs / "python3", PYTHON3_FAILS_STUB)
        code, output = sandbox.run("post_create.py")
        assert_exit_code(0, "a failed pip fallback must not fail post_create", code)
        assert_true(
            "Warning: could not install pre-commit; hooks not activated." in output,
            f"the pip fallback should warn, got:\n{output}",
        )
        print(output)
    finally:
        sandbox.cleanup()


def case_post_start_replaces_block_and_loads_env() -> None:
    sandbox = Sandbox()
    try:
        sandbox.ws.joinpath(".env.local").write_text(
            "GITHUB_PERSONAL_ACCESS_TOKEN=file-value\nZAI_API_KEY=\nUNITY_MCP_HTTP_PORT=1\n"
        )
        sandbox.env["GITHUB_PERSONAL_ACCESS_TOKEN"] = "stale-value"
        legacy = "# top line\n" + BEGIN + "\nset -a; . .env.local\n" + END + "\nexport OTHER=keep\n"
        sandbox.home.joinpath(".bashrc").write_text(legacy)
        code, output = sandbox.run("post_start.py")
        assert_exit_code(0, "post_start should succeed in a sandbox", code)
        bashrc = sandbox.bashrc()
        assert_true("set -a" not in bashrc, "the legacy naive block must be replaced")
        assert_bashrc(bashrc, sandbox.ws, sandbox.home, precedence=True, above_lines=["# top line", "export OTHER=keep"])
        calls = stub_calls(sandbox.marker_lines())
        envs = stub_envs(sandbox.marker_lines())
        assert_true(len(calls) >= 4, f"expected the install, backend, doctor, and sync calls, got {calls}")
        assert_true(
            calls[0].startswith("STUB install_npm_tools.py|") and calls[1].startswith("STUB ai_backends.py|"),
            f"the npm and backend installs run before the load, got {calls}",
        )
        assert_true("STUB node| .llm/mcp/mcp-doctor.mjs --repair" in calls[2], f"the doctor runs before the sync, got {calls}")
        assert_true(calls[3].startswith("STUB sync_mcp.py|"), f"the sync runs after the doctor, got {calls}")
        assert_true("GITHUB_PERSONAL_ACCESS_TOKEN=stale-value;" in envs[0], f"children before the load see the exported value, got {envs[0]}")
        assert_true("GITHUB_PERSONAL_ACCESS_TOKEN=file-value;" in envs[3], f"children after the load see the file value, got {envs[3]}")
        print(output)
    finally:
        sandbox.cleanup()


def case_update_content_restores_and_syncs_only_when_present() -> None:
    sandbox = Sandbox()
    try:
        code, output = sandbox.run("update_content.py")
        assert_exit_code(0, "update_content should succeed in a sandbox", code)
        assert_true(
            stub_calls(sandbox.marker_lines()) == [f"STUB sync_mcp.py| {sandbox.ws}"],
            f"update_content should only sync without the dotnet manifest, got {stub_calls(sandbox.marker_lines())}",
        )

        (sandbox.ws / ".config").mkdir()
        (sandbox.ws / ".config" / "dotnet-tools.json").write_text("{}")
        sandbox.dotnet_stub(sandbox.root / "dotnet-count")
        sandbox.marker.unlink()
        code, output = sandbox.run("update_content.py")
        assert_exit_code(0, "update_content should stay green when the guarded restore fails", code)
        lines = sandbox.marker_lines()
        assert_true(len(lines) == 2, f"update_content should restore and then sync, got {lines}")
        assert_true(lines[0] == "dotnet tool restore attempt 1", f"the restore should be recorded first, got {lines}")
        assert_true(lines[1].startswith(f"STUB sync_mcp.py| {sandbox.ws} |ENV:"), f"the sync should be recorded second, got {lines}")

        sandbox.env["PATH"] = "/bin"
        sandbox.marker.unlink()
        code, output = sandbox.run("update_content.py")
        assert_exit_code(0, "update_content should succeed without dotnet or node reachable", code)
        assert_true(sandbox.marker_lines() == [], f"neither tool should run, got {sandbox.marker_lines()}")
        print(output)
    finally:
        sandbox.cleanup()


def case_missing_workspace_fails_for_every_entry() -> None:
    missing = Path(tempfile.gettempdir()) / f"lifecycle-missing-{uuid.uuid4().hex}"
    for entry in ("on_create.py", "post_create.py", "post_start.py", "update_content.py"):
        completed = subprocess.run(
            [sys.executable, str(LIFECYCLE_DIR / entry), str(missing)],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        output = (completed.stdout or "") + (completed.stderr or "")
        assert_exit_code(1, f"{entry} should fail on a missing workspace", completed.returncode)
        assert_true("❌ Workspace not found" in output, f"{entry} should print the workspace failure, got:\n{output}")


def main() -> int:
    print("== devcontainer lifecycle contract tests ==")
    if not posix_available():
        print("    SKIP: bash/POSIX not available; lifecycle contracts need a POSIX shell.")
        print("== devcontainer lifecycle: 0 failure(s) ==")
        return 0

    run_cases([
        ("OnCreate_BootstrapsEnvLocalAndIsIdempotent", case_on_create_bootstraps_and_is_idempotent),
        ("OnCreate_ToleratesNpmRefreshFailureOnly", case_on_create_tolerates_npm_failure_only),
        ("PostCreate_RetriesUntilTheThirdAttemptSucceeds", case_post_create_retries_until_success),
        ("PostCreate_InstallsPreCommitHooksBestEffort", case_post_create_installs_pre_commit_hooks),
        ("PostStart_ReplacesBashrcBlockAndLoadsEnvWithFilePrecedence", case_post_start_replaces_block_and_loads_env),
        ("UpdateContent_RestoresAndSyncsOnlyWhenPresent", case_update_content_restores_and_syncs_only_when_present),
        ("Lifecycle_MissingWorkspaceFailsForEveryEntry", case_missing_workspace_fails_for_every_entry),
    ])

    failures = failure_count()
    print(f"== devcontainer lifecycle: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
