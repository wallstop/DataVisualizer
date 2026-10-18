#!/usr/bin/env python3
"""env-local precedence contract tests (typed Python port of ``test-env-local-precedence.sh``, issue #171).

The suite compares the typed Python loader with the sourced Bash loader
(``env-local.sh``). Existing precedence cases remain, and direct Python
cases run even when Bash is unavailable.

Background: a repaired GitHub PAT in .env.local had no effect because the
stale value was already exported into the container's long-lived process
environment, and the loader's "non-empty preexisting always wins" rule
pinned it there until a full rebuild. DATAVIZ_ENV_LOCAL_PRECEDENCE=file
inverts that rule for MCP credential names only.

These tests use synthetic placeholder values and never read the real
.env.local. Shell-equivalence cases skip when Bash is unavailable.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Dict, List, Optional, Tuple
from unittest.mock import patch

sys.dont_write_bytecode = True

from _test_helpers import assert_true, failure_count, new_temp_root, remove_temp_root, run_case

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
LOADER = REPO_ROOT / ".devcontainer" / "env-local.sh"
sys.path.insert(0, str(LOADER.parent))
from env_local import load_environment


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
UV_BIN = (shutil.which('uv') or 'uv').replace('\\', '/')


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
    env: Dict[str, str] = {"PATH": BASE_PATH, "HOME": HOME, "DATAVIZ_ENV_UV": UV_BIN}
    if overrides:
        env.update(overrides)
    # ``printf %s "${!3}"`` prints the variable named by the third argument.
    # MSYS Bash can prepend its runtime paths at process startup. Set the
    # fixture PATH after startup so this tests the loader's precedence alone.
    script = 'PATH="$4"; . "$1"; load_env_local "$2" || exit; printf %s "${!3}"'
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
    assert_true(completed.returncode == 0, f"the sourced loader must succeed: {completed.stderr}")
    actual = completed.stdout
    direct = load_environment(env_file, env).get(variable, '')
    assert_eq('Python and sourced-shell loaders agree', actual, direct)
    return actual


def case_fills_empty_preexisting_variable() -> None:
    fixture = new_temp_root(prefix="env-local-test-") / "fixture.env"
    fixture.write_bytes("TEST_TOKEN=from-file\n".encode("utf-8"))
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
    fixture.write_bytes("TEST_TOKEN=from-file\n".encode("utf-8"))
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
    fixture.write_bytes("PATH=/evil/path\n".encode("utf-8"))
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
    fixture.write_bytes("GITHUB_PERSONAL_ACCESS_TOKEN=repaired-in-file\n".encode("utf-8"))
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
    fixture.write_bytes("GITHUB_MCP_PAT=repaired-in-file\n".encode("utf-8"))
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
    fixture.write_bytes("ZAI_API_KEY=repaired-in-file\n".encode("utf-8"))
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
    fixture.write_bytes("SOME_OTHER_SETTING=from-file\n".encode("utf-8"))
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
    fixture.write_bytes("GITHUB_PERSONAL_ACCESS_TOKEN=value-in-file\n".encode("utf-8"))
    try:
        once = resolve(
            fixture,
            "GITHUB_PERSONAL_ACCESS_TOKEN",
            {"GITHUB_PERSONAL_ACCESS_TOKEN": "stale", "DATAVIZ_ENV_LOCAL_PRECEDENCE": "file"},
        )
        bash = shutil.which("bash")
        if bash is None:
            raise RuntimeError("bash must be on PATH; the loader subject is a bash script")
        script = '. "$1"; load_env_local "$2" || exit; load_env_local "$2" || exit; printf %s "${!3}"'
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


def case_python_loader_preserves_literal_formats_and_precedence() -> None:
    from post_start import child_env_with_env_local
    from recycle_stale_opencode_service import read_env_file_values
    fixture = new_temp_root(prefix='env-python-') / '.env.local'
    cases: List[Tuple[str, Dict[str, str], str, str]] = [
        ('\ufeff export TOKEN = " spaced = value "\r\n', {}, 'TOKEN', ' spaced = value '),
        ("TOKEN='$(never-run) `never-run` $HOME # literal'\n", {}, 'TOKEN', '$(never-run) `never-run` $HOME # literal'),
        ('TOKEN=first\nTOKEN=second\n', {}, 'TOKEN', 'first'),
        ('TOKEN=\nTOKEN=second', {}, 'TOKEN', 'second'),
        ('ZAI_API_KEY=first\nZAI_API_KEY=second', {'DATAVIZ_ENV_LOCAL_PRECEDENCE': 'file'}, 'ZAI_API_KEY', 'second'),
        ('DATAVIZ_ENV_LOCAL_PRECEDENCE=file\nZAI_API_KEY=repaired', {'ZAI_API_KEY': 'stale'}, 'ZAI_API_KEY', 'repaired'),
        ('# ignored\ninvalid-key=bad\nTOKEN=kept\n', {}, 'TOKEN', 'kept'),
    ]
    try:
        for content, environment, key, expected in cases:
            fixture.write_bytes(content.encode('utf-8'))
            before = dict(environment)
            result = load_environment(fixture, environment)
            assert_eq('literal Python loader result', expected, result.get(key, ''))
            assert_true(environment == before, 'loading must not mutate the caller environment')
            if skip_guard():
                assert_eq('format agrees with sourced shell', expected, resolve(fixture, key, environment))
        fixture.write_text('GITHUB_MCP_PAT=synthetic-file-value\n', encoding='utf-8')
        with patch.dict(os.environ, {'GITHUB_MCP_PAT': 'synthetic-stale-value'}, clear=True):
            assert_eq('startup loads without a shell', 'synthetic-file-value', child_env_with_env_local(fixture.parent)['GITHUB_MCP_PAT'])
            assert_true(read_env_file_values(fixture) == {'GITHUB_MCP_PAT': 'synthetic-file-value'}, 'recycler reads only file values')
        with patch.object(Path, 'read_bytes', side_effect=PermissionError('synthetic read failure')):
            assert_true(read_env_file_values(fixture) == {}, 'unreadable credentials must not judge a service')
        fixture.unlink()
        assert_true(load_environment(fixture, {'TOKEN': 'keep'}) == {'TOKEN': 'keep'}, 'a missing file leaves inherited values intact')
    finally:
        remove_temp_root(fixture.parent)


def case_shell_adapter_preserves_values_and_applies_exports_atomically() -> None:
    bash = shutil.which('bash')
    if bash is None:
        return
    fixture = new_temp_root(prefix='env-adapter-') / 'fixture.env'
    environment = {'PATH': BASE_PATH, 'HOME': HOME, 'DATAVIZ_ENV_UV': UV_BIN}
    script = '. "$1"; load_env_local "$2"; result=$?; printf "%s:%s" "$result" "${TOKEN-unset}"'
    try:
        cases = [
            (b'TOKEN="$(exit 8) `exit 9` $HOME # literal"\r\n', '0:$(exit 8) `exit 9` $HOME # literal'),
            (b'TOKEN=" trailing spaces "\n', '0: trailing spaces '),
            (b'TOKEN=valid\nOTHER=invalid\0value\n', '1:unset'),
        ]
        for content, expected in cases:
            fixture.write_bytes(content)
            result = subprocess.run(
                [bash, '-c', script, '_', bash_path(LOADER), bash_path(fixture)],
                env=environment, capture_output=True, text=True, encoding='utf-8', check=False,
            )
            assert_eq('shell adapter result', expected, result.stdout)
            assert_true('invalid' not in result.stderr, 'failure diagnostics must not contain file values')
        fixture.write_text('TOKEN=from-file\n', encoding='utf-8')
        environment['DATAVIZ_ENV_UV'] = bash_path(fixture.parent / 'missing-uv')
        result = subprocess.run(
            [bash, '-c', script, '_', bash_path(LOADER), bash_path(fixture)],
            env=environment, capture_output=True, text=True, encoding='utf-8', check=False,
        )
        assert_eq('a missing launcher fails without exports', '1:unset', result.stdout)
    finally:
        remove_temp_root(fixture.parent)


def case_shell_adapter_preserves_unexported_values_and_file_selection() -> None:
    bash = shutil.which('bash')
    if bash is None:
        return
    fixture = new_temp_root(prefix='env-shell-')
    workspace = fixture / 'workspace'
    workspace.mkdir()
    explicit = fixture / 'explicit.env'
    explicit.write_text('TOKEN=explicit\nGITHUB_MCP_PAT=rotated\n', encoding='utf-8')
    (workspace / '.env.local').write_text('TOKEN=workspace\n', encoding='utf-8')
    (fixture / '.env.local').write_text('TOKEN=cwd\n', encoding='utf-8')
    environment = {'PATH': BASE_PATH, 'HOME': HOME, 'DATAVIZ_ENV_UV': UV_BIN}
    cases: List[Tuple[str, Dict[str, str], str]] = [
        ('load_env_local "$2"', {}, 'explicit'),
        ('load_env_local', {'AI_BACKENDS_ENV_FILE': bash_path(explicit)}, 'explicit'),
        ('load_env_local', {'WORKSPACE_ROOT': bash_path(workspace)}, 'workspace'),
        ('load_env_local', {}, 'cwd'),
        ('TOKEN=unexported; load_env_local "$2"', {}, 'unexported'),
        ('TOKEN=; load_env_local "$2"', {}, 'explicit'),
        ('GITHUB_MCP_PAT=stale; DATAVIZ_ENV_LOCAL_PRECEDENCE=file; load_env_local "$2"; TOKEN=$GITHUB_MCP_PAT', {}, 'rotated'),
    ]
    try:
        for command, overrides, expected in cases:
            script = '. "$1"; ' + command + ' || exit; printf %s "$TOKEN"'
            result = subprocess.run(
                [bash, '-c', script, '_', bash_path(LOADER), bash_path(explicit)],
                env={**environment, **overrides}, cwd=fixture, capture_output=True, text=True, encoding='utf-8', check=False,
            )
            assert_true(result.returncode == 0, f'loader must succeed: {result.stderr}')
            assert_eq('shell values and file selection', expected, result.stdout)
    finally:
        remove_temp_root(fixture)


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
    case_shell_adapter_preserves_values_and_applies_exports_atomically,
    case_shell_adapter_preserves_unexported_values_and_file_selection,
)


def skip_guard() -> bool:
    """Report whether shell-equivalence cases can run."""
    return shutil.which("bash") is not None


def main() -> int:
    assert_true(LOADER.is_file(), f"loader not found at {LOADER}")
    print("== env-local precedence contract tests ==")
    run_case(case_python_loader_preserves_literal_formats_and_precedence.__name__, case_python_loader_preserves_literal_formats_and_precedence)
    if not skip_guard():
        print("    SKIP: bash not found on PATH; skipping shell-equivalence cases.")
        return 1 if failure_count() else 0
    for case in CASES:
        run_case(case.__name__, case)
    failures = failure_count()
    print(f"== env-local precedence: {failures} failure(s) ==")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
