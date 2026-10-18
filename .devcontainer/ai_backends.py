#!/usr/bin/env python3
"""Install and launch isolated Z.ai and OpenRouter backends (issue #171).

Typed, stdlib-only port of ``ai-backends.sh``. Launchers installed as:
codex-zai, claude-zai, codex-openrouter, claude-openrouter (symlinks to
this script). The ordinary ``codex`` and ``claude`` commands keep their
native backends.

Credentials: launchers self-load a gitignored .env.local (workspace root
beside this script, or the current directory, or AI_BACKENDS_ENV_FILE) for
any credential that is not already exported. Preexisting environment
variables always win, so non-interactive shells (agents, tasks, CI) resolve
keys without relying on interactive shell rc sourcing. The parser mirrors
``load_env_local`` in ``env-local.sh`` (KEY=VALUE, ``export `` prefix,
symmetric quotes, blanks and comments skipped, UTF-8 BOM and CRLF
tolerated), which interactive shells keep sourcing from ``~/.bashrc``.
"""
from __future__ import annotations

import os
import platform
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Callable, Dict, List, NoReturn, Optional, Sequence, Tuple

from env_local import load_environment

PROFILE_NAME = "devcontainer-zai"
OPENROUTER_PROFILE_NAME = "devcontainer-openrouter"
ZAI_ANTHROPIC_URL = "https://api.z.ai/api/anthropic"
OPENROUTER_ANTHROPIC_URL = "https://openrouter.ai/api"

LAUNCHER_NAMES = ("codex-zai", "claude-zai", "codex-openrouter", "claude-openrouter")

# Environment variables each Claude launch action unsets before re-exporting.
# Membership matches the bash original's unset list; order is not observable.
CLAUDE_ZAI_UNSET_KEYS = (
    "ANTHROPIC_API_KEY",
    "ANTHROPIC_MODEL",
    "ANTHROPIC_DEFAULT_MODEL",
    "ANTHROPIC_DEFAULT_HAIKU_MODEL",
    "ANTHROPIC_DEFAULT_SONNET_MODEL",
    "ANTHROPIC_DEFAULT_OPUS_MODEL",
    "ANTHROPIC_SMALL_FAST_MODEL",
    "CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST",
    "CLAUDE_CODE_USE_ANTHROPIC_AWS",
    "CLAUDE_CODE_USE_BEDROCK",
    "CLAUDE_CODE_USE_VERTEX",
    "CLAUDE_CODE_USE_FOUNDRY",
    "CLAUDE_CODE_USE_MANTLE",
    "CLAUDE_CODE_USE_GATEWAY",
)
CLAUDE_OPENROUTER_UNSET_KEYS = (
    "ANTHROPIC_API_KEY",
    "ANTHROPIC_MODEL",
    "ANTHROPIC_DEFAULT_MODEL",
    "ANTHROPIC_DEFAULT_FABLE_MODEL",
    "ANTHROPIC_DEFAULT_HAIKU_MODEL",
    "ANTHROPIC_DEFAULT_SONNET_MODEL",
    "ANTHROPIC_DEFAULT_OPUS_MODEL",
    "ANTHROPIC_SMALL_FAST_MODEL",
    "CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST",
    "CLAUDE_CODE_USE_ANTHROPIC_AWS",
    "CLAUDE_CODE_USE_BEDROCK",
    "CLAUDE_CODE_USE_VERTEX",
    "CLAUDE_CODE_USE_FOUNDRY",
    "CLAUDE_CODE_USE_MANTLE",
    "CLAUDE_CODE_USE_GATEWAY",
)

# Settings JSON disabling Claude's weaker nested sandbox inside containers,
# where the outer devcontainer is the primary isolation boundary. Passed to
# claude via --settings: a bare positional would be parsed as the session
# prompt (and historically became the first user message of every session).
CLAUDE_SANDBOX_SETTINGS = '{"sandbox":{"enabled":false,"enableWeakerNestedSandbox":true}}'

_STATE_WRITER_MISSING_MESSAGE = (
    "[ai-backends] ERROR: A working Python 3 interpreter is required for backend state writes."
)

_SCRIPT_PATH = Path(__file__).resolve()
WORKSPACE_ROOT = _SCRIPT_PATH.parent.parent

_state_writer_python: Optional[str] = None


def die(message: str) -> NoReturn:
    """Print the launcher error shape and exit 1, like the bash ``die``."""
    print(f"[ai-backends] ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def load_env_local(env_file: Optional[str] = None) -> None:
    """Load KEY=VALUE lines from .env.local without overriding the environment.

    Mirrors ``load_env_local`` in ``env-local.sh``: UTF-8 BOM and CRLF
    tolerated, ``export `` prefix and symmetric quotes stripped, blanks and
    comments skipped, unknown key characters rejected, and a non-empty
    preexisting variable always wins except for the managed MCP credential
    names when ``DATAVIZ_ENV_LOCAL_PRECEDENCE=file``.
    """
    resolved: Optional[str] = None
    candidate = env_file or os.environ.get("AI_BACKENDS_ENV_FILE")
    if candidate:
        if not Path(candidate).is_file():
            return
        resolved = candidate
    elif Path(WORKSPACE_ROOT / ".env.local").is_file():
        resolved = str(WORKSPACE_ROOT / ".env.local")
    elif Path(".env.local").is_file():
        resolved = ".env.local"
    else:
        return

    os.environ.update(load_environment(Path(resolved), os.environ))


def resolve_action(invoked_as: str, first_argument: Optional[str]) -> str:
    """Return the action: a launcher basename wins, else the argument."""
    if invoked_as in LAUNCHER_NAMES:
        return invoked_as
    return first_argument if first_argument else "help"


def resolve_zai_key() -> str:
    for candidate in ("ZAI_API_KEY", "Z_AI_API_KEY"):
        value = os.environ.get(candidate)
        if value:
            return value
    die(
        "Set ZAI_API_KEY (or Z_AI_API_KEY) before launching a Z.ai backend. "
        "Add it to a gitignored .env.local at the workspace root "
        "(see .env.local.example); launchers load it automatically."
    )


def resolve_openrouter_key() -> str:
    for candidate in ("OPENROUTER_API_KEY", "OPEN_ROUTER_API_KEY"):
        value = os.environ.get(candidate)
        if value:
            return value
    die(
        "Set OPENROUTER_API_KEY (or OPEN_ROUTER_API_KEY) before launching an "
        "OpenRouter backend. Add it to a gitignored .env.local at the workspace "
        "root (see .env.local.example); launchers load it automatically."
    )


def resolve_timeout_ms(var_name: str, default_ms: int) -> str:
    """Validate a "<VAR>_TIMEOUT_MS" style override and return its value."""
    raw = os.environ.get(var_name, str(default_ms))
    if not raw.isdigit():
        die(f"{var_name} must be a positive integer.")
    if int(raw) == 0:
        die(f"{var_name} must be greater than zero.")
    return raw


def _interpreter_runs_command(candidate: Optional[str]) -> bool:
    """Validate that a candidate interpreter exists and actually executes."""
    if not candidate:
        return False
    try:
        completed = subprocess.run(
            [candidate, "-c", ""],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
    except OSError:
        return False
    return completed.returncode == 0


def resolve_state_python() -> str:
    """Resolve one interpreter for backend state writes, once per process.

    ``PYTHON`` is only the first candidate of a validated chain, exactly like
    the bash original; the interpreter running this launcher is the final
    fallback, so a launcher never fails merely for lack of an interpreter on
    PATH.
    """
    global _state_writer_python
    if _state_writer_python is not None:
        return _state_writer_python
    for candidate in (os.environ.get("PYTHON"), "python3", "python", sys.executable):
        if _interpreter_runs_command(candidate):
            _state_writer_python = candidate
            return candidate
    die("A working Python 3 interpreter is required for backend state writes.")


def run_backend_state(arguments: Sequence[str]) -> int:
    """Run ``backend_state.py`` with the resolved interpreter."""
    command = [resolve_state_python(), str(WORKSPACE_ROOT / ".devcontainer" / "backend_state.py")]
    command.extend(arguments)
    return subprocess.run(command, check=False).returncode


def prepare_config_dir(directory: Path) -> None:
    """Create and secure a private config directory, repairing root-owned ones.

    Root-owned Docker named volumes (created before the image pre-built the
    mount target) are repaired in place via passwordless sudo, mirroring
    post_start.py's uv cache repair; the launcher only fails with remediation
    guidance when neither path works.
    """
    try:
        directory.mkdir(parents=True, exist_ok=True)
    except OSError:
        pass
    try:
        os.chmod(directory, 0o700)
        return
    except OSError:
        pass
    try:
        repaired = (
            subprocess.run(
                [
                    "sudo",
                    "-n",
                    "chown",
                    "-R",
                    f"{os.getuid()}:{os.getgid()}",
                    str(directory),
                ],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
            ).returncode
            == 0
            and os.chmod(directory, 0o700) is None
        )
    except OSError:
        repaired = False
    if not repaired:
        die(
            f"Cannot secure config directory {directory} (chmod 700 failed). "
            "If it is a Docker volume with root ownership, repair it with: "
            f'sudo chown -R "$(id -u):$(id -g)" {directory}'
        )


def detect_container_mode() -> str:
    container_mode = os.environ.get("AI_BACKENDS_CONTAINER_MODE", "auto")
    if container_mode == "auto":
        if Path("/.dockerenv").is_file() or Path("/run/.containerenv").is_file():
            return "yes"
        return "no"
    if container_mode in ("yes", "no"):
        return container_mode
    die("AI_BACKENDS_CONTAINER_MODE must be auto, yes, or no.")


def claude_scrub_value() -> str:
    """Effective Claude Code subprocess env-scrub preference.

    Legacy ``CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB`` stays honored for the Z.ai
    launcher.
    """
    scrub = os.environ.get(
        "AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB",
        os.environ.get("CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB", "auto"),
    )
    if scrub == "auto":
        return "0" if detect_container_mode() == "yes" else "1"
    if scrub in ("0", "1"):
        return scrub
    die("AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB must be auto, 0, or 1.")


def claude_sandbox_arg() -> Optional[str]:
    """Return the sandbox settings JSON inside containers, else ``None``."""
    if detect_container_mode() == "yes":
        return CLAUDE_SANDBOX_SETTINGS
    return None


def preflight_claude_subprocess_scrub(scrub: str) -> None:
    if platform.system() == "Linux" and scrub == "1":
        if not shutil.which("bwrap"):
            die("bubblewrap is required for Claude subprocess isolation; install bubblewrap and socat.")
        if not shutil.which("socat"):
            die("socat is required for Claude sandbox networking; install bubblewrap and socat.")
        completed = subprocess.run(
            [
                "bwrap",
                "--unshare-user",
                "--ro-bind",
                "/",
                "/",
                "--bind",
                "/proc",
                "/proc",
                "--dev",
                "/dev",
                "true",
            ],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        if completed.returncode != 0:
            die(
                "Claude subprocess isolation cannot create its nested sandbox. "
                "Set AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB=0 "
                "(or CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB=0) to rely on the outer "
                "devcontainer boundary."
            )


def codex_home() -> Path:
    return Path(os.environ.get("CODEX_HOME", str(Path.home() / ".codex")))


def install_codex_profile(profile: str) -> None:
    home = codex_home()
    prepare_config_dir(home)
    if run_backend_state(["profile", profile, "--config-dir", str(home)]) != 0:
        raise SystemExit(1)


def install_launchers() -> None:
    bin_dir_override = os.environ.get("AI_BACKENDS_BIN_DIR")
    if bin_dir_override:
        bin_dir = Path(bin_dir_override)
    elif f":{os.environ.get('PATH', '')}:".find(f":{Path.home() / '.local/bin'}:") >= 0:
        bin_dir = Path.home() / ".local/bin"
    else:
        codex_command = shutil.which("codex")
        if codex_command and os.access(str(Path(codex_command).parent), os.W_OK):
            bin_dir = Path(codex_command).parent
        else:
            bin_dir = Path.home() / ".local/bin"
    bin_dir.mkdir(parents=True, exist_ok=True)

    for launcher in LAUNCHER_NAMES:
        target = bin_dir / launcher
        if os.path.lexists(target) and not os.path.islink(target):
            die(f"Refusing to replace non-symlink launcher: {target}")
        if os.path.lexists(target):
            os.remove(target)
        os.symlink(str(_SCRIPT_PATH), str(target))


class _StateWriteFailure(Exception):
    """The backend-state writer exited nonzero (bash: a function return)."""


def seed_claude_approvals(config_dir: Path) -> None:
    """Seed folder trust + MCP server approvals into an isolated config dir.

    Mirrors what configure.mjs records for ~/.claude.json and what the
    interactive dialogs write. Without this, every project .mcp.json server
    shows "Pending approval" in claude-zai / claude-openrouter because they
    never read ~/.claude.json.
    """
    mcp_config = os.environ.get("AI_BACKENDS_MCP_CONFIG", str(WORKSPACE_ROOT / ".mcp.json"))
    if not Path(mcp_config).is_file():
        return
    prepare_config_dir(config_dir)
    if run_backend_state(
        [
            "approve",
            "--config-dir",
            str(config_dir),
            "--project",
            str(WORKSPACE_ROOT),
            "--mcp-config",
            mcp_config,
        ]
    ) != 0:
        raise _StateWriteFailure


def _best_effort_seed(config_dir: Path) -> None:
    """Seed approvals, swallowing only the writer's failure.

    bash's ``|| true`` cannot catch a function-internal ``exit``: a config-dir
    repair failure aborts the whole launcher exactly as it did before the port,
    and only the state writer's nonzero return is swallowed here.
    """
    try:
        seed_claude_approvals(config_dir)
    except _StateWriteFailure:
        pass


def install_backend_support() -> None:
    install_codex_profile("zai")
    install_codex_profile("openrouter")
    install_launchers()
    _best_effort_seed(Path(os.environ.get("CLAUDE_ZAI_CONFIG_DIR", str(Path.home() / ".claude-zai"))))
    _best_effort_seed(
        Path(os.environ.get("CLAUDE_OPENROUTER_CONFIG_DIR", str(Path.home() / ".claude-openrouter")))
    )
    print("[ai-backends] Installed codex-zai, claude-zai, codex-openrouter, and claude-openrouter launchers.")


def _execvp(binary: str, arguments: Sequence[str]) -> NoReturn:
    os.execvp(binary, [binary, *arguments])


def launch_codex_zai(arguments: Sequence[str]) -> None:
    if not shutil.which("codex"):
        die("codex is not installed.")
    zai_key = resolve_zai_key()
    os.environ["ZAI_API_KEY"] = zai_key
    # Codex cannot inject static env values into MCP servers — env_vars entries
    # pass through from this process — so set the mode the @z_ai vision server
    # expects, matching the claude/opencode/vscode configs.
    os.environ["Z_AI_MODE"] = "ZAI"

    home = codex_home()
    catalog_file = home / f"{PROFILE_NAME}-models.json"
    if not (home / f"{PROFILE_NAME}.config.toml").is_file() or not catalog_file.is_file():
        install_codex_profile("zai")

    model = os.environ.get("CODEX_ZAI_MODEL", "glm-5.3")
    reasoning_effort = os.environ.get("CODEX_ZAI_REASONING_EFFORT", "max")
    if reasoning_effort not in ("low", "high", "max"):
        die("CODEX_ZAI_REASONING_EFFORT must be low, high, or max.")
    command = [
        "--profile",
        PROFILE_NAME,
        "--model",
        model,
        "--config",
        'model_provider="ZAI"',
        "--config",
        f'model_reasoning_effort="{reasoning_effort}"',
        "--config",
        f'model_catalog_json="{catalog_file}"',
    ]
    _execvp("codex", [*command, *arguments])


def launch_codex_openrouter(arguments: Sequence[str]) -> None:
    if not shutil.which("codex"):
        die("codex is not installed.")
    or_key = resolve_openrouter_key()
    os.environ["OPENROUTER_API_KEY"] = or_key
    os.environ.pop("OPEN_ROUTER_API_KEY", None)

    home = codex_home()
    if not (home / f"{OPENROUTER_PROFILE_NAME}.config.toml").is_file():
        install_codex_profile("openrouter")

    model = os.environ.get("CODEX_OPENROUTER_MODEL", "~openai/gpt-latest")
    reasoning_effort = os.environ.get("CODEX_OPENROUTER_REASONING_EFFORT", "high")
    if reasoning_effort not in ("low", "medium", "high", "xhigh", "max"):
        die("CODEX_OPENROUTER_REASONING_EFFORT must be low, medium, high, xhigh, or max.")
    command = [
        "--profile",
        OPENROUTER_PROFILE_NAME,
        "--model",
        model,
        "--config",
        'model_provider="OPENROUTER"',
        "--config",
        f'model_reasoning_effort="{reasoning_effort}"',
    ]
    _execvp("codex", [*command, *arguments])


def _claude_common_setup(
    config_dir: Path, timeout_var: str, default_timeout_ms: int
) -> Tuple[str, List[str], str]:
    """Shared Claude launch preamble: timeout, config dir, scrub, sandbox arg."""
    timeout_ms = resolve_timeout_ms(timeout_var, default_timeout_ms)
    prepare_config_dir(config_dir)
    _best_effort_seed(config_dir)

    scrub = claude_scrub_value()
    preflight_claude_subprocess_scrub(scrub)
    arguments: List[str] = []
    sandbox_arg = claude_sandbox_arg()
    if sandbox_arg:
        arguments.extend(["--settings", sandbox_arg])
    return scrub, arguments, timeout_ms


def launch_claude_zai(arguments: Sequence[str]) -> None:
    if not shutil.which("claude"):
        die("claude is not installed.")
    zai_key = resolve_zai_key()
    config_dir = Path(os.environ.get("CLAUDE_ZAI_CONFIG_DIR", str(Path.home() / ".claude-zai")))
    scrub, extra_arguments, timeout_ms = _claude_common_setup(config_dir, "ZAI_API_TIMEOUT_MS", 300000)

    for key in CLAUDE_ZAI_UNSET_KEYS:
        os.environ.pop(key, None)
    os.environ["ANTHROPIC_AUTH_TOKEN"] = zai_key
    # Keep the raw key exported under both spellings: project .mcp.json expands
    # ${ZAI_API_KEY} inside MCP servers (zai-web-search Authorization,
    # zai-vision startup — the @z_ai server exits when it is empty), and
    # @z_ai/mcp-server reads Z_AI_API_KEY first. The same secret already rides
    # along as ANTHROPIC_AUTH_TOKEN, so this adds variable names, not exposure.
    os.environ["ZAI_API_KEY"] = zai_key
    os.environ["Z_AI_API_KEY"] = zai_key
    os.environ["ANTHROPIC_BASE_URL"] = ZAI_ANTHROPIC_URL
    os.environ["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = os.environ.get(
        "CLAUDE_ZAI_HAIKU_MODEL", "glm-5.3-flash[1m]"
    )
    os.environ["ANTHROPIC_DEFAULT_SONNET_MODEL"] = os.environ.get(
        "CLAUDE_ZAI_SONNET_MODEL", "glm-5.3[1m]"
    )
    os.environ["ANTHROPIC_DEFAULT_OPUS_MODEL"] = os.environ.get("CLAUDE_ZAI_OPUS_MODEL", "glm-5.3[1m]")
    os.environ["API_TIMEOUT_MS"] = timeout_ms
    os.environ["CLAUDE_CODE_AUTO_COMPACT_WINDOW"] = "1000000"
    os.environ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1"
    os.environ["CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST"] = "1"
    os.environ["CLAUDE_CODE_SUBPROCESS_ENV_SCRUB"] = scrub
    os.environ["CLAUDE_CONFIG_DIR"] = str(config_dir)
    _execvp("claude", [*extra_arguments, *arguments])


def launch_claude_openrouter(arguments: Sequence[str]) -> None:
    if not shutil.which("claude"):
        die("claude is not installed.")
    or_key = resolve_openrouter_key()
    config_dir = Path(
        os.environ.get("CLAUDE_OPENROUTER_CONFIG_DIR", str(Path.home() / ".claude-openrouter"))
    )
    scrub, extra_arguments, timeout_ms = _claude_common_setup(
        config_dir, "OPENROUTER_API_TIMEOUT_MS", 300000
    )

    for key in CLAUDE_OPENROUTER_UNSET_KEYS:
        os.environ.pop(key, None)
    os.environ["ANTHROPIC_BASE_URL"] = OPENROUTER_ANTHROPIC_URL
    os.environ["ANTHROPIC_AUTH_TOKEN"] = or_key
    # OpenRouter contract: ANTHROPIC_API_KEY must be explicitly empty so Claude
    # Code never falls back to x-api-key auth against Anthropic directly.
    os.environ["ANTHROPIC_API_KEY"] = ""
    os.environ["ANTHROPIC_DEFAULT_FABLE_MODEL"] = os.environ.get(
        "CLAUDE_OPENROUTER_FABLE_MODEL", "~anthropic/claude-fable-latest"
    )
    os.environ["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = os.environ.get(
        "CLAUDE_OPENROUTER_HAIKU_MODEL", "~anthropic/claude-haiku-latest"
    )
    os.environ["ANTHROPIC_DEFAULT_SONNET_MODEL"] = os.environ.get(
        "CLAUDE_OPENROUTER_SONNET_MODEL", "~anthropic/claude-sonnet-latest"
    )
    os.environ["ANTHROPIC_DEFAULT_OPUS_MODEL"] = os.environ.get(
        "CLAUDE_OPENROUTER_OPUS_MODEL", "~anthropic/claude-opus-latest"
    )
    os.environ["CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY"] = os.environ.get(
        "CLAUDE_OPENROUTER_GATEWAY_DISCOVERY", "1"
    )
    subagent_model = os.environ.get("CLAUDE_OPENROUTER_SUBAGENT_MODEL")
    if subagent_model:
        os.environ["CLAUDE_CODE_SUBAGENT_MODEL"] = subagent_model
    os.environ["API_TIMEOUT_MS"] = timeout_ms
    os.environ["CLAUDE_CODE_AUTO_COMPACT_WINDOW"] = "1000000"
    os.environ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1"
    os.environ["CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST"] = "1"
    os.environ["CLAUDE_CODE_SUBPROCESS_ENV_SCRUB"] = scrub
    os.environ["CLAUDE_CONFIG_DIR"] = str(config_dir)
    os.environ.pop("OPENROUTER_API_KEY", None)
    os.environ.pop("OPEN_ROUTER_API_KEY", None)
    _execvp("claude", [*extra_arguments, *arguments])


HELP_TEXT = """Usage:
  python3 .devcontainer/ai_backends.py install
  codex-zai [codex arguments...]
  claude-zai [claude arguments...]
  codex-openrouter [codex arguments...]
  claude-openrouter [claude arguments...]

The ordinary `codex` and `claude` commands retain their native backends.

Credentials: every launcher loads a gitignored .env.local for keys that are
not already exported (workspace root beside this script, then the current
directory, or AI_BACKENDS_ENV_FILE when set). Preexisting environment
variables always win, so non-interactive shells work without rc sourcing.
Z.ai keys: ZAI_API_KEY (or Z_AI_API_KEY). OpenRouter keys: OPENROUTER_API_KEY
(or OPEN_ROUTER_API_KEY, sk-or-...). Set them only in your private shell or
secret store; see .env.local.example.

Optional overrides:
  Z.ai:       CODEX_ZAI_MODEL, CODEX_ZAI_REASONING_EFFORT (low|high|max),
              ZAI_API_TIMEOUT_MS, CLAUDE_ZAI_CONFIG_DIR,
              CLAUDE_ZAI_{SONNET,OPUS,HAIKU}_MODEL
  OpenRouter: CODEX_OPENROUTER_MODEL, CODEX_OPENROUTER_REASONING_EFFORT
              (low|medium|high|xhigh|max), OPENROUTER_API_TIMEOUT_MS,
              CLAUDE_OPENROUTER_CONFIG_DIR,
              CLAUDE_OPENROUTER_{FABLE,SONNET,OPUS,HAIKU}_MODEL,
              CLAUDE_OPENROUTER_SUBAGENT_MODEL,
              CLAUDE_OPENROUTER_GATEWAY_DISCOVERY (0|1)
  Shared:     AI_BACKENDS_CONTAINER_MODE (auto|yes|no),
              AI_BACKENDS_CLAUDE_SUBPROCESS_ENV_SCRUB (auto|0|1; legacy
              CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB stays honored),
              AI_BACKENDS_ENV_FILE (explicit .env.local path)
"""


def print_help() -> None:
    print(HELP_TEXT, end="")


def main(argv: Optional[Sequence[str]] = None) -> int:
    arguments = list(sys.argv if argv is None else argv)
    load_env_local()
    invoked_as = os.path.basename(arguments[0]) if arguments else ""
    if invoked_as in LAUNCHER_NAMES:
        action = invoked_as
        rest: List[str] = arguments[1:]
    else:
        action = arguments[1] if len(arguments) > 1 else "help"
        rest = arguments[2:]
    handlers: Dict[str, Callable[[], None]] = {
        "install": install_backend_support,
        "codex-zai": lambda: launch_codex_zai(rest),
        "claude-zai": lambda: launch_claude_zai(rest),
        "codex-openrouter": lambda: launch_codex_openrouter(rest),
        "claude-openrouter": lambda: launch_claude_openrouter(rest),
        "help": print_help,
        "-h": print_help,
        "--help": print_help,
    }
    handler = handlers.get(action)
    if handler is None:
        die(f"Unknown action: {action}")
    handler()
    return 0


if __name__ == "__main__":
    sys.exit(main())
