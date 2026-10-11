#!/usr/bin/env python3
"""Single source of truth for which repository paths can change a self-test outcome.

The harness self-tests (``scripts/tests/test-*.ps1``) verify this repository's
agent tooling: lint scripts, the release pipeline, MCP configuration, and the
devcontainer. Both the CI lint workflow (through
``scripts/should_run_self_tests.py``) and the local fast check
(``scripts/fast_check.py``) run the selection defined here, so the two always
agree. Every verdict fails safe: an unmapped path, a stale name, or an
unresolvable base selects the full suite, so mapping drift can never silently
skip coverage. Edit only here.

Launch with ``uv run scripts/harness_scope.py``; any Python 3.9+ interpreter
works. The ``surface`` and ``selection`` subcommands print JSON probe results
for the harness self-tests and manual debugging; they execute nothing.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Set, Tuple

REPO_ROOT = Path(__file__).resolve().parent.parent

SURFACE_PREFIXES: Tuple[str, ...] = (
    "scripts/",
    ".llm/",
    ".github/",
    ".devcontainer/",
    ".config/",
)

SURFACE_FILES: Tuple[str, ...] = (
    "package.json",
    "package-lock.json",
    "opencode.json",
    "pyproject.toml",
    "uv.lock",
    ".pre-commit-config.yaml",
    ".mcp.json",
    ".env.local.example",
)

# Which self-test files verify which tooling subject. A change outside every
# subject (shared manifests, the suite's own helpers, or anything unmapped)
# selects every test file, so a subject without a mapping still gets coverage.
SUBJECTS_BY_PATH: Dict[str, Tuple[str, ...]] = {
    "scripts/fast_check.py": ("test-fast-check.ps1",),
    "scripts/gate.py": ("test-gate.ps1",),
    "pyproject.toml": ("test-gate.ps1",),
    "uv.lock": ("test-gate.ps1",),
    "scripts/harness_scope.py": ("test-fast-check.ps1", "test-harness-scope.ps1"),
    "scripts/should_run_self_tests.py": ("test-harness-scope.ps1",),
    "scripts/generate-skills-index.ps1": ("test-generate-skills-index.ps1",),
    "scripts/lint-llm-instructions.ps1": ("test-lint-llm-instructions.ps1",),
    "scripts/lint-file-lengths.ps1": ("test-file-lengths.ps1",),
    "scripts/lint-assembly-warnings.ps1": ("test-assembly-warnings.ps1",),
    "scripts/lint-csharp-member-order.js": ("test-csharp-member-order.ps1",),
    "scripts/lint-csharp-null-assertions.js": ("test-lint-csharp-null-assertions.ps1",),
    "scripts/lint-csharp-usings.js": ("test-lint-csharp-usings.ps1",),
    "scripts/lint-changelog-length.js": ("test-lint-changelog-length.ps1",),
    "scripts/lint-line-endings.js": ("test-lint-line-endings.ps1",),
    "scripts/lint-release-secrets.js": ("test-lint-release-secrets.ps1",),
    "scripts/run-unity-validation.ps1": ("test-run-unity-validation.ps1",),
    "scripts/install-host-errorprone-analyzers.ps1": ("test-host-errorprone-analyzers.ps1",),
    "scripts/release/verify-release.mjs": ("test-verify-release.ps1",),
    "scripts/release/validate-unitypackage.mjs": ("test-validate-unitypackage.ps1",),
    "scripts/release/build-unitypackage.mjs": ("test-build-unitypackage.ps1", "test-validate-unitypackage.ps1"),
    "scripts/release/tag-release.mjs": ("test-tag-release.ps1",),
    "scripts/release/prepare-release.mjs": ("test-prepare-release.ps1",),
    "scripts/release/extract-release-notes.mjs": ("test-extract-release-notes.ps1",),
    "scripts/LlmConfig.psm1": ("test-lint-llm-instructions.ps1", "test-file-lengths.ps1", "test-generate-skills-index.ps1"),
    ".devcontainer/ai-backends.sh": ("test-ai-backends.ps1", "test-env-local-precedence.ps1"),
    ".devcontainer/env-local.sh": ("test-ai-backends.ps1", "test-env-local-precedence.ps1"),
    ".devcontainer/recycle-stale-opencode-service.sh": ("test-opencode-service-recycle.ps1",),
}

# Coarse subjects for tooling trees whose files share one contract suite.
# Specific prefixes win over the coarse trees, so order matters; the mcp
# subtree verifies the MCP contract suite, not the .llm content lints.
SUBJECT_PREFIXES: Tuple[Tuple[str, Tuple[str, ...]], ...] = (
    (".llm/mcp/", ("test-mcp-credentials.ps1", "test-mcp-sync.ps1")),
    (
        ".devcontainer/",
        (
            "test-ai-backends.ps1",
            "test-env-local-precedence.ps1",
            "test-mcp-credentials.ps1",
            "test-mcp-sync.ps1",
            "test-opencode-service-recycle.ps1",
        ),
    ),
    (".llm/", ("test-lint-llm-instructions.ps1", "test-file-lengths.ps1", "test-generate-skills-index.ps1")),
    (".github/", ("test-release-workflows.ps1", "test-lint-release-secrets.ps1")),
)

SHARED_PREFIXES: Tuple[str, ...] = (
    "scripts/tests/",
    ".config/",
)

_SELF_TEST_FILE_PATTERN = re.compile(r"^scripts/tests/(test-[a-z0-9-]+\.ps1)$")
_RENAME_PATTERN = re.compile(r"^(.*) -> (.*)$")


@dataclass(frozen=True)
class Selection:
    """The self-test files a change selects: everything, or an explicit list."""

    all: bool
    names: Tuple[str, ...] = ()


def normalize_relative_path(path: str) -> str:
    """Normalize a repository-relative path to forward slashes without ``./``.

    Exactly one leading ``./`` is stripped, matching the PowerShell original's
    anchored single replacement; deeper dot prefixes stay verbatim.
    """
    if not path or not path.strip():
        return ""
    normalized = path.replace("\\", "/")
    if normalized.startswith("./"):
        normalized = normalized[2:]
    return normalized.lstrip("/")


def is_surface_path(path: str) -> bool:
    """Return whether a path sits on the surface the self-tests verify."""
    normalized = normalize_relative_path(path)
    if not normalized:
        return False
    for prefix in SURFACE_PREFIXES:
        if normalized.startswith(prefix):
            return True
    for surface_file in SURFACE_FILES:
        if normalized == surface_file:
            return True
    return False


def _git_lines(repo_root: Path, args: Sequence[str]) -> List[str]:
    try:
        completed = subprocess.run(
            ["git", *args],
            cwd=str(repo_root),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except OSError:
        return []
    if completed.returncode != 0:
        return []
    return [line for line in (completed.stdout or "").splitlines() if line.strip()]


def committed_changed_files(repo_root: Path, base_sha: str, head_sha: str = "HEAD") -> List[str]:
    """List the paths that changed between two commits (tree-only, no worktree scan)."""
    if not base_sha or not head_sha:
        return []
    return _git_lines(repo_root, ["--no-optional-locks", "diff", "--name-only", base_sha, head_sha])


def working_tree_changed_files(repo_root: Path) -> List[str]:
    """List staged, unstaged, and untracked paths in one scan.

    ``git status --porcelain`` covers all three states in one scan, and
    ``-uall`` lists untracked files individually instead of collapsing them
    into their untracked parent directory. ``--no-optional-locks`` keeps git
    from writing the refreshed index back, which dominates the scan on
    bind-mounted working trees (measured ~13 s there with the write vs ~2 s
    without) and turns every fast-check invocation into a slow one.
    """
    paths: List[str] = []
    for line in _git_lines(repo_root, ["--no-optional-locks", "status", "--porcelain", "-uall"]):
        path = line[3:]
        rename = _RENAME_PATTERN.match(path)
        if rename:
            # A staged rename reports both sides; the old side is kept for
            # group triggering and filtered out later by the existence check.
            paths.append(rename.group(1))
            paths.append(rename.group(2))
            continue
        paths.append(path)
    return [path for path in paths if path]


def harness_surface_changed(repo_root: Path, base_sha: str) -> bool:
    """Return whether any path changed on the self-test surface."""
    paths = committed_changed_files(repo_root, base_sha) + working_tree_changed_files(repo_root)
    return any(is_surface_path(path) for path in paths)


def self_test_selection(paths: Sequence[str], available_test_files: Sequence[str] = ()) -> Selection:
    """Map changed surface paths onto the self-test files that must run.

    Any shared or unmapped path - or a mapped name that is not among the
    available files - fails safe to the full suite.
    """
    selected: Set[str] = set()
    for path in paths:
        normalized = normalize_relative_path(path)
        # Paths outside the self-test surface cannot change a test outcome,
        # so they never influence the selection; only surface paths that no
        # subject claims fail safe to the full suite.
        if not normalized or not is_surface_path(normalized):
            continue

        subjects = SUBJECTS_BY_PATH.get(normalized)
        if subjects is not None:
            selected.update(subjects)
            continue

        # Editing a self-test file selects itself for immediate feedback.
        self_test = _SELF_TEST_FILE_PATTERN.match(normalized)
        if self_test:
            selected.add(self_test.group(1))
            continue

        if any(normalized.startswith(shared) for shared in SHARED_PREFIXES):
            return Selection(all=True)

        matched = False
        for prefix, names in SUBJECT_PREFIXES:
            if normalized.startswith(prefix):
                matched = True
                selected.update(names)
                break
        if not matched:
            return Selection(all=True)

    if not selected:
        return Selection(all=True)

    names = tuple(sorted(selected))
    if available_test_files:
        if any(name not in available_test_files for name in names):
            return Selection(all=True)
    return Selection(all=False, names=names)


def _parse_path_list(raw: str) -> List[str]:
    return [path for path in raw.split(",") if path]


def probe_document(paths: Sequence[str], available_test_files: Sequence[str]) -> Dict[str, object]:
    """Build the JSON probe result used by the CLI and the harness self-tests."""
    selection = self_test_selection(paths, available_test_files)
    return {
        "surface": {path: is_surface_path(path) for path in paths},
        "selection": {"all": selection.all, "names": list(selection.names)},
    }


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="harness-scope",
        description="Probe the harness self-test surface and test selection without executing anything.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    probe = subparsers.add_parser("probe", help="probe surface and selection results for a comma-separated path list")
    probe.add_argument("--paths", default="", help="comma-separated repository-relative paths")
    probe.add_argument("--available", default="", help="comma-separated available test file names")
    options = parser.parse_args(argv)

    paths = _parse_path_list(options.paths)
    available = _parse_path_list(options.available)
    print(json.dumps(probe_document(paths, available), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
