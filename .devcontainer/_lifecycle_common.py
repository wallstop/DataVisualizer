#!/usr/bin/env python3
"""Shared plumbing for the devcontainer lifecycle scripts (issue #171).

The lifecycle entry points (``on_create.py``, ``post_create.py``,
``post_start.py``, ``update_content.py``) are boot-path scripts: they run
inside the built container, where the Dockerfile guarantees ``python3``.
The helpers here keep the four entry points to their own steps: workspace
validation, the marker-delimited ``~/.bashrc`` block surgery, and child
process launches with the bash originals' success semantics.

The scripts stay standard library only and typed; ``uvx pyright`` validates
them with the same strict configuration as ``scripts/``.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
from pathlib import Path
from typing import Dict, List, Optional, Sequence

ENV_BLOCK_BEGIN = "# >>> DataVisualizer devcontainer env >>>"
ENV_BLOCK_END = "# <<< DataVisualizer devcontainer env <<<"


def resolve_workspace(argv: Sequence[str]) -> Path:
    """Return the workspace directory from argv[1], else the working directory.

    Mirrors the bash ``WORKSPACE_DIR="${1:-$(pwd)}"`` preamble: a missing
    directory prints the same failure line and exits 1 before any setup runs.
    """
    workspace = Path(argv[1]) if len(argv) > 1 else Path.cwd()
    if not workspace.is_dir():
        print(f"❌ Workspace not found: {workspace}", flush=True)
        raise SystemExit(1)
    return workspace


def run_child(command: Sequence[str], cwd: Path, env: Optional[Dict[str, str]] = None) -> int:
    """Run a child command, streaming its output, and return its exit code."""
    completed = subprocess.run(list(command), cwd=str(cwd), env=env, check=False)
    return completed.returncode


def run_child_strict(command: Sequence[str], cwd: Path, env: Optional[Dict[str, str]] = None) -> int:
    """Run a child command and exit with its code on failure (bash ``set -e``)."""
    code = run_child(command, cwd, env)
    if code != 0:
        raise SystemExit(code)
    return code


def read_bashrc_text(home: Path) -> str:
    """Read ``~/.bashrc`` bytes as text, or an empty string when absent.

    ``surrogateescape`` round-trips bytes that are not valid UTF-8 so a
    non-UTF-8 bashrc survives a rewrite without corruption.
    """
    bashrc = home / ".bashrc"
    if not bashrc.is_file():
        return ""
    return bashrc.read_bytes().decode("utf-8", errors="surrogateescape")


def write_bashrc_text(home: Path, text: str) -> None:
    """Replace ``~/.bashrc`` through a sibling temp file and ``os.replace``.

    The bash originals wrote an ``mktemp`` file and ``mv``-ed it over the
    bashrc: a crash cannot leave a truncated file, and the replacement keeps
    the temp file's 0600 mode, which is the visible permission outcome of the
    originals too.
    """
    bashrc = home / ".bashrc"
    handle, temp_name = tempfile.mkstemp(dir=str(home), prefix=".bashrc.")
    try:
        with os.fdopen(handle, "w", encoding="utf-8", errors="surrogateescape", newline="") as stream:
            stream.write(text)
        os.replace(temp_name, bashrc)
    except BaseException:
        try:
            os.unlink(temp_name)
        except OSError:
            pass
        raise


def awk_strip_env_block(text: str) -> str:
    """Apply the originals' awk filter: drop the marker block, keep every other record.

    An exact-line match on the begin marker starts skipping and the end marker
    stops it. Records are newline-terminated like awk's: the trailing-newline
    state of the input decides whether the final split fragment is a record,
    and every emitted record gains a terminating newline, so the output stays
    byte-faithful to what the awk filter wrote for every input shape.
    """
    lines = text.split("\n")
    trailing_is_artifact = text.endswith("\n") or text == ""
    records = lines[:-1] if trailing_is_artifact else lines
    kept: List[str] = []
    skipping = False
    for line in records:
        if line == ENV_BLOCK_BEGIN:
            skipping = True
            continue
        if line == ENV_BLOCK_END:
            skipping = False
            continue
        if not skipping:
            kept.append(line)
    if not kept:
        return ""
    return "".join(f"{line}\n" for line in kept)


def append_bashrc_block(home: Path, block_lines: Sequence[str]) -> None:
    """Append ``block_lines`` to ``~/.bashrc`` without touching other content.

    The originals appended with a leading ``echo ""`` so the block always
    starts on its own fresh line; the same leading newline is written here.
    """
    with open(home / ".bashrc", "a", encoding="utf-8", errors="surrogateescape", newline="") as stream:
        stream.write("\n" + "".join(f"{line}\n" for line in block_lines))


def bashrc_has_block_end(home: Path) -> bool:
    """Return whether ``~/.bashrc`` currently holds the block end marker."""
    return ENV_BLOCK_END in read_bashrc_text(home)


def repair_ownership(path: Path) -> None:
    """Chown a root-owned volume back to the current user when passwordless sudo works.

    Mirrors the originals' ``sudo -n chown -R "$(id -u):$(id -g)" ... 2>/dev/null || true``:
    the repair is best-effort, silent when sudo is absent or refuses, and never
    fails the lifecycle script.
    """
    sudo = shutil.which("sudo")
    if sudo is None:
        return
    subprocess.run(
        [sudo, "-n", "chown", "-R", f"{os.getuid()}:{os.getgid()}", str(path)],
        capture_output=True,
        check=False,
    )
