#!/usr/bin/env python3
"""Run the disposable benchmark driver over a local fixture/version matrix."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import Any, Iterable

from benchmark_driver import FIXTURE_SIZES, BenchmarkError, parse_fixture_size


DRIVER_PATH = Path(__file__).with_name("benchmark_driver.py").resolve()
DEFAULT_TIMEOUT_SECONDS = 7_200.0


def parse_sizes(value: str) -> tuple[int, ...]:
    sizes = tuple(parse_fixture_size(item.strip()) for item in value.split(","))
    if not sizes or len(set(sizes)) != len(sizes):
        raise argparse.ArgumentTypeError("fixture sizes must be unique and non-empty")
    return sizes


def build_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Run disposable Data Visualizer benchmarks over a matrix."
    )
    parser.add_argument("--host-project", required=True)
    parser.add_argument("--unity-version", action="append", required=True)
    parser.add_argument(
        "--sizes",
        type=parse_sizes,
        default=FIXTURE_SIZES,
        help="Comma-separated fixture sizes (default: 100,1000,10000,50000).",
    )
    parser.add_argument(
        "--suite", choices=("fixture", "play-entry", "all"), default="fixture"
    )
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--mode", choices=("direct", "mcp"), default="direct")
    parser.add_argument("--unity-path", default="unity")
    parser.add_argument("--mcp-url")
    parser.add_argument("--mcp-token")
    parser.add_argument("--timeout-seconds", type=float, default=DEFAULT_TIMEOUT_SECONDS)
    parser.add_argument("--keep-fixture", action="store_true")
    parser.add_argument("--path-map")
    parser.add_argument("--dry-run", action="store_true")
    return parser


def driver_command(args: argparse.Namespace, version: str, size: int) -> list[str]:
    command = [
        sys.executable,
        str(DRIVER_PATH),
        "--host-project",
        args.host_project,
        "--unity-version",
        version,
        "--fixture-size",
        str(size),
        "--suite",
        args.suite,
        "--output-dir",
        str(args.output_dir),
        "--mode",
        args.mode,
        "--unity-path",
        args.unity_path,
        "--timeout-seconds",
        str(args.timeout_seconds),
    ]
    if args.mcp_url:
        command.extend(["--mcp-url", args.mcp_url])
    if args.mcp_token:
        command.extend(["--mcp-token", args.mcp_token])
    if args.keep_fixture:
        command.append("--keep-fixture")
    if args.path_map:
        command.extend(["--path-map", args.path_map])
    return command


def matrix_paths(output_dir: Path, suite: str) -> tuple[Path, Path]:
    stem = f"data-visualizer-matrix-{suite}"
    return output_dir / f"{stem}.json", output_dir / f"{stem}.md"


def display_command(command: list[str]) -> list[str]:
    displayed: list[str] = []
    redact_next = False
    for item in command:
        if redact_next:
            displayed.append("REDACTED")
            redact_next = False
        else:
            displayed.append(item)
        if item == "--mcp-token":
            redact_next = True
    return displayed


def persist_matrix(output_dir: Path, suite: str, matrix: dict[str, Any]) -> None:
    json_path, markdown_path = matrix_paths(output_dir, suite)
    json_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.write_text(
        json.dumps(matrix, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    write_markdown(markdown_path, matrix)


def run_matrix(args: argparse.Namespace) -> dict[str, Any]:
    if args.timeout_seconds <= 0:
        raise BenchmarkError("--timeout-seconds must be positive")
    output_dir = args.output_dir.expanduser().resolve()
    runs: list[dict[str, Any]] = []
    matrix: dict[str, Any] = {
        "schemaVersion": "1",
        "status": "planned" if args.dry_run else "running",
        "hostProject": args.host_project,
        "mode": args.mode,
        "suite": args.suite,
        "unityVersions": args.unity_version,
        "fixtureSizes": list(args.sizes),
        "runs": runs,
        "unavailable": [
            "Unity versions not listed in --unity-version",
            "package-absent controls",
            "cold/warm restart measurements",
            "indexed-search latency",
            "render-frame and retained-reference memory measurements",
            "player compile/build smoke",
        ],
    }
    if not args.dry_run:
        persist_matrix(output_dir, args.suite, matrix)
    for version in args.unity_version:
        for size in args.sizes:
            command = driver_command(args, version, size)
            entry: dict[str, Any] = {
                "unityVersion": version,
                "fixtureSize": size,
                "command": display_command(command),
                "status": "planned",
            }
            if not args.dry_run:
                try:
                    completed = subprocess.run(command, check=False)
                    report = output_dir / f"data-visualizer-{args.suite}-{size}-{version}.json"
                    entry["returnCode"] = completed.returncode
                    entry["report"] = str(report)
                    if report.is_file():
                        result = json.loads(report.read_text(encoding="utf-8"))
                        entry["status"] = result.get("status", "unknown")
                        entry["fixtureVerified"] = result.get("fixtureVerified", False)
                        entry["cleanupCompleted"] = result.get("cleanupCompleted", False)
                    else:
                        entry["status"] = "missing-report"
                    if entry["returnCode"] != 0 or entry["status"] != "completed":
                        raise BenchmarkError(f"Matrix run failed: {entry}")
                    comparison = report.with_suffix(".md")
                    if comparison.is_file():
                        entry["comparison"] = str(comparison)
                except BenchmarkError as error:
                    entry["status"] = "failed"
                    entry["error"] = str(error)
                    runs.append(entry)
                    matrix["status"] = "failed"
                    matrix["error"] = str(error)
                    persist_matrix(output_dir, args.suite, matrix)
                    raise
                except (OSError, subprocess.SubprocessError, json.JSONDecodeError) as error:
                    entry["status"] = "failed"
                    entry["error"] = str(error)
                    runs.append(entry)
                    matrix["status"] = "failed"
                    matrix["error"] = str(error)
                    persist_matrix(output_dir, args.suite, matrix)
                    raise BenchmarkError(f"Matrix run failed: {entry}") from error
            runs.append(entry)
            if not args.dry_run:
                persist_matrix(output_dir, args.suite, matrix)
    if not args.dry_run:
        matrix["status"] = "completed"
        persist_matrix(output_dir, args.suite, matrix)
    return matrix


def write_markdown(path: Path, matrix: dict[str, Any]) -> None:
    lines = [
        "# Data Visualizer benchmark matrix",
        "",
        f"- Status: `{matrix['status']}`",
        f"- Mode: `{matrix['mode']}`",
        f"- Suite: `{matrix['suite']}`",
        f"- Unity versions: {', '.join(f'`{item}`' for item in matrix['unityVersions'])}",
        "",
        "| Unity version | Fixture size | Status | Fixture verified | Cleanup completed |",
        "| --- | ---: | --- | --- | --- |",
    ]
    for run in matrix["runs"]:
        lines.append(
            f"| `{run['unityVersion']}` | `{run['fixtureSize']:,}` | "
            f"`{run['status']}` | `{run.get('fixtureVerified', 'n/a')}` | "
            f"`{run.get('cleanupCompleted', 'n/a')}` |"
        )
    lines.extend(["", "## Unavailable in this harness", ""])
    lines.extend(f"- `{item}`" for item in matrix["unavailable"])
    lines.append("")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines), encoding="utf-8")


def main(argv: Iterable[str] | None = None) -> int:
    args = build_argument_parser().parse_args(list(argv) if argv is not None else None)
    try:
        matrix = run_matrix(args)
    except (BenchmarkError, OSError, subprocess.SubprocessError, json.JSONDecodeError) as error:
        print(f"benchmark matrix error: {error}", file=sys.stderr)
        return 2
    print(json.dumps(matrix, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
