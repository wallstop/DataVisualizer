#!/usr/bin/env python3
"""Single source of truth for which repository paths can change a self-test outcome.

The harness self-tests (``scripts/tests/test_*.py``) verify
this repository's agent tooling: lint scripts, the release pipeline, MCP
configuration, and the devcontainer. Both the CI lint workflow (through
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
    "docs/images/data-visualizer-",
)

SURFACE_FILES: Tuple[str, ...] = (
    "package.json",
    "package-lock.json",
    "opencode.json",
    "pyproject.toml",
    "uv.lock",
    "docs/images/capture-manifest.json",
    ".pre-commit-config.yaml",
    ".mcp.json",
    ".env.local.example",
)

# Which self-test files verify which tooling subject. A change outside every
# subject (shared manifests, the suite's own helpers, or anything unmapped)
# selects every test file, so a subject without a mapping still gets coverage.
# The tests that drive typed Python scripts are typed Python themselves
# (issue #171); the migration's last .ps1 test file ported with the
# host-analyzer installer tranche.
SUBJECTS_BY_PATH: Dict[str, Tuple[str, ...]] = {
    "docs/images/capture-manifest.json": ("test_export_docs_webp.py",),
    "scripts/build_docs.py": ("test_build_docs.py",),
    # Unity metadata cannot affect unrelated Python subjects. Keep this
    # companion on the same docs contract; unknown metadata stays fail-safe.
    "scripts/tests/test_build_docs.py.meta": ("test_build_docs.py",),
    "scripts/unity_mcp_doctor.py": ("test_unity_mcp_doctor.py",),
    ".llm/mcp/unity-mcp-host.mjs": ("test_unity_mcp_host.py",),
    # The live doctor path selects the doctor contract suite alongside the
    # coarse mcp contract suites the subtree prefix already selects (#269).
    "scripts/mcp_doctor.py": ("test_unity_mcp_doctor.py", "test_mcp_credentials.py", "test_mcp_sync.py"),
    ".llm/mcp/mcp-doctor.mjs": (
        "test_unity_mcp_doctor.py",
        "test_mcp_credentials.py",
        "test_mcp_sync.py",
    ),
    # The zai-image server contract suite joins the coarse mcp suites the
    # subtree prefix already selects.
    ".llm/mcp/zai_image_mcp.py": (
        "test_zai_image_mcp.py",
        "test_mcp_credentials.py",
        "test_mcp_sync.py",
    ),
    # Retired doctor path: kept so a deletion still selects the suite that
    # pinned it, instead of failing safe to the full suite.
    ".llm/mcp/unity-mcp-doctor.mjs": ("test_unity_mcp_doctor.py",),
    # Retired node zai-image server (issue #171 port): a deletion keeps the
    # ported contract suite selected instead of failing safe to everything.
    ".llm/mcp/zai-image-mcp.mjs": (
        "test_zai_image_mcp.py",
        "test_mcp_credentials.py",
        "test_mcp_sync.py",
    ),
    "scripts/run_ci_lints.py": ("test_run_ci_lints.py",),
    "scripts/run_ci_lints.py.meta": ("test_run_ci_lints.py",),
    "scripts/tests/test_run_ci_lints.py.meta": ("test_run_ci_lints.py",),
    "scripts/lint_meta_companions.py": ("test_meta_companions.py",),
    "scripts/lint_meta_companions.py.meta": ("test_meta_companions.py",),
    "scripts/tests/test_meta_companions.py.meta": ("test_meta_companions.py",),
    "scripts/tests/test_zai_image_mcp.py.meta": ("test_zai_image_mcp.py",),
    "scripts/export_docs_gif.py": ("test_export_docs_gif.py",),
    "scripts/export_docs_gif.py.meta": ("test_export_docs_gif.py",),
    "scripts/tests/test_export_docs_gif.py.meta": ("test_export_docs_gif.py",),
    "scripts/markdown_check.py": ("test_markdown_check.py",),
    "scripts/markdown_check.py.meta": ("test_markdown_check.py",),
    "scripts/tests/test_markdown_check.py.meta": ("test_markdown_check.py",),
    "scripts/fast_check.py": ("test_fast_check.py",),
    "scripts/gate.py": ("test_gate.py",),
    "pyproject.toml": ("test_gate.py",),
    "uv.lock": ("test_gate.py",),
    "scripts/harness_scope.py": ("test_fast_check.py", "test_harness_scope.py"),
    "scripts/should_run_self_tests.py": ("test_harness_scope.py",),
    "scripts/generate_skills_index.py": ("test_generate_skills_index.py",),
    "scripts/lint_llm_instructions.py": ("test_lint_llm_instructions.py",),
    "scripts/lint_file_lengths.py": ("test_file_lengths.py",),
    "scripts/lint_assembly_warnings.py": ("test_assembly_warnings.py",),
    "scripts/lint_csharp_member_order.py": ("test_csharp_member_order.py",),
    "scripts/lint_csharp_null_assertions.py": ("test_csharp_null_assertions.py",),
    "scripts/lint_csharp_usings.py": ("test_csharp_usings.py",),
    "scripts/lint_csharp_source_policy.py": ("test_csharp_source_policy.py",),
    "scripts/lint_changelog_length.py": ("test_changelog_length.py",),
    "scripts/lint_line_endings.py": ("test_line_endings.py",),
    "scripts/lint_release_secrets.py": ("test_release_secrets.py",),
    "scripts/run_unity_validation.py": ("test_run_unity_validation.py",),
    "scripts/export_docs_webp.py": ("test_export_docs_webp.py",),
    "scripts/export_docs_webp.py.meta": ("test_export_docs_webp.py",),
    "scripts/tests/test_export_docs_webp.py.meta": ("test_export_docs_webp.py",),
    "scripts/install_host_errorprone_analyzers.py": ("test_host_errorprone_analyzers.py",),
    "scripts/release/repository_identity.py": ("test_release_workflows.py",),
    "scripts/release/complete_release_pr.py": ("test_release_workflows.py",),
    "scripts/release/repository_identity.py.meta": ("test_release_workflows.py",),
    "scripts/release/complete_release_pr.py.meta": ("test_release_workflows.py",),
    "scripts/release/verify_release.py": ("test_verify_release.py",),
    "scripts/release/validate_unitypackage.py": ("test_validate_unitypackage.py",),
    "scripts/release/build_unitypackage.py": ("test_build_unitypackage.py", "test_validate_unitypackage.py"),
    "scripts/release/tag_release.py": ("test_tag_release.py",),
    "scripts/release/prepare_release.py": ("test_prepare_release.py",),
    "scripts/release/extract_release_notes.py": ("test_extract_release_notes.py",),
    "scripts/LlmConfig.psm1": ("test_lint_llm_instructions.py", "test_file_lengths.py", "test_generate_skills_index.py"),
    # Retired PowerShell surfaces (issue #171) keep their entries so a
    # deletion still selects the tests that verify the contract they used to
    # own, instead of failing safe to the full suite.
    # Retired node release scripts (issue #171) select their ported test
    # files the same way.
    "scripts/release/verify-release.mjs": ("test_verify_release.py",),
    "scripts/release/validate-unitypackage.mjs": ("test_validate_unitypackage.py",),
    "scripts/release/build-unitypackage.mjs": ("test_build_unitypackage.py", "test_validate_unitypackage.py"),
    "scripts/release/tag-release.mjs": ("test_tag_release.py",),
    "scripts/release/prepare-release.mjs": ("test_prepare_release.py",),
    "scripts/release/extract-release-notes.mjs": ("test_extract_release_notes.py",),
    # Retired node lints (issue #171) select their ported test files the same
    # way a retired PowerShell surface does.
    "scripts/lint-csharp-member-order.js": ("test_csharp_member_order.py",),
    "scripts/lint-csharp-null-assertions.js": ("test_csharp_null_assertions.py",),
    "scripts/lint-csharp-usings.js": ("test_csharp_usings.py",),
    "scripts/generate-skills-index.ps1": ("test_generate_skills_index.py",),
    "scripts/lint-llm-instructions.ps1": ("test_lint_llm_instructions.py",),
    "scripts/lint-file-lengths.ps1": ("test_file_lengths.py",),
    "scripts/lint-assembly-warnings.ps1": ("test_assembly_warnings.py",),
    "scripts/run-unity-validation.ps1": ("test_run_unity_validation.py",),
    "scripts/install-host-errorprone-analyzers.ps1": ("test_host_errorprone_analyzers.py",),
    # Retired node lints (issue #171) select their ported test files the same
    # way.
    "scripts/lint-changelog-length.js": ("test_changelog_length.py",),
    "scripts/lint-line-endings.js": ("test_line_endings.py",),
    "scripts/lint-release-secrets.js": ("test_release_secrets.py",),
    ".devcontainer/install_npm_tools.py": ("test_install_npm_tools.py",),
    ".devcontainer/install-npm-tools.sh": ("test_install_npm_tools.py",),
    "scripts/tests/test_install_npm_tools.py.meta": ("test_install_npm_tools.py",),
    ".devcontainer/ai_backends.py": ("test_ai_backends.py", "test_env_local_precedence.py"),
    ".devcontainer/env-local.sh": ("test_ai_backends.py", "test_env_local_precedence.py"),
    ".devcontainer/backend_state.py": ("test_backend_state.py", "test_ai_backends.py", "test_env_local_precedence.py"),
    "scripts/tests/test_backend_state.py.meta": ("test_backend_state.py",),
    ".devcontainer/recycle_stale_opencode_service.py": ("test_opencode_service_recycle.py",),
    # The lifecycle entry points' contract suite (issue #171 port).
    ".devcontainer/_lifecycle_common.py": ("test_devcontainer_lifecycle.py",),
    ".devcontainer/on_create.py": ("test_devcontainer_lifecycle.py",),
    ".devcontainer/post_create.py": ("test_devcontainer_lifecycle.py",),
    ".devcontainer/post_start.py": ("test_devcontainer_lifecycle.py",),
    ".devcontainer/update_content.py": ("test_devcontainer_lifecycle.py",),
}

# Coarse subjects for tooling trees whose files share one contract suite.
# Specific prefixes win over the coarse trees, so order matters; the mcp
# subtree verifies the MCP contract suite, not the .llm content lints.
SUBJECT_PREFIXES: Tuple[Tuple[str, Tuple[str, ...]], ...] = (
    (".llm/mcp/", ("test_mcp_credentials.py", "test_mcp_sync.py")),
    (
        ".devcontainer/",
        (
            "test_ai_backends.py",
            "test_devcontainer_lifecycle.py",
            "test_env_local_precedence.py",
            "test_mcp_credentials.py",
            "test_mcp_sync.py",
            "test_opencode_service_recycle.py",
        ),
    ),
    (".llm/", ("test_lint_llm_instructions.py", "test_file_lengths.py", "test_generate_skills_index.py")),
    ("docs/images/data-visualizer-", ("test_export_docs_webp.py",)),
    (".github/", ("test_release_workflows.py", "test_release_secrets.py")),
)

SHARED_PREFIXES: Tuple[str, ...] = (
    "scripts/tests/",
    ".config/",
)

_SELF_TEST_FILE_PATTERN = re.compile(r"^scripts/tests/(test-[a-z0-9-]+\.ps1|test_[a-z0-9_]+\.py)$")
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


def markdown_paths_changed(paths: Sequence[str], repo_root: Path = REPO_ROOT) -> bool:
    """Return whether any changed path can affect the checked Markdown formatting.

    Any changed ``.md`` file counts, plus the Prettier configuration, ignore
    list, and the package manifest that pins the formatter. CI's llm-lint
    scope step and the gate's Markdown-format step share this predicate so a
    change that cannot affect the checked Markdown skips the formatter in
    both places with identical semantics.
    """
    return markdown_check_files(paths, repo_root) != _MARKDOWN_UNCHANGED


_MARKDOWN_UNCHANGED = ""
_MARKDOWN_CHECK_ALL = "all"


def markdown_check_files(paths: Sequence[str], repo_root: Path = REPO_ROOT) -> str:
    """Return the Markdown check scope for changed paths.

    The result is ``all`` when the Prettier configuration, ignore list, or
    package manifest changed, or when the ignore list cannot be read, so
    the formatter never narrows on doubt. Otherwise it is the sorted,
    comma-joined changed ``.md`` paths that Prettier's own glob would check,
    meaning paths that still exist on disk and sit outside the ignore
    list's directory prefixes. The empty string means the change cannot
    affect checked Markdown.

    CI's llm-lint workflow checks these explicit paths on pull requests so
    the step does not re-scan every tracked file; push events keep the full
    ``**/*.md`` sweep.
    """
    files: List[str] = []
    for path in paths:
        normalized = normalize_relative_path(path)
        if normalized in _MARKDOWN_CONFIG_FILES:
            return _MARKDOWN_CHECK_ALL
        if normalized.endswith(".md"):
            files.append(normalized)
    if not files:
        return _MARKDOWN_UNCHANGED
    # Deletions and rename old-sides appear in a git diff but not on disk,
    # and Prettier exits 2 on a path that matches no file, so only paths
    # that still exist may reach the formatter. When nothing remains, the
    # change cannot affect checked Markdown.
    files = [path for path in files if (repo_root / path).is_file()]
    if not files:
        return _MARKDOWN_UNCHANGED
    prefixes = _prettierignore_prefixes(repo_root)
    if prefixes is None:
        return _MARKDOWN_CHECK_ALL
    files = [path for path in files if not any(path.startswith(prefix) for prefix in prefixes)]
    if not files:
        return _MARKDOWN_UNCHANGED
    return ",".join(sorted(set(files)))


_MARKDOWN_CONFIG_FILES = (".prettierrc.json", ".prettierignore", "package.json")


def _prettierignore_prefixes(repo_root: Path) -> Optional[Tuple[str, ...]]:
    """Return the normalized directory prefixes ``.prettierignore`` excludes.

    Every current pattern is a root-relative directory prefix ending in a
    slash. A missing file yields no exclusions; a pattern the simple prefix
    grammar cannot represent yields ``None``, which the caller must treat as
    "check everything" - the same fail-safe direction as every other verdict
    here.
    """
    ignore_path = repo_root / ".prettierignore"
    try:
        lines = ignore_path.read_text(encoding="utf-8").splitlines()
    except OSError:
        return ()
    prefixes: List[str] = []
    for line in lines:
        pattern = line.strip()
        if not pattern or pattern.startswith("#"):
            continue
        if not pattern.endswith("/"):
            # An unsupported glob shape must not silently narrow the check.
            return None
        prefix = normalize_relative_path(pattern.rstrip("/"))
        if prefix:
            prefixes.append(prefix + "/")
    return tuple(prefixes)


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


def available_test_files() -> Tuple[str, ...]:
    """List the self-test file names the suite currently discovers.

    A mapped name is only considered available when the runner would
    actually discover it; a stale mapping therefore fails safe to the full
    suite instead of narrowing to a file that does not exist.
    """
    tests_dir = Path(__file__).resolve().parent / "tests"
    return tuple(sorted(path.name for path in tests_dir.glob("test_*.py")))


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
