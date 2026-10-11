#!/usr/bin/env python3
"""Builds the documentation site (typed Python port of ``build-docs.ps1``, issue #171).

Creates (or reuses) the ``.docs-venv`` virtual environment, installs the
docs requirements into it, and runs a strict mkdocs build — or the preview
server with ``--serve``. Verdicts, exit codes, and the ``[docs]`` message
shapes match the PowerShell original, so callers and CI logs keep their
meaning.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Optional, Sequence

_SCRIPTS_DIR = Path(__file__).resolve().parent


def resolve_repo_root(explicit_root: str) -> Path:
    """Resolve the repository root: the argument, else the parent of scripts/."""
    if explicit_root.strip():
        return Path(os.path.abspath(explicit_root))
    return _SCRIPTS_DIR.parent


def venv_python_path(venv_root: Path) -> Path:
    """Return the venv interpreter path for this platform."""
    relative = "Scripts/python.exe" if os.name == "nt" else "bin/python"
    return venv_root / relative


def invoke_checked(python_path: Path, arguments: Sequence[str], description: str) -> None:
    """Run a child python command, failing the build with the named description."""
    completed = subprocess.run([str(python_path), *arguments], check=False)
    if completed.returncode != 0:
        raise RuntimeError(f"{description} failed with exit code {completed.returncode}")


def resolve_python_launcher() -> Optional[str]:
    """Find a bootstrap interpreter (python3 or python) on PATH."""
    return shutil.which("python3") or shutil.which("python")


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="build-docs",
        description="Build the documentation site strictly, or serve it locally.",
    )
    parser.add_argument("--root", default="", help="repository root (default: the checkout holding this script)")
    parser.add_argument("--serve", action="store_true", help="run the mkdocs preview server instead of building")
    options = parser.parse_args(argv)

    try:
        repo_root = resolve_repo_root(options.root)
        if not repo_root.is_dir():
            print(f"[docs] ERROR: repository root '{repo_root}' does not exist")
            return 1
        requirements_path = repo_root / "requirements-docs.txt"
        config_path = repo_root / "mkdocs.yml"
        for required in (requirements_path, config_path):
            if not required.is_file():
                print(f"[docs] ERROR: required file '{required}' does not exist")
                return 1

        venv_root = repo_root / ".docs-venv"
        python_path = venv_python_path(venv_root)
        if not python_path.is_file():
            launcher = resolve_python_launcher()
            if launcher is None:
                print("[docs] ERROR: no python3 or python executable is on PATH")
                return 1
            print(f"[docs] creating the virtual environment at {venv_root}")
            invoke_checked(Path(launcher), ["-m", "venv", str(venv_root)], "python -m venv")
            python_path = venv_python_path(venv_root)
            if not python_path.is_file():
                print(f"[docs] ERROR: the virtual environment has no interpreter at {python_path}")
                return 1

        invoke_checked(
            python_path,
            [
                "-m",
                "pip",
                "install",
                "--quiet",
                "--disable-pip-version-check",
                "--requirement",
                str(requirements_path),
            ],
            "pip install -r requirements-docs.txt",
        )

        if options.serve:
            invoke_checked(python_path, ["-m", "mkdocs", "serve", "--config-file", str(config_path)], "mkdocs serve")
            return 0

        # --strict is repeated on the command line so the build stays strict
        # even if mkdocs.yml is later relaxed for the preview server. The
        # output directory stays owned by mkdocs.yml.
        invoke_checked(python_path, ["-m", "mkdocs", "build", "--strict", "--config-file", str(config_path)], "mkdocs build --strict")

        print("[docs] OK: strict build succeeded")
        return 0
    except RuntimeError as error:
        print(f"[docs] ERROR: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
