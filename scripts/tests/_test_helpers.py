#!/usr/bin/env python3
"""Shared helpers for the typed-Python harness self-tests (issue #171).

Typed Python port of ``TestHelpers.ps1``: the same temp-root lifecycle,
fixture writers, case runner, and interpreter resolution with the same
output shapes (``PASS:``/``FAIL:`` lines and the per-file failure summary),
so the suite runner's replayed log stays stable across the migration. The
PowerShell original retired with the last ``test-*.ps1`` port; this module
is the helpers' single owner now.

The leading underscore keeps this module outside the runner's ``test_*.py``
discovery: helper modules never match the test-file pattern by design, so
the suite can never try to execute one.
"""

from __future__ import annotations

import os
import shutil
import stat
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path
from typing import Any, Callable, Sequence, Tuple

_failure_count_value = 0


def _make_writable_and_retry(function: Callable[[str], None], error_path: str, _exc: Any) -> None:
    """Clear the read-only bit and retry one deletion.

    Git marks its object files read-only on Windows, so a recursive delete
    of a fixture ``.git`` directory dies with ``PermissionError`` unless the
    read-only bit is cleared first, which is what the PowerShell original's
    ``Remove-Item -Recurse -Force`` did implicitly.
    """
    os.chmod(error_path, stat.S_IWRITE)
    function(error_path)


def _rmtree_force(path: Path) -> None:
    """Delete a tree even when it holds read-only files, on every supported Python."""
    if sys.version_info >= (3, 12):
        shutil.rmtree(path, onexc=_make_writable_and_retry)
    else:
        shutil.rmtree(path, onerror=_make_writable_and_retry)


def new_temp_root(prefix: str = "llm-selftest-") -> Path:
    """Create a unique temporary directory under the system temp path."""
    path = Path(tempfile.gettempdir()) / f"{prefix}{uuid.uuid4().hex}"
    path.mkdir(parents=True)
    return path


def remove_temp_root(path: Path) -> None:
    """Delete a temp root, reporting a missing path instead of no-oping.

    A missing path means the caller handed cleanup a child that was never
    created (a derived path instead of the created root), which would
    silently leak the real root; say so instead of no-oping.
    """
    if path.exists():
        _rmtree_force(path)
        return
    print(
        "[test_helpers] WARNING: remove_temp_root called for a path that does not exist; "
        f"the created root may be leaking: {path}"
    )


def write_fixture_file(root: Path, relative_path: str, content: str) -> None:
    """Write a UTF-8 no-BOM fixture file, creating parent directories."""
    full_path = root / relative_path
    full_path.parent.mkdir(parents=True, exist_ok=True)
    # newline="" keeps the bytes verbatim on every platform: several cases
    # pin line-ending behavior, so no implicit newline translation may run.
    with open(full_path, "w", encoding="utf-8", newline="") as handle:
        handle.write(content)


def fixture_path(root: Path, relative_path: str) -> Path:
    """Join a fixture root and a repository-relative path."""
    return root / relative_path


def fixture_lines(count: int) -> str:
    """Build ``line-N`` content with exactly ``count`` newline-terminated lines."""
    if count <= 0:
        return ""
    return "".join(f"line-{number}\n" for number in range(1, count + 1))


def failure_count() -> int:
    """Return how many cases failed in this process."""
    return _failure_count_value


def run_case(name: str, body: Callable[[], None]) -> bool:
    """Run one test case, printing the shared PASS/FAIL line shapes.

    Every exception becomes a recorded failure with its message; only an
    escaping ``SystemExit`` (a body that exits the process) can bypass the
    runner, matching the PowerShell original where ``exit`` leaves the
    whole child.
    """
    global _failure_count_value
    try:
        body()
    except Exception as error:  # the case runner must convert every failure into a record
        print(f"  FAIL: {name} :: {error}")
        _failure_count_value += 1
        return False
    print(f"  PASS: {name}")
    return True


def assert_true(condition: object, message: str) -> None:
    """Raise ``AssertionError`` unless ``condition`` is truthy."""
    if not condition:
        raise AssertionError(message)


def assert_exit_code(expected: int, description: str, actual: int) -> None:
    """Raise unless the last child exited with ``expected``."""
    if actual != expected:
        raise AssertionError(f"{description} (expected exit {expected}, got {actual})")


def find_python() -> str:
    """Resolve a working Python 3 interpreter for driving the typed scripts.

    The typed Python harness scripts (issue #171) need any working Python 3
    interpreter; the self-tests only exercise modes that execute no
    toolchain.
    """
    candidates = ["python", "python3"] if sys.platform == "win32" else ["python3", "python"]
    for candidate in candidates:
        resolved = shutil.which(candidate)
        if resolved is None:
            continue
        completed = subprocess.run(
            [resolved, "--version"],
            capture_output=True,
            text=True,
            check=False,
        )
        if completed.returncode == 0:
            return resolved
    raise RuntimeError("the harness self-tests require a working Python 3 interpreter (python3 or python)")


def find_node() -> str:
    """Resolve the node runtime for driving the release pipeline scripts.

    The release pipeline scripts (issue #171's remaining surface) are still
    node programs while their self-tests port to typed Python; the tests
    only exercise local modes that execute no registry or network work.
    """
    resolved = shutil.which("node")
    if resolved is None:
        raise RuntimeError("the release self-tests require node on PATH")
    return resolved


def run_tool_output(command: Sequence[str]) -> Tuple[str, int]:
    """Run a child tool with all output merged, returning ``(output, exit code)``.

    This mirrors the PowerShell originals' ``*>&1 | Out-String``: stdout and
    stderr land in one string the assertions search, and the exit code
    travels beside it. Line endings stay as the child wrote them, so
    ``\\r?\\n``-tolerant patterns keep working on every platform.
    """
    completed = subprocess.run(
        list(command),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return ((completed.stdout or "") + (completed.stderr or "")), completed.returncode


def new_llm_fixture_root(package_version: str = "1.2.3") -> Path:
    """Build the standard valid fixture tree the instructions lint expects."""
    root = new_temp_root(prefix="llm-fixture-")
    context_link = "](./.llm/context.md)"
    context_link_nested = "](../.llm/context.md)"

    context = "\n".join(
        [
            "# LLM Agent Instructions",
            "",
            "Procedural skills are in the [skills/](./skills/) directory.",
            "",
            "## Repository Overview",
            "",
            "- **Package**: com.test.fixture",
            f"- **Version**: {package_version}",
            "",
            "## Skills Reference",
            "",
            "See the generated [Skills Index](./skills/index.md).",
        ]
    )
    write_fixture_file(root, ".llm/context.md", context + "\n")
    write_fixture_file(root, "package.json", '{"name":"com.test.fixture","version":"' + package_version + '"}')

    pointer_content = "\n".join(
        [
            "# Repository Guidelines",
            "",
            f"See the [AI Agent Guidelines]({context_link}) for all AI agent guidelines.",
        ]
    )
    pointer_content_nested = "\n".join(
        [
            "# GitHub Copilot Instructions",
            "",
            f"See the [AI Agent Guidelines]({context_link_nested}) for all AI agent guidelines.",
        ]
    )

    for pointer in ["AGENTS.md", "CLAUDE.md", ".cursorrules", ".windsurfrules"]:
        write_fixture_file(root, pointer, pointer_content + "\n")
    write_fixture_file(root, ".github/copilot-instructions.md", pointer_content_nested + "\n")

    skill_alpha = "\n".join(
        [
            "---",
            "name: alpha-skill",
            "description: Core skill used by the fixture. Use when testing core behavior.",
            "metadata:",
            "  category: Core",
            "---",
            "",
            "# Skill: Alpha Skill",
            "",
            "Body.",
        ]
    )
    skill_zulu = "\n".join(
        [
            "---",
            "name: zulu-skill",
            "description: Feature skill used by the fixture. Use when testing feature behavior.",
            "metadata:",
            "  category: Feature",
            "---",
            "",
            "# Skill: Zulu Skill",
            "",
            "Body.",
        ]
    )
    write_fixture_file(root, ".llm/skills/alpha-skill/SKILL.md", skill_alpha + "\n")
    write_fixture_file(root, ".llm/skills/zulu-skill/SKILL.md", skill_zulu + "\n")

    return root
