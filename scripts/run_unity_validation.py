#!/usr/bin/env python3
"""Runs one local Unity test suite and records reproducible invocation evidence.

Typed Python port of ``run-unity-validation.ps1`` (issue #171): the same
validation order, manifest contract, exit codes, and output policy, driven
by the standard library. This direct-host runner is the first
validation-harness slice for issue #21. Run it on the machine that owns
both the Unity executable and host project. Fixture size is recorded and
forwarded for fixture-aware test entrypoints; fixture generation is a
separate task. Output is intentionally rejected when it is inside this
package repository.

Example:
    uv run scripts/run_unity_validation.py \\
        --host-project /path/to/host-project \\
        --unity-executable /path/to/Unity \\
        --unity-version 6000.4.6f1 \\
        --fixture-size 100 \\
        --suite editmode \\
        --output-directory /tmp/data-visualizer-validation

    uv run scripts/run_unity_validation.py \\
        --host-project C:/Code/DataVisualizerHost \\
        --unity-executable C:/Unity/Editor/Unity.exe \\
        --unity-version 2022.3.50f1 \\
        --fixture-size 10000 \\
        --suite playmode \\
        --output-directory C:/Temp/DataVisualizerValidation \\
        --plan-only
"""

from __future__ import annotations

import argparse
import json
import os
import re
import signal
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, Optional, Sequence

REPO_ROOT = Path(__file__).resolve().parent.parent

FIXTURE_SIZES = (100, 1000, 10000, 50000)
SUITES = ("editmode", "playmode")


class _ValidationFailure(Exception):
    """A rejected run: the message goes to stderr and the run exits 2."""


def _resolve_validation_path(path: str, description: str, must_exist: bool = False) -> Path:
    if not path or not path.strip():
        raise _ValidationFailure(f"{description} must not be empty.")
    resolved = Path(os.path.abspath(path))
    if must_exist and not resolved.exists():
        raise _ValidationFailure(f"{description} does not exist: {resolved}")
    return resolved


def _identity(value: str) -> str:
    return value


def _path_within(candidate: Path, parent: Path) -> bool:
    # Windows compares paths case-insensitively; every other platform is
    # ordinal. normcase is the standard-library spelling of that split and
    # also unifies the separator on Windows.
    normalize = os.path.normcase if sys.platform == "win32" else _identity
    separators = os.sep + (os.altsep or "")
    parent_with_separator = str(parent).rstrip(separators) + os.sep
    candidate_text = str(candidate)
    parent_text = str(parent)
    return normalize(candidate_text) == normalize(parent_text) or normalize(candidate_text).startswith(
        normalize(parent_with_separator)
    )


def _write_run_manifest(manifest: Dict[str, object], path: Path) -> None:
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(json.dumps(manifest, indent=2) + os.linesep)


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="run-unity-validation",
        description="Run one local Unity test suite and record reproducible invocation evidence.",
    )
    parser.add_argument("--host-project", required=True, help="Unity host project directory")
    parser.add_argument("--unity-executable", required=True, help="Unity editor executable")
    parser.add_argument("--unity-version", required=True, help="Unity version the host project must declare")
    parser.add_argument("--fixture-size", required=True, type=int, choices=FIXTURE_SIZES, help="Fixture asset count")
    parser.add_argument("--suite", required=True, choices=SUITES, help="Test suite to run")
    parser.add_argument("--output-directory", required=True, help="Evidence output directory, outside this repository")
    parser.add_argument(
        "--timeout-seconds",
        type=_validate_timeout,
        default=900,
        help="Unity run timeout in seconds (1-86400)",
    )
    parser.add_argument("--plan-only", action="store_true", help="Write the plan manifest without running Unity")
    return parser


def _validate_timeout(value: str) -> int:
    try:
        timeout = int(value)
    except ValueError:
        raise argparse.ArgumentTypeError(f"invalid int value: '{value}'") from None
    if timeout < 1 or timeout > 86400:
        raise argparse.ArgumentTypeError("timeout must be between 1 and 86400 seconds")
    return timeout


def run(options: argparse.Namespace) -> int:
    manifest: Optional[Dict[str, object]] = None
    manifest_path: Optional[Path] = None
    try:
        repo_root = _resolve_validation_path(str(REPO_ROOT), "Repository root", must_exist=True)
        project_path = _resolve_validation_path(options.host_project, "Host project", must_exist=True)
        unity_path = _resolve_validation_path(options.unity_executable, "Unity executable", must_exist=True)
        if not unity_path.is_file():
            raise _ValidationFailure(f"Unity executable is not a file: {unity_path}")

        assets_path = project_path / "Assets"
        project_version_path = project_path / "ProjectSettings/ProjectVersion.txt"
        if not assets_path.is_dir():
            raise _ValidationFailure(f"Host project is missing its Assets directory: {assets_path}")
        if not project_version_path.is_file():
            raise _ValidationFailure("Host project is missing ProjectSettings/ProjectVersion.txt.")

        version_text = project_version_path.read_text(encoding="utf-8-sig")
        version_match = re.search(r"(?m)^m_EditorVersion:\s*(\S+)\s*$", version_text)
        if version_match is None:
            raise _ValidationFailure("Host project does not declare m_EditorVersion.")
        actual_unity_version = version_match.group(1)
        if actual_unity_version != options.unity_version:
            raise _ValidationFailure(
                f"Requested Unity {options.unity_version}, but the host project declares {actual_unity_version}."
            )

        output_path = _resolve_validation_path(options.output_directory, "Output directory")
        if _path_within(output_path, repo_root):
            raise _ValidationFailure(f"Output directory must be outside the package repository: {repo_root}")
        output_path.mkdir(parents=True, exist_ok=True)

        manifest_path = output_path / "data-visualizer-validation.json"
        results_path = output_path / f"{options.suite}-results.xml"
        log_path = output_path / f"{options.suite}-unity.log"
        unity_arguments = [
            "-batchmode",
            "-quit",
            "-projectPath", str(project_path),
            "-runTests",
            "-testPlatform", options.suite,
            "-testResults", str(results_path),
            "-logFile", str(log_path),
            "-dataVisualizerFixtureSize", str(options.fixture_size),
        ]
        manifest = {
            "schemaVersion": 1,
            "status": "planned" if options.plan_only else "running",
            "startedAtUtc": datetime.now(timezone.utc).isoformat(),
            "completedAtUtc": None,
            "exitCode": None,
            "configuration": {
                "hostProject": str(project_path),
                "unityExecutable": str(unity_path),
                "unityVersion": options.unity_version,
                "fixtureSize": options.fixture_size,
                "suite": options.suite,
                "outputDirectory": str(output_path),
                "timeoutSeconds": options.timeout_seconds,
            },
            "command": [str(unity_path), *unity_arguments],
            "artifacts": {
                "testResults": str(results_path),
                "unityLog": str(log_path),
            },
        }
        _write_run_manifest(manifest, manifest_path)

        if options.plan_only:
            print(f"Validation plan written to {manifest_path}")
            return 0

        # start_new_session detaches the child into its own process group so a
        # timeout can kill the whole Unity tree the way the PowerShell
        # original's Kill($true) did.
        process = subprocess.Popen([str(unity_path), *unity_arguments], start_new_session=sys.platform != "win32")
        child_exit: Optional[int] = None
        timed_out = False
        try:
            child_exit = process.wait(timeout=options.timeout_seconds)
        except subprocess.TimeoutExpired:
            timed_out = True
            if sys.platform == "win32":
                process.kill()
            else:
                os.killpg(os.getpgid(process.pid), signal.SIGKILL)
            process.wait()
        manifest["completedAtUtc"] = datetime.now(timezone.utc).isoformat()
        if timed_out or child_exit is None:
            manifest["status"] = "timedOut"
            manifest["exitCode"] = 124
            return_code = 124
        else:
            manifest["exitCode"] = child_exit
            manifest["status"] = "passed" if child_exit == 0 else "failed"
            return_code = child_exit
        _write_run_manifest(manifest, manifest_path)
        return return_code
    except Exception as error:  # the catch must turn every failure into exit 2
        if manifest is not None and manifest_path is not None:
            manifest["status"] = "failed"
            manifest["exitCode"] = 2
            manifest["completedAtUtc"] = datetime.now(timezone.utc).isoformat()
            _write_run_manifest(manifest, manifest_path)
        print(str(error), file=sys.stderr)
        return 2


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = _build_parser().parse_args(argv)
    return run(options)


if __name__ == "__main__":
    sys.exit(main())
