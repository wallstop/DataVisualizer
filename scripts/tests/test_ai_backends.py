#!/usr/bin/env python3
"""ai-backends launcher contract tests (typed Python port of ``test-ai-backends.sh``, issue #171).

Every case from the bash original is kept with the same assertions; only
the driver language changed. The suite stubs ``claude`` and ``codex`` on a
sandbox PATH so no CLI and no network is touched, and pins
``AI_BACKENDS_ENV_FILE`` to a sandbox file so the real .env.local (and any
real credentials) are never read.

The cases that drive a full launch end with the production ``execvp`` of the
stubbed CLI, which only POSIX can carry out natively (a shebang script).
Those cases print a SKIP line on Windows; the interpreter-level cases run
everywhere. The previous Windows coverage for the exec cases came from MSYS
bash shebang emulation of the bash launcher, which no longer exists to
emulate.
"""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path
from typing import Dict, List, Optional, Sequence

sys.dont_write_bytecode = True

from _test_helpers import assert_exit_code, assert_true, new_temp_root, remove_temp_root, run_cases

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
LAUNCHER = REPO_ROOT / ".devcontainer" / "ai_backends.py"
STATE_WRITER = REPO_ROOT / ".devcontainer" / "backend_state.py"

TEST_ENV_KEYS = (
    "ANTHROPIC_AUTH_TOKEN",
    "ANTHROPIC_BASE_URL",
    "ANTHROPIC_API_KEY",
    "ANTHROPIC_DEFAULT_SONNET_MODEL",
    "ANTHROPIC_DEFAULT_OPUS_MODEL",
    "ANTHROPIC_DEFAULT_HAIKU_MODEL",
    "ANTHROPIC_DEFAULT_FABLE_MODEL",
    "CLAUDE_CONFIG_DIR",
    "CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY",
    "OPENROUTER_API_KEY",
    "ZAI_API_KEY",
    "Z_AI_API_KEY",
    "Z_AI_MODE",
)

STUB_SCRIPT = """#!/usr/bin/env python3
import os
import sys

lines = [f"cmd: {os.path.basename(sys.argv[0])}"]
for argument in sys.argv[1:]:
    lines.append(f"arg: {argument}")
for key in os.environ["AI_BACKENDS_TEST_ENV_KEYS"].split():
    if key in os.environ:
        lines.append(f"env: {key}={os.environ[key]}")
    else:
        lines.append(f"env-unset: {key}")
with open(os.environ["AI_BACKENDS_TEST_DUMP"], "w", encoding="utf-8", newline="") as handle:
    handle.write("\\n".join(lines) + "\\n")
sys.exit(0)
"""

# Credential-shaped and launcher-config keys scrubbed from the inherited
# Windows environment: a devcontainer session forwards real secrets, and the
# sandbox must resolve keys only from its own fixtures.
_HOST_SCRUBBED_KEYS = frozenset(
    {
        "ZAI_API_KEY",
        "Z_AI_API_KEY",
        "OPENROUTER_API_KEY",
        "OPEN_ROUTER_API_KEY",
        "GITHUB_PERSONAL_ACCESS_TOKEN",
        "GITHUB_MCP_PAT",
        "GITHUB_TOKEN",
        "UNITY_MCP_TOKEN",
        "CONTEXT7_API_KEY",
        "CLAUDE_ZAI_CONFIG_DIR",
        "CLAUDE_OPENROUTER_CONFIG_DIR",
        "CLAUDE_CONFIG_DIR",
        "DATAVIZ_ENV_LOCAL_PRECEDENCE",
    }
)


class Sandbox:
    """Hermetic fixture tree for one launcher case."""

    def __init__(self) -> None:
        self.root = new_temp_root(prefix="ai-backends-test-")
        self.bin = self.root / "bin"
        self.home = self.root / "home"
        self.codex_home = self.root / "codex-home"
        self.workdir = self.root / "workdir"
        for directory in (self.bin, self.home, self.codex_home, self.workdir):
            directory.mkdir(parents=True)
        (self.root / ".devcontainer").mkdir()
        self.dump = self.root / "dump.txt"
        self.stdout = self.root / "stdout.txt"
        self.stderr = self.root / "stderr.txt"
        self.env_file = self.root / "env.local"
        self.mcp_config = self.root / ".mcp.json"
        self.launcher_names = ("codex-zai", "claude-zai", "codex-openrouter", "claude-openrouter")
        self.last_exit = -1
        self._symlinked = False
        self.write_stub_cli("claude")
        self.write_stub_cli("codex")

    def write_stub_cli(self, name: str) -> None:
        """
            Windows cannot exec a shebang script, so the interpreter-level cases
            that need a running stub are POSIX-only; Windows still needs the stub
            to be FOUND on PATH (shutil.which resolves PATHEXT there), so a stub
            that merely exits cleanly keeps the launcher's failure points where
            the cases expect them.
        """
        if os.name == "nt":
            stub = self.bin / f"{name}.bat"
            stub.write_bytes("@echo off\r\nexit 0\r\n".encode("utf-8"))
            return
        stub = self.bin / name
        stub.write_bytes(STUB_SCRIPT.encode("utf-8"))
        stub.chmod(0o755)

    def link_launchers(self) -> None:
        for launcher in self.launcher_names:
            os.symlink(str(LAUNCHER), str(self.bin / launcher))
        self._symlinked = True

    def copy_launcher_pair(self, broken_state_writer: bool) -> None:
        """Stage the launcher + state writer as a copied self-contained pair.

        A copied launcher computes its workspace from its own location, so a
        deliberately broken ``backend_state.py`` beside it exercises the
        required-versus-optional writer contract hermetically.
        """
        devcontainer = self.root / ".devcontainer"
        target = devcontainer / "ai_backends.py"
        target.write_bytes(LAUNCHER.read_text(encoding="utf-8").encode("utf-8"))
        target.chmod(0o755)
        if broken_state_writer:
            writer = devcontainer / "backend_state.py"
            writer.write_bytes("import sys\nsys.exit(1)\n".encode("utf-8"))

    def write_env_file(self, content: str) -> None:
        self.env_file.write_bytes(content.encode("utf-8"))

    def run(
        self,
        launcher_name: str,
        arguments: Sequence[str] = (),
        extra_env: Optional[Dict[str, str]] = None,
        command: Optional[Sequence[str]] = None,
    ) -> None:
        """Run one launcher action in a hermetic environment.

        A symlinked sandbox dispatches through the real symlink, which is the
        production invocation shape on POSIX. Everything else drives the
        launcher file through the running interpreter with the action named as
        the first argument, the direct-invocation shape.
        """
        if os.name == "nt":
            path_value = f"{self.bin}{os.pathsep}{os.environ.get('PATH', '')}"
        else:
            path_value = f"{self.bin}:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
        if os.name == "nt":
            env: Dict[str, str] = dict(os.environ)
            # Inheriting the full environment brings the host's credentials and
            # config overrides along; scrub every launcher-relevant key so a
            # missing-key case can never see an exported key and a config-dir
            # case can never be pulled off the fixture.
            for key in list(env):
                if key.startswith("AI_BACKENDS_") or key in _HOST_SCRUBBED_KEYS:
                    del env[key]
            # Windows children need the full shell context (TEMP, COMSPEC,
            # SystemRoot) for tempfile and .bat resolution; the sandbox-owned
            # keys below still override everything they control. USERPROFILE
            # follows the sandbox home so Path.home() stays inside the fixture.
            env.update(
                {
                    "PATH": path_value,
                    "HOME": str(self.home),
                    "USERPROFILE": str(self.home),
                    "CODEX_HOME": str(self.codex_home),
                    "AI_BACKENDS_TEST_DUMP": str(self.dump),
                    "AI_BACKENDS_TEST_ENV_KEYS": " ".join(TEST_ENV_KEYS),
                    "AI_BACKENDS_ENV_FILE": str(self.env_file),
                    "AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB": "0",
                    "PYTHON": sys.executable,
                }
            )
            appdata = self.home / "AppData" / "Roaming"
            env["APPDATA"] = str(appdata)
        else:
            env = {
                "PATH": path_value,
                "HOME": str(self.home),
                "CODEX_HOME": str(self.codex_home),
                "AI_BACKENDS_TEST_DUMP": str(self.dump),
                "AI_BACKENDS_TEST_ENV_KEYS": " ".join(TEST_ENV_KEYS),
                "AI_BACKENDS_ENV_FILE": str(self.env_file),
                "AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB": "0",
                "PYTHON": sys.executable,
            }
        if extra_env:
            env.update(extra_env)
        if command is None:
            if self._symlinked:
                command_list: List[str] = [str(self.bin / launcher_name), *arguments]
            else:
                command_list = [sys.executable, str(LAUNCHER), launcher_name, *arguments]
        else:
            command_list = list(command)
        completed = subprocess.run(
            command_list,
            cwd=str(self.workdir),
            env=env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        self.last_exit = completed.returncode
        self.stdout.write_bytes((completed.stdout or "").encode("utf-8"))
        self.stderr.write_bytes((completed.stderr or "").encode("utf-8"))

    @staticmethod
    def _read(path: Path) -> str:
        with open(path, "r", encoding="utf-8", newline="") as handle:
            return handle.read()

    def dump_lines(self) -> List[str]:
        if not self.dump.is_file():
            return []
        return self._read(self.dump).splitlines()

    def stderr_text(self) -> str:
        if not self.stderr.is_file():
            return ""
        return self._read(self.stderr)

    def stdout_text(self) -> str:
        if not self.stdout.is_file():
            return ""
        return self._read(self.stdout)

    def destroy(self) -> None:
        remove_temp_root(self.root)


def _read_file(path: Path) -> str:
    with open(path, "r", encoding="utf-8", newline="") as handle:
        return handle.read()


def assert_dump_line(sandbox: Sandbox, description: str, expected: str) -> None:
    assert_true(
        expected in sandbox.dump_lines(),
        f"{description} (expected exact stub line [{expected}]; dump: {sandbox.dump_lines()})",
    )


def assert_dump_lacks(sandbox: Sandbox, description: str, unwanted: str) -> None:
    matching = [line for line in sandbox.dump_lines() if unwanted in line]
    assert_true(not matching, f"{description} (dump unexpectedly contains [{unwanted}]: {matching})")


def assert_stderr_contains(sandbox: Sandbox, description: str, needle: str) -> None:
    stderr = sandbox.stderr_text()
    assert_true(needle in stderr, f"{description} (stderr missing [{needle}]; stderr: {stderr})")


def assert_file_contains(path: Path, description: str, needle: str) -> None:
    assert_true(path.is_file(), f"{description} (missing file {path})")
    content = _read_file(path)
    assert_true(needle in content, f"{description} (missing [{needle}] in {path})")


def case_claude_zai_uses_env_local_file_key() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("ZAI_API_KEY=zk-file-key\n")
    sandbox.link_launchers()
    sandbox.run("claude-zai", ["--version"])
    assert_exit_code(0, "claude-zai launches with key from .env.local", sandbox.last_exit)
    assert_dump_line(sandbox, "claude-zai auth token from .env.local", "env: ANTHROPIC_AUTH_TOKEN=zk-file-key")
    assert_dump_line(
        sandbox, "claude-zai base URL is Z.ai anthropic endpoint", "env: ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic"
    )
    assert_dump_line(
        sandbox,
        "claude-zai keeps ZAI_API_KEY exported for .mcp.json ${ZAI_API_KEY} expansion",
        "env: ZAI_API_KEY=zk-file-key",
    )
    assert_dump_line(
        sandbox, "claude-zai mirrors Z_AI_API_KEY alias (@z_ai reads it first)", "env: Z_AI_API_KEY=zk-file-key"
    )
    sandbox.destroy()


def case_claude_zai_env_var_wins_over_file() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("ZAI_API_KEY=zk-file-key\n")
    sandbox.link_launchers()
    sandbox.run("claude-zai", ["--version"], extra_env={"ZAI_API_KEY": "zk-env-key"})
    assert_dump_line(sandbox, "process env beats .env.local for ZAI key", "env: ANTHROPIC_AUTH_TOKEN=zk-env-key")
    sandbox.destroy()


def case_claude_zai_accepts_variant_file_key() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("Z_AI_API_KEY=zk-variant-key\n")
    sandbox.link_launchers()
    sandbox.run("claude-zai", ["--version"])
    assert_dump_line(sandbox, "Z_AI_API_KEY variant in .env.local is honored", "env: ANTHROPIC_AUTH_TOKEN=zk-variant-key")
    sandbox.destroy()


def case_claude_zai_missing_key_fails() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("# nothing here\n")
    sandbox.run("claude-zai", ["--version"])
    assert_exit_code(1, "missing ZAI key fails loudly", sandbox.last_exit)
    assert_stderr_contains(sandbox, "error mentions ZAI_API_KEY", "ZAI_API_KEY")
    sandbox.destroy()


def case_claude_zai_sandbox_settings_flag() -> None:
    # The container sandbox override must reach claude via --settings; a bare
    # positional is parsed as the session PROMPT (it literally became the first
    # user message of every claude-zai session).
    sandbox = Sandbox()
    sandbox.write_env_file("ZAI_API_KEY=zk-file-key\n")
    sandbox.link_launchers()
    sandbox.run("claude-zai", ["--version"], extra_env={"AI_BACKENDS_CONTAINER_MODE": "yes"})
    assert_dump_line(sandbox, "sandbox override passed via --settings flag", "arg: --settings")
    assert_dump_line(
        sandbox,
        "sandbox settings JSON disables weaker nested sandbox",
        'arg: {"sandbox":{"enabled":false,"enableWeakerNestedSandbox":true}}',
    )
    sandbox.destroy()


def case_claude_openrouter_uses_env_local_file_key() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("OPENROUTER_API_KEY=sk-or-file-key\n")
    sandbox.link_launchers()
    sandbox.run("claude-openrouter", ["--version"])
    assert_exit_code(0, "claude-openrouter launches with key from .env.local", sandbox.last_exit)
    assert_dump_line(sandbox, "claude-openrouter auth token from .env.local", "env: ANTHROPIC_AUTH_TOKEN=sk-or-file-key")
    assert_dump_line(
        sandbox, "claude-openrouter base URL is OpenRouter anthropic skin", "env: ANTHROPIC_BASE_URL=https://openrouter.ai/api"
    )
    assert_dump_line(
        sandbox, "claude-openrouter blanks ANTHROPIC_API_KEY per OpenRouter docs", "env: ANTHROPIC_API_KEY="
    )
    assert_dump_line(
        sandbox,
        "claude-openrouter uses isolated config dir",
        f"env: CLAUDE_CONFIG_DIR={sandbox.home / '.claude-openrouter'}",
    )
    assert_dump_line(sandbox, "claude-openrouter scrubs raw key from child env", "env-unset: OPENROUTER_API_KEY")
    assert_dump_line(
        sandbox, "claude-openrouter enables gateway model discovery", "env: CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY=1"
    )
    assert_dump_line(
        sandbox, "claude-openrouter default sonnet model", "env: ANTHROPIC_DEFAULT_SONNET_MODEL=~anthropic/claude-sonnet-latest"
    )
    assert_dump_line(
        sandbox, "claude-openrouter default opus model", "env: ANTHROPIC_DEFAULT_OPUS_MODEL=~anthropic/claude-opus-latest"
    )
    assert_dump_line(
        sandbox, "claude-openrouter default haiku model", "env: ANTHROPIC_DEFAULT_HAIKU_MODEL=~anthropic/claude-haiku-latest"
    )
    sandbox.destroy()


def case_claude_openrouter_accepts_variant_file_key() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("OPEN_ROUTER_API_KEY=sk-or-variant-key\n")
    sandbox.link_launchers()
    sandbox.run("claude-openrouter", ["--version"])
    assert_dump_line(
        sandbox, "OPEN_ROUTER_API_KEY variant in .env.local is honored", "env: ANTHROPIC_AUTH_TOKEN=sk-or-variant-key"
    )
    sandbox.destroy()


def case_claude_openrouter_env_var_wins_over_file() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("OPENROUTER_API_KEY=sk-or-file-key\n")
    sandbox.link_launchers()
    sandbox.run("claude-openrouter", ["--version"], extra_env={"OPENROUTER_API_KEY": "sk-or-env-key"})
    assert_dump_line(
        sandbox, "process env beats .env.local for OpenRouter key", "env: ANTHROPIC_AUTH_TOKEN=sk-or-env-key"
    )
    sandbox.destroy()


def case_claude_openrouter_model_overrides() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("OPENROUTER_API_KEY=sk-or-file-key\n")
    sandbox.link_launchers()
    sandbox.run(
        "claude-openrouter",
        [],
        extra_env={
            "CLAUDE_OPENROUTER_SONNET_MODEL": "acme/sonnet-x",
            "CLAUDE_OPENROUTER_OPUS_MODEL": "acme/opus-x",
            "CLAUDE_OPENROUTER_FABLE_MODEL": "acme/fable-x",
        },
    )
    assert_dump_line(sandbox, "sonnet model override honored", "env: ANTHROPIC_DEFAULT_SONNET_MODEL=acme/sonnet-x")
    assert_dump_line(sandbox, "opus model override honored", "env: ANTHROPIC_DEFAULT_OPUS_MODEL=acme/opus-x")
    assert_dump_line(sandbox, "fable model override honored", "env: ANTHROPIC_DEFAULT_FABLE_MODEL=acme/fable-x")
    sandbox.destroy()


def case_claude_openrouter_missing_key_fails() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("# nothing here\n")
    sandbox.run("claude-openrouter", ["--version"])
    assert_exit_code(1, "missing OpenRouter key fails loudly", sandbox.last_exit)
    assert_stderr_contains(sandbox, "error mentions OPENROUTER_API_KEY", "OPENROUTER_API_KEY")
    sandbox.destroy()


def case_codex_openrouter_launch() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("OPENROUTER_API_KEY=sk-or-file-key\n")
    sandbox.link_launchers()
    sandbox.run("codex-openrouter", ["--version"])
    assert_exit_code(0, "codex-openrouter launches with key from .env.local", sandbox.last_exit)
    assert_dump_line(
        sandbox, "codex-openrouter selects devcontainer-openrouter profile", "arg: devcontainer-openrouter"
    )
    assert_dump_line(sandbox, "codex-openrouter default model", "arg: ~openai/gpt-latest")
    assert_dump_line(sandbox, "codex-openrouter provider override", 'arg: model_provider="OPENROUTER"')
    assert_dump_line(
        sandbox, "codex-openrouter exports canonical OPENROUTER_API_KEY", "env: OPENROUTER_API_KEY=sk-or-file-key"
    )
    profile_file = sandbox.codex_home / "devcontainer-openrouter.config.toml"
    assert_file_contains(profile_file, "codex-openrouter profile exists", 'base_url = "https://openrouter.ai/api/v1"')
    assert_file_contains(profile_file, "codex-openrouter profile uses command auth", 'command = "sh"')
    assert_file_contains(profile_file, "codex-openrouter auth echoes OPENROUTER_API_KEY", "echo $OPENROUTER_API_KEY")
    sandbox.destroy()


def case_codex_openrouter_reasoning_effort_validation() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("OPENROUTER_API_KEY=sk-or-file-key\n")
    sandbox.run("codex-openrouter", ["--version"], extra_env={"CODEX_OPENROUTER_REASONING_EFFORT": "bogus"})
    assert_exit_code(1, "invalid reasoning effort rejected", sandbox.last_exit)
    sandbox.link_launchers()
    sandbox.run("codex-openrouter", ["--version"], extra_env={"CODEX_OPENROUTER_REASONING_EFFORT": "xhigh"})
    assert_dump_line(sandbox, "valid reasoning effort forwarded", 'arg: model_reasoning_effort="xhigh"')
    sandbox.destroy()


def case_codex_openrouter_missing_key_fails() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("# nothing here\n")
    sandbox.run("codex-openrouter", ["--version"])
    assert_exit_code(1, "missing OpenRouter key fails for codex too", sandbox.last_exit)
    sandbox.destroy()


def case_codex_zai_regression() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("ZAI_API_KEY=zk-file-key\n")
    sandbox.link_launchers()
    sandbox.run("codex-zai", ["--version"])
    assert_exit_code(0, "codex-zai launches with key from .env.local", sandbox.last_exit)
    assert_dump_line(sandbox, "codex-zai still exports ZAI_API_KEY from .env.local", "env: ZAI_API_KEY=zk-file-key")
    assert_dump_line(sandbox, "codex-zai exports Z_AI_MODE=ZAI for MCP env passthrough", "env: Z_AI_MODE=ZAI")
    assert_dump_line(sandbox, "codex-zai still selects devcontainer-zai profile", "arg: devcontainer-zai")
    assert_file_contains(
        sandbox.codex_home / "devcontainer-zai.config.toml",
        "codex-zai profile uses env_key auth",
        'env_key = "ZAI_API_KEY"',
    )
    sandbox.destroy()


def case_env_file_does_not_clobber_existing_env() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file("PATH=/evil\nZAI_API_KEY=zk-file-key\n")
    sandbox.link_launchers()
    sandbox.run("claude-zai", ["--version"])
    assert_dump_lacks(sandbox, ".env.local never overrides preexisting PATH", "/evil")
    assert_dump_line(sandbox, "other .env.local keys still load", "env: ANTHROPIC_AUTH_TOKEN=zk-file-key")
    sandbox.destroy()


def case_env_file_with_bom_and_crlf_is_parsed() -> None:
    # Windows-edited .env.local files commonly carry a UTF-8 BOM and CRLF
    # endings; bash `source` chokes on the BOM, so the loader must not.
    sandbox = Sandbox()
    sandbox.write_env_file("\ufeff# Machine-local credentials (gitignored).\r\nZAI_API_KEY=zk-bom-key\r\n")
    sandbox.link_launchers()
    sandbox.run("claude-zai", ["--version"])
    assert_dump_line(sandbox, "BOM + CRLF comment-first file loads keys", "env: ANTHROPIC_AUTH_TOKEN=zk-bom-key")
    sandbox.destroy()
    sandbox = Sandbox()
    sandbox.write_env_file("\ufeffZAI_API_KEY=zk-bom-first-key\r\n")
    sandbox.link_launchers()
    sandbox.run("claude-zai", ["--version"])
    assert_dump_line(sandbox, "BOM-prefixed first variable line loads", "env: ANTHROPIC_AUTH_TOKEN=zk-bom-first-key")
    sandbox.destroy()


def case_env_file_precedence_inversion_for_managed_credentials() -> None:
    # DATAVIZ_ENV_LOCAL_PRECEDENCE=file lets a rotated managed credential in
    # .env.local win over a stale exported copy; unmanaged keys keep
    # "non-empty preexisting wins".
    sandbox = Sandbox()
    sandbox.write_env_file("ZAI_API_KEY=zk-rotated-key\n")
    sandbox.link_launchers()
    sandbox.run(
        "claude-zai",
        ["--version"],
        extra_env={"ZAI_API_KEY": "zk-stale-key", "DATAVIZ_ENV_LOCAL_PRECEDENCE": "file"},
    )
    assert_dump_line(
        sandbox, "precedence inversion lets the file credential win", "env: ANTHROPIC_AUTH_TOKEN=zk-rotated-key"
    )
    sandbox.destroy()


def case_config_dir_root_owned_is_self_healed_with_sudo() -> None:
    # Root-owned config dirs (Docker named volumes initialized before the image
    # pre-created the mount target) must be repaired in place when passwordless
    # sudo is available, not left failing. Needs sudo to stage the state; main
    # decides the sudo gate so the skip prints before the pool starts.
    sandbox = Sandbox()
    sandbox.write_env_file("OPENROUTER_API_KEY=sk-or-file-key\n")
    sandbox.link_launchers()
    locked_dir = sandbox.home / ".claude-openrouter"
    subprocess.run(["sudo", "mkdir", "-p", str(locked_dir)], check=True)
    subprocess.run(["sudo", "chown", "root:root", str(locked_dir)], check=True)
    sandbox.run("claude-openrouter", ["--version"])
    assert_exit_code(0, "root-owned config dir is repaired via passwordless sudo", sandbox.last_exit)
    stat_result = os.stat(locked_dir)
    assert_true(
        stat_result.st_uid == os.getuid(),
        f"repaired config dir is owned by the invoking user (owner {stat_result.st_uid})",
    )
    subprocess.run(["sudo", "rm", "-rf", str(locked_dir)], check=True)
    sandbox.destroy()


def case_config_dir_failure_is_actionable() -> None:
    # With neither a writable dir nor a working sudo/chmod, the launcher must
    # still fail fast with the chown remediation. A read-only parent forces
    # mkdir and chmod to fail deterministically; the real passwordless sudo
    # chown of a nonexistent path fails on its own.
    sandbox = Sandbox()
    sandbox.write_env_file("OPENROUTER_API_KEY=sk-or-file-key\n")
    sandbox.link_launchers()
    read_only_parent = sandbox.home / "locked"
    read_only_parent.mkdir()
    read_only_parent.chmod(0o555)
    try:
        sandbox.run(
            "claude-openrouter",
            ["--version"],
            extra_env={"CLAUDE_OPENROUTER_CONFIG_DIR": str(read_only_parent / "child")},
        )
        assert_exit_code(1, "unhealable config dir fails fast", sandbox.last_exit)
        assert_stderr_contains(sandbox, "error includes chown remediation", "chown")
    finally:
        read_only_parent.chmod(0o755)
    sandbox.destroy()


def case_install_creates_all_launchers() -> None:
    sandbox = Sandbox()
    install_bin = sandbox.root / "install-bin"
    install_bin.mkdir()
    sandbox.run(
        "install",
        extra_env={"AI_BACKENDS_BIN_DIR": str(install_bin)},
        command=[sys.executable, str(LAUNCHER), "install"],
    )
    assert_exit_code(0, "install exits zero", sandbox.last_exit)
    for launcher in sandbox.launcher_names:
        target = install_bin / launcher
        assert_true(target.is_symlink(), f"install created {launcher} launcher as a symlink")
    sandbox.destroy()


def case_install_refuses_to_replace_non_symlink_launcher() -> None:
    sandbox = Sandbox()
    install_bin = sandbox.root / "install-bin"
    install_bin.mkdir()
    (install_bin / "codex-zai").write_bytes("# a regular file must never be replaced\n".encode("utf-8"))
    sandbox.run(
        "install",
        extra_env={"AI_BACKENDS_BIN_DIR": str(install_bin)},
        command=[sys.executable, str(LAUNCHER), "install"],
    )
    assert_exit_code(1, "install refuses to replace a non-symlink launcher", sandbox.last_exit)
    assert_stderr_contains(sandbox, "error names the refused target", "Refusing to replace non-symlink launcher")
    sandbox.destroy()


def case_install_seeds_isolated_claude_approvals() -> None:
    # Isolated Claude config dirs (claude-zai / claude-openrouter) read MCP
    # approvals and folder trust from their own .claude.json. `install` must
    # seed them, or every project MCP server shows "Pending approval" there.
    sandbox = Sandbox()
    install_bin = sandbox.root / "install-bin"
    install_bin.mkdir()
    sandbox.mcp_config.write_bytes('{"mcpServers":{"unity":{}}}\n'.encode("utf-8"))
    sandbox.run(
        "install",
        extra_env={
            "AI_BACKENDS_BIN_DIR": str(install_bin),
            "AI_BACKENDS_MCP_CONFIG": str(sandbox.mcp_config),
        },
        command=[sys.executable, str(LAUNCHER), "install"],
    )
    assert_exit_code(0, "install exits zero with approval seeding", sandbox.last_exit)
    for config_dir_name in (".claude-zai", ".claude-openrouter"):
        state_file = sandbox.home / config_dir_name / ".claude.json"
        assert_file_contains(state_file, f"install seeds trust for {config_dir_name}", '"hasTrustDialogAccepted": true')
        assert_file_contains(state_file, f"install seeds managed server approvals for {config_dir_name}", '"unity"')
    sandbox.destroy()


def case_broken_state_writer_keeps_optional_claude_approvals_best_effort() -> None:
    # The Codex profile write is required: a failing state writer must fail the
    # launcher. Claude approval seeding is optional: a failing writer must not
    # stop the launch. The copied launcher pair points its workspace at the
    # sandbox, where backend_state.py is deliberately broken.
    sandbox = Sandbox()
    sandbox.write_env_file("ZAI_API_KEY=z-fixture-key\nOPENROUTER_API_KEY=or-fixture-key\n")
    sandbox.mcp_config.write_bytes('{"mcpServers":{"unity":{}}}\n'.encode("utf-8"))
    sandbox.copy_launcher_pair(broken_state_writer=True)
    copied_launcher = sandbox.root / ".devcontainer" / "ai_backends.py"

    sandbox.run(
        "claude-zai",
        ["--version"],
        extra_env={"AI_BACKENDS_MCP_CONFIG": str(sandbox.mcp_config)},
        command=[sys.executable, str(copied_launcher), "claude-zai", "--version"],
    )
    assert_exit_code(0, "claude-zai continues without the optional approval writer", sandbox.last_exit)
    assert_dump_line(sandbox, "claude-zai reaches exec", "cmd: claude")

    sandbox.run(
        "codex-zai",
        ["--version"],
        command=[sys.executable, str(copied_launcher), "codex-zai", "--version"],
    )
    assert_exit_code(1, "required Codex profile writer still fails when the writer fails", sandbox.last_exit)
    assert_true(
        "Installed codex-zai" not in sandbox.stdout_text(),
        "a failed install must not print the success line",
    )
    sandbox.destroy()


def case_config_dir_failure_aborts_despite_best_effort_approvals() -> None:
    # bash parity: the launcher's `|| true` cannot catch a function-internal
    # `exit`, so a config-dir repair failure aborts claude-zai even though the
    # approval seeding is best-effort. The copied pair points the workspace at
    # the sandbox, where the state writer is broken AND the config parent is
    # read-only, so the writer failure (swallowed) and the config failure
    # (fatal) are both live in one case.
    sandbox = Sandbox()
    sandbox.write_env_file("ZAI_API_KEY=z-fixture-key\n")
    sandbox.mcp_config.write_bytes('{"mcpServers":{"unity":{}}}\n'.encode("utf-8"))
    sandbox.copy_launcher_pair(broken_state_writer=True)
    copied_launcher = sandbox.root / ".devcontainer" / "ai_backends.py"
    read_only_parent = sandbox.home / "locked"
    read_only_parent.mkdir()
    read_only_parent.chmod(0o555)
    try:
        sandbox.run(
            "claude-zai",
            ["--version"],
            extra_env={
                "CLAUDE_ZAI_CONFIG_DIR": str(read_only_parent / "child"),
                "AI_BACKENDS_MCP_CONFIG": str(sandbox.mcp_config),
            },
            command=[sys.executable, str(copied_launcher), "claude-zai", "--version"],
        )
        assert_exit_code(1, "a config-dir failure aborts despite best-effort approvals", sandbox.last_exit)
        assert_stderr_contains(sandbox, "error includes chown remediation", "chown")
        assert_true(not sandbox.dump.is_file(), "the launch must not reach exec after a config-dir failure")
    finally:
        read_only_parent.chmod(0o755)
    sandbox.destroy()


def case_direct_invocation_runs_help_and_unknown_action_fails() -> None:
    sandbox = Sandbox()
    sandbox.run("help", command=[sys.executable, str(LAUNCHER)])
    assert_exit_code(0, "no-argument invocation prints help", sandbox.last_exit)
    help_text = sandbox.stdout_text()
    assert_true("Usage:" in help_text, f"help output should carry the usage block, got: {help_text[:200]}")
    sandbox.run("bogus", command=[sys.executable, str(LAUNCHER), "bogus-action"])
    assert_exit_code(1, "unknown action fails loudly", sandbox.last_exit)
    assert_stderr_contains(sandbox, "error names the action", "Unknown action: bogus-action")
    sandbox.destroy()


def case_state_writer_falls_back_when_early_candidates_cannot_execute() -> None:
    # The state-writer interpreter is a validated chain, not a fixed name: a
    # broken python3 must not break the launcher when a working interpreter
    # exists later in the chain, and the launcher's own interpreter is the
    # final fallback.
    sandbox = Sandbox()
    sandbox.write_env_file("ZAI_API_KEY=z-fixture-key\n")
    binary = sandbox.root / "interp"
    binary.mkdir()
    broken = binary / "python3"
    broken.write_bytes("#!/bin/sh\nexit 1\n".encode("utf-8"))
    broken.chmod(0o755)
    (binary / "python").symlink_to(sys.executable)

    sandbox.run(
        "install",
        extra_env={
            "AI_BACKENDS_BIN_DIR": str(sandbox.root / "install-bin"),
            "PATH": f"{binary}{os.pathsep}{os.environ.get('PATH', '')}",
        },
        command=[sys.executable, str(LAUNCHER), "install"],
    )
    assert_exit_code(0, "install succeeds when a later chain candidate works", sandbox.last_exit)
    assert_true(
        (sandbox.codex_home / "devcontainer-zai.config.toml").is_file(),
        "the fallback interpreter should still write the codex profile",
    )

    # A broken PYTHON override plus a PATH with no working interpreter still
    # reaches the launcher's own interpreter, the chain's final fallback.
    broken_only = sandbox.root / "interp-broken"
    broken_only.mkdir()
    for name in ("python3", "python"):
        candidate = broken_only / name
        candidate.write_bytes("#!/bin/sh\nexit 1\n".encode("utf-8"))
        candidate.chmod(0o755)
    sandbox.run(
        "install",
        extra_env={
            "AI_BACKENDS_BIN_DIR": str(sandbox.root / "install-bin"),
            "PATH": str(broken_only),
            "PYTHON": str(broken_only / "python3"),
        },
        command=[sys.executable, str(LAUNCHER), "install"],
    )
    assert_exit_code(0, "a broken PYTHON override falls through to the running interpreter", sandbox.last_exit)
    sandbox.destroy()


CASES = (
    case_claude_zai_uses_env_local_file_key,
    case_claude_zai_env_var_wins_over_file,
    case_claude_zai_accepts_variant_file_key,
    case_claude_zai_missing_key_fails,
    case_claude_zai_sandbox_settings_flag,
    case_claude_openrouter_uses_env_local_file_key,
    case_claude_openrouter_accepts_variant_file_key,
    case_claude_openrouter_env_var_wins_over_file,
    case_claude_openrouter_model_overrides,
    case_claude_openrouter_missing_key_fails,
    case_codex_openrouter_launch,
    case_codex_openrouter_reasoning_effort_validation,
    case_codex_openrouter_missing_key_fails,
    case_codex_zai_regression,
    case_env_file_does_not_clobber_existing_env,
    case_env_file_with_bom_and_crlf_is_parsed,
    case_env_file_precedence_inversion_for_managed_credentials,
    case_config_dir_root_owned_is_self_healed_with_sudo,
    case_config_dir_failure_is_actionable,
    case_install_creates_all_launchers,
    case_install_refuses_to_replace_non_symlink_launcher,
    case_install_seeds_isolated_claude_approvals,
    case_broken_state_writer_keeps_optional_claude_approvals_best_effort,
    case_state_writer_falls_back_when_early_candidates_cannot_execute,
    case_config_dir_failure_aborts_despite_best_effort_approvals,
    case_direct_invocation_runs_help_and_unknown_action_fails,
)


# Platform gates decided by main, not by the case bodies: bodies run on the
# worker pool, where a printed SKIP line would land in completion order
# instead of the submitted order the sequential runner produced. Messages are
# the originals so the replayed output stays byte-comparable to it.
POSIX_ONLY_CASES: Dict[str, str] = {
    "case_claude_zai_uses_env_local_file_key": "exec-path case requires a POSIX host",
    "case_claude_zai_env_var_wins_over_file": "exec-path case requires a POSIX host",
    "case_claude_zai_accepts_variant_file_key": "exec-path case requires a POSIX host",
    "case_claude_zai_sandbox_settings_flag": "exec-path case requires a POSIX host",
    "case_claude_openrouter_uses_env_local_file_key": "exec-path case requires a POSIX host",
    "case_claude_openrouter_accepts_variant_file_key": "exec-path case requires a POSIX host",
    "case_claude_openrouter_env_var_wins_over_file": "exec-path case requires a POSIX host",
    "case_claude_openrouter_model_overrides": "exec-path case requires a POSIX host",
    "case_codex_openrouter_launch": "exec-path case requires a POSIX host",
    "case_codex_openrouter_reasoning_effort_validation": "valid-effort exec check requires a POSIX host",
    "case_codex_zai_regression": "exec-path case requires a POSIX host",
    "case_env_file_does_not_clobber_existing_env": "exec-path case requires a POSIX host",
    "case_env_file_with_bom_and_crlf_is_parsed": "exec-path case requires a POSIX host",
    "case_env_file_precedence_inversion_for_managed_credentials": "exec-path case requires a POSIX host",
    "case_config_dir_root_owned_is_self_healed_with_sudo": "sudo repair case requires a POSIX host",
    "case_config_dir_failure_is_actionable": "config-dir failure case requires a POSIX host",
    "case_install_creates_all_launchers": "symlink creation case requires a POSIX host",
    "case_install_seeds_isolated_claude_approvals": "symlink creation case requires a POSIX host",
    "case_broken_state_writer_keeps_optional_claude_approvals_best_effort": "exec-path case requires a POSIX host",
    "case_state_writer_falls_back_when_early_candidates_cannot_execute": "interpreter fixture symlinks require a POSIX host",
    "case_config_dir_failure_aborts_despite_best_effort_approvals": "exec-path case requires a POSIX host",
}

# The sudo repair case also needs a non-root user with passwordless sudo;
# main probes that once so the decision cannot race with the pool.
SUDO_REPAIR_CASE = "case_config_dir_root_owned_is_self_healed_with_sudo"


def main() -> int:
    assert_true(LAUNCHER.is_file(), f"launcher not found at {LAUNCHER}")
    assert_true(STATE_WRITER.is_file(), f"state writer not found at {STATE_WRITER}")
    print("== ai-backends launcher contract tests ==")

    posix = os.name == "posix"
    sudo_ready = (
        posix
        and os.getuid() != 0
        and subprocess.run(["sudo", "-n", "true"], capture_output=True, check=False).returncode == 0
    )
    # Every case owns its Sandbox temp root and spawns children with an
    # explicit environment, so the cases overlap their waits on a bounded
    # pool; four workers match the suite runner's file-level cap.
    if posix:
        selected = [case for case in CASES if sudo_ready or case.__name__ != SUDO_REPAIR_CASE]
        failures = run_cases([(case.__name__, case) for case in selected], max_workers=4)
    else:
        # Preserve the original interleaved skip/pass order on non-POSIX hosts.
        failures = 0
        for case in CASES:
            reason = POSIX_ONLY_CASES.get(case.__name__)
            if reason is not None:
                print(f"    SKIP: {reason}")
                continue
            failures += run_cases([(case.__name__, case)], max_workers=1)
    print(f"== ai-backends: {failures} failure(s) ==")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
