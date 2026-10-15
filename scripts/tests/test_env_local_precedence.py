#!/usr/bin/env python3
"""env-local precedence contract tests (typed Python port of ``test-env-local-precedence.sh``, issue #171).

Every case from the bash original is kept with the same assertions; only
the driver language changed. The subject stays the sourced bash loader
(``env-local.sh``), so each case still resolves one variable through a
fresh bash that sources it, exactly like the bash suite's ``resolve``.

Background: a repaired GitHub PAT in .env.local had no effect because the
stale value was already exported into the container's long-lived process
environment, and the loader's "non-empty preexisting always wins" rule
pinned it there until a full rebuild. DATAVIZ_ENV_LOCAL_PRECEDENCE=file
inverts that rule for MCP credential names only.

These tests use synthetic placeholder values and never read the real
.env.local. The cases skip on hosts without bash (the subject is a bash
loader), printing a SKIP line like the previous wrapper did.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Dict, Optional

sys.dont_write_bytecode = True

from _test_helpers import assert_true, failure_count, new_temp_root, remove_temp_root, run_case

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
LOADER = REPO_ROOT / ".devcontainer" / "env-local.sh"


def bash_path(path: Path) -> str:
    """Render a path bash accepts on every host.

    Windows-created fixture paths reach Git Bash through MSYS, which reads
    drive-letter paths with forward slashes; backslashes would need quoting
    gymnastics inside the sourced loader, so the forward form is used
    everywhere (a no-op on POSIX).
    """
    return str(path).replace("\\", "/")

# Absolute so a case that overrides PATH cannot break the interpreter lookup,
# mirroring the bash suite's BASH_BIN.
BASE_PATH = os.environ.get("PATH", os.defpath)
HOME = os.environ.get("HOME") or "/home/vscode"


def assert_eq(description: str, expected: str, actual: str) -> None:
    assert_true(expected == actual, f"{description} (expected: [{expected}]; actual: [{actual}])")


def resolve(env_file: Path, variable: str, overrides: Optional[Dict[str, str]] = None) -> str:
    """Resolve one variable by sourcing the loader in a fresh bash.

    Each run gets its own minimal environment: the inherited environment is
    dropped (the bash original ran under ``env -i``) and only PATH, HOME, and
    the case's overrides are visible, so host-exported credentials can never
    leak into a fixture.
    """
    bash = shutil.which("bash")
    if bash is None:
        raise RuntimeError("bash must be on PATH; the loader subject is a bash script")
    env: Dict[str, str] = {"PATH": BASE_PATH, "HOME": HOME}
    if overrides:
        env.update(overrides)
    # ``printf %s "${!3}"`` prints the variable named by the third argument.
    # MSYS Bash can prepend its runtime paths at process startup. Set the
    # fixture PATH after startup so this tests the loader's precedence alone.
    script = 'PATH="$4"; . "$1"; load_env_local "$2"; printf %s "${!3}"'
    command = [bash, "-c", script, "_", bash_path(LOADER), bash_path(env_file), variable, env["PATH"]]
    completed = subprocess.run(
        command,
        env=env,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return completed.stdout.strip()


def case_fills_empty_preexisting_variable() -> None:
    fixture = new_temp_root(prefix="env-local-test-") / "fixture.env"
    fixture.write_text("TEST_TOKEN=from-file\n", encoding="utf-8", newline="")
    try:
        assert_eq(
            "an empty preexisting variable is filled from the file",
            "from-file",
            resolve(fixture, "TEST_TOKEN"),
        )
    finally:
        remove_temp_root(fixture.parent)


def case_nonempty_preexisting_variable_wins_by_default() -> None:
    fixture = new_temp_root(prefix="env-local-test-") / "fixture.env"
    fixture.write_text("TEST_TOKEN=from-file\n", encoding="utf-8", newline="")
    try:
        assert_eq(
            "a non-empty preexisting variable wins by default",
            "from-env",
            resolve(fixture, "TEST_TOKEN", {"TEST_TOKEN": "from-env"}),
        )
    finally:
        remove_temp_root(fixture.parent)


def case_path_is_never_overridden_under_file_precedence() -> None:
    # The inverted mode must stay scoped to MCP credentials. Overriding PATH
    # would break every agent CLI in the session.
    fixture = new_temp_root(prefix="env-local-test-") / "fixture.env"
    fixture.write_text("PATH=/evil/path\n", encoding="utf-8", newline="")
    try:
        assert_eq(
            "PATH is never overridden even when precedence=file",
            "from-env",
            resolve(fixture, "PATH", {"PATH": "from-env", "DATAVIZ_ENV_LOCAL_PRECEDENCE": "file"}),
        )
    finally:
        remove_temp_root(fixture.parent)


def case_github_pat_overridden_under_file_precedence() -> None:
    fixture = new_temp_root(prefix="env-local-test-") / "fixture.env"
    fixture.write_text("GITHUB_PERSONAL_ACCESS_TOKEN=repaired-in-file\n", encoding="utf-8", newline="")
    try:
        assert_eq(
            "GITHUB_PERSONAL_ACCESS_TOKEN takes the file value under precedence=file",
            "repaired-in-file",
            resolve(
                fixture,
                "GITHUB_PERSONAL_ACCESS_TOKEN",
                {"GITHUB_PERSONAL_ACCESS_TOKEN": "stale-in-env", "DATAVIZ_ENV_LOCAL_PRECEDENCE": "file"},
            ),
        )
    finally:
        remove_temp_root(fixture.parent)


def case_github_mcp_pat_overridden_under_file_precedence() -> None:
    fixture = new_temp_root(prefix="env-local-test-") / "fixture.env"
    fixture.write_text("GITHUB_MCP_PAT=repaired-in-file\n", encoding="utf-8", newline="")
    try:
        assert_eq(
            "GITHUB_MCP_PAT takes the file value under precedence=file",
            "repaired-in-file",
            resolve(
                fixture,
                "GITHUB_MCP_PAT",
                {"GITHUB_MCP_PAT": "stale-in-env", "DATAVIZ_ENV_LOCAL_PRECEDENCE": "file"},
            ),
        )
    finally:
        remove_temp_root(fixture.parent)


def case_zai_key_overridden_under_file_precedence() -> None:
    fixture = new_temp_root(prefix="env-local-test-") / "fixture.env"
    fixture.write_text("ZAI_API_KEY=repaired-in-file\n", encoding="utf-8", newline="")
    try:
        assert_eq(
            "ZAI_API_KEY takes the file value under precedence=file",
            "repaired-in-file",
            resolve(
                fixture,
                "ZAI_API_KEY",
                {"ZAI_API_KEY": "stale-in-env", "DATAVIZ_ENV_LOCAL_PRECEDENCE": "file"},
            ),
        )
    finally:
        remove_temp_root(fixture.parent)


def case_unmanaged_variable_not_overridden_under_file_precedence() -> None:
    # Scope guard: precedence=file must not turn the loader into a general
    # "the file wins" mode for arbitrary variables.
    fixture = new_temp_root(prefix="env-local-test-") / "fixture.env"
    fixture.write_text("SOME_OTHER_SETTING=from-file\n", encoding="utf-8", newline="")
    try:
        assert_eq(
            "an unmanaged variable keeps the preexisting value under precedence=file",
            "from-env",
            resolve(
                fixture,
                "SOME_OTHER_SETTING",
                {"SOME_OTHER_SETTING": "from-env", "DATAVIZ_ENV_LOCAL_PRECEDENCE": "file"},
            ),
        )
    finally:
        remove_temp_root(fixture.parent)


def case_repeated_load_is_idempotent() -> None:
    fixture = new_temp_root(prefix="env-local-test-") / "fixture.env"
    fixture.write_text("GITHUB_PERSONAL_ACCESS_TOKEN=value-in-file\n", encoding="utf-8", newline="")
    try:
        once = resolve(
            fixture,
            "GITHUB_PERSONAL_ACCESS_TOKEN",
            {"GITHUB_PERSONAL_ACCESS_TOKEN": "stale", "DATAVIZ_ENV_LOCAL_PRECEDENCE": "file"},
        )
        bash = shutil.which("bash")
        if bash is None:
            raise RuntimeError("bash must be on PATH; the loader subject is a bash script")
        script = '. "$1"; load_env_local "$2"; load_env_local "$2"; printf %s "${!3}"'
        completed = subprocess.run(
            [
                bash,
                "-c",
                script,
                "_",
                bash_path(LOADER),
                bash_path(fixture),
                "GITHUB_PERSONAL_ACCESS_TOKEN",
            ],
            env={
                "PATH": BASE_PATH,
                "HOME": HOME,
                "GITHUB_PERSONAL_ACCESS_TOKEN": once,
                "DATAVIZ_ENV_LOCAL_PRECEDENCE": "file",
            },
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        twice = completed.stdout.strip()
        assert_eq("loading twice yields the same value as loading once", once, twice)
    finally:
        remove_temp_root(fixture.parent)


def case_missing_file_is_not_an_error() -> None:
    assert_eq(
        "a missing .env.local leaves the environment untouched",
        "from-env",
        resolve(Path("/nonexistent/.env.local"), "TEST_TOKEN", {"TEST_TOKEN": "from-env"}),
    )


CASES = (
    case_fills_empty_preexisting_variable,
    case_nonempty_preexisting_variable_wins_by_default,
    case_path_is_never_overridden_under_file_precedence,
    case_github_pat_overridden_under_file_precedence,
    case_github_mcp_pat_overridden_under_file_precedence,
    case_zai_key_overridden_under_file_precedence,
    case_unmanaged_variable_not_overridden_under_file_precedence,
    case_repeated_load_is_idempotent,
    case_missing_file_is_not_an_error,
)


def skip_guard() -> bool:
    """Report whether the suite can run: the subject is a sourced bash loader."""
    return shutil.which("bash") is not None


def main() -> int:
    assert_true(LOADER.is_file(), f"loader not found at {LOADER}")
    print("== env-local precedence contract tests ==")
    if not skip_guard():
        print("    SKIP: bash not found on PATH; env-local precedence tests require a POSIX shell.")
        return 0
    for case in CASES:
        run_case(case.__name__, case)
    failures = failure_count()
    print(f"== env-local precedence: {failures} failure(s) ==")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
