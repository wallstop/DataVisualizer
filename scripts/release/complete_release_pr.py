#!/usr/bin/env python3
"""Wait for explicitly dispatched CI, merge its exact PR head, and report the merge.

GITHUB_TOKEN events do not start other workflows. The caller must publish the
reported merge in the same run rather than wait for a pull_request event.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from typing import Any, Dict, List, Sequence, cast


def gh(arguments: Sequence[str], decode: bool = True) -> Any:
    result = subprocess.run(["gh", *arguments], capture_output=True, text=True, check=False)
    if result.returncode != 0:
        raise ValueError(f"GitHub command failed: {result.stderr.strip() or result.stdout.strip()}")
    return json.loads(result.stdout) if decode and result.stdout.strip() else None


def complete(branch: str, request: str, expected_head: str, timeout: float = 900) -> str:
    """Fail closed on changed heads, failed CI, and repository merge policies."""
    repository = os.environ["GITHUB_REPOSITORY"]
    pr = cast(Dict[str, Any], gh(["pr", "view", branch, "--json", "number,headRefOid,baseRefName,isCrossRepository,state"]))
    if pr["baseRefName"] != "main" or pr["isCrossRepository"] or pr["state"] != "OPEN":
        raise ValueError("Only an open release PR from this repository into main can be merged.")
    head = str(pr["headRefOid"])
    if head != expected_head:
        raise ValueError("The release PR head changed after preparation; refusing to merge.")
    endpoint = f"repos/{repository}"
    gh(["workflow", "run", "llm-lint.yml", "--ref", branch, "-f", f"release_request={request}"], decode=False)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        data = cast(Dict[str, Any], gh(["api", f"{endpoint}/actions/workflows/llm-lint.yml/runs", "--method", "GET",
                                      "-f", f"branch={branch}", "-f", "event=workflow_dispatch", "-f", "per_page=100"]))
        runs = cast(List[Dict[str, Any]], data["workflow_runs"])
        matching = [run for run in runs if run["display_title"] == request and run["head_sha"] == head]
        if matching and matching[0]["status"] == "completed":
            if matching[0]["conclusion"] != "success":
                raise ValueError(f"Release CI failed: {matching[0]['html_url']}")
            break
        time.sleep(10)
    else:
        raise ValueError("Timed out waiting for release CI; no merge or publication was attempted.")
    result = cast(Dict[str, Any], gh(["api", f"{endpoint}/pulls/{pr['number']}/merge", "--method", "PUT",
                                    "-f", "merge_method=squash", "-f", f"sha={head}"]))
    if result.get("merged") is not True:
        raise ValueError(f"Repository policy prevented release merge: {result.get('message')}")
    return str(result["sha"])


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--branch", required=True)
    parser.add_argument("--request", required=True)
    parser.add_argument("--head", required=True)
    args = parser.parse_args()
    if not args.branch.startswith("release/v"):
        raise ValueError("Expected a release/vX.Y.Z branch.")
    sha = complete(args.branch, args.request, args.head)
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"merge_sha={sha}\n")
    print(f"Merged the validated release at {sha}.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError) as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
