#!/usr/bin/env python3
"""One-time same-runner CI comparison; removed before PR handoff."""
from __future__ import annotations

import argparse
import contextlib
import json
import os
import platform
import statistics
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Generator


def checked(arguments: list[str], root: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(arguments, cwd=root, capture_output=True, text=True, encoding="utf-8", errors="replace", check=True)


@contextlib.contextmanager
def baseline_tree(root: Path, revision: str) -> Generator[Path, None, None]:
    with tempfile.TemporaryDirectory(prefix="dv-ci-compare-", dir=root.parent) as directory:
        target = Path(directory) / "baseline"
        checked(["git", "worktree", "add", "--detach", str(target), revision], root)
        try:
            yield target
        finally:
            checked(["git", "worktree", "remove", "--force", str(target)], root)


def measure(root: Path, label: str, sample: int) -> dict[str, object]:
    started = time.perf_counter()
    durations: dict[str, float] = {}
    for name, script in (("lints", "scripts/run_ci_lints.py"), ("tests", "scripts/tests/run_all.py")):
        step_started = time.perf_counter()
        arguments = ["uv", "run", script]
        completed = subprocess.run(arguments, cwd=root, capture_output=True, text=True, encoding="utf-8", errors="replace", check=False)
        durations[name] = round(time.perf_counter() - step_started, 3)
        print(f"=== {label} sample {sample}: {name} ===\n{completed.stdout}\n{completed.stderr}", flush=True)
        if completed.returncode != 0:
            raise RuntimeError(f"{label} sample {sample}: {name} failed ({completed.returncode})")
        if name == "tests" and "Self-tests: all 39 test file(s) passed." not in completed.stdout:
            raise RuntimeError(f"{label} sample {sample}: full 39-file verdict absent")
    row: dict[str, object] = {"label": label, "sample": sample, "seconds": round(time.perf_counter() - started, 3), "steps": durations, "files": 39}
    print("CI_COMPARISON_SAMPLE " + json.dumps(row), flush=True)
    return row


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    options = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    probe = subprocess.run(["git", "rev-parse", "--verify", f"{options.base}^{{commit}}"], cwd=root, capture_output=True, text=True, check=False)
    if probe.returncode != 0:
        checked(["git", "fetch", "--no-tags", "--depth=1", "origin", options.base], root)
    base = checked(["git", "rev-parse", options.base], root).stdout.strip()
    head = checked(["git", "rev-parse", "HEAD"], root).stdout.strip()
    rows: list[dict[str, object]] = []
    with baseline_tree(root, base) as before:
        # Provision both environments before timing; do not warm one suite first.
        for path in (before, root):
            checked(["uv", "run", "python", "-c", "import sys; print(sys.version)"], path)
        for sample, order in enumerate((("baseline", "candidate"), ("candidate", "baseline"), ("baseline", "candidate")), 1):
            for label in order:
                rows.append(measure(before if label == "baseline" else root, label, sample))
    totals: dict[str, float] = {}
    for label in ("baseline", "candidate"):
        values = [float(str(row["seconds"])) for row in rows if row["label"] == label]
        totals[label] = statistics.median(values)
    report = {"base": base, "head": head, "python": sys.version, "platform": platform.platform(), "cpu_count": os.cpu_count(), "roots": {"baseline": str(before), "candidate": str(root)}, "samples": rows, "median_seconds": totals}
    print("CI_COMPARISON_REPORT " + json.dumps(report), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
