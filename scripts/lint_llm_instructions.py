#!/usr/bin/env python3
"""Lints the .llm agent harness contract (issue #171).

Typed Python port of ``scripts/lint-llm-instructions.ps1``: required files,
SKILL.md frontmatter validity, generated-index freshness, context H1 and
version sync, and agent entrypoint delegation. ``--fix`` regenerates a
missing or stale index and syncs the context version; without it every
finding is an error.

Launch with ``uv run scripts/lint_llm_instructions.py``; any Python 3.9+
interpreter works.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

# Importing the shared config must not leave scripts/__pycache__ behind:
# the bytecode cache is an untracked surface path the next scan would see,
# fail safe on, and turn every subsequent run into the full suite.
sys.dont_write_bytecode = True

from llm_config import (
    AGENT_ENTRYPOINTS,
    ALLOWED_SKILL_CATEGORIES,
    CONTEXT_RELATIVE_PATH,
    SKILLS_INDEX_FILE_NAME,
    SKILLS_RELATIVE_PATH,
    SkillMetadata,
    get_skill_metadata_list,
    read_utf8_text,
    resolve_llm_root,
    skills_index_content,
    write_utf8_no_bom,
)

_H1_PATTERN = re.compile(r"^# ", re.MULTILINE)


class _LintState:
    """Collects errors so one run reports every finding, not just the first."""

    def __init__(self) -> None:
        self.errors = 0

    def write_error(self, message: str) -> None:
        print(f"[llm-instructions] ERROR: {message}")
        self.errors += 1

    def write_fix(self, message: str) -> None:
        print(f"[llm-instructions] fix: {message}")


def _package_version(package_json_path: Path) -> str:
    """Read package.json's version, raising when it is unusable."""
    raw = read_utf8_text(package_json_path)
    try:
        package: Dict[str, Any] = json.loads(raw)
    except json.JSONDecodeError as error:
        raise ValueError(f"package.json is not valid JSON: {error}") from error
    try:
        version: Optional[str] = package.get("version")
    except AttributeError:
        # Non-object JSON (an array or scalar) has no version field; the
        # PowerShell original degraded to the missing-version verdict.
        version = None
    if not isinstance(version, str) or not version.strip():
        raise ValueError("package.json is missing a version field")
    return version


def _update_context_version(context_path: Path, package_version: str) -> bool:
    """Rewrite the context version line once; report whether anything changed."""
    text = read_utf8_text(context_path)
    pattern = re.compile(r"(\*\*Version\*\*:\s*)([^\r\n]*)")
    if pattern.search(text) is None:
        return False
    updated = pattern.sub(lambda match: match.group(1) + package_version, text, count=1)
    if updated == text:
        return False
    write_utf8_no_bom(context_path, updated)
    return True


def _check_index(
    state: _LintState,
    skills_root: Path,
    skills: List[SkillMetadata],
    index_path: Path,
    fix: bool,
) -> None:
    """Verify the generated index against the skill frontmatter."""
    expected_index = skills_index_content(skills)
    if not index_path.exists():
        if fix:
            write_utf8_no_bom(index_path, expected_index)
            state.write_fix(f"generated missing {SKILLS_INDEX_FILE_NAME}")
        else:
            state.write_error(f"{SKILLS_INDEX_FILE_NAME} is missing; run lint_llm_instructions.py --fix")
        return
    actual_index = read_utf8_text(index_path)
    if actual_index == expected_index:
        return
    if fix:
        write_utf8_no_bom(index_path, expected_index)
        state.write_fix(f"regenerated stale {SKILLS_INDEX_FILE_NAME}")
        return
    state.write_error(
        f"{SKILLS_INDEX_FILE_NAME} is out of date; run lint_llm_instructions.py --fix "
        f"or scripts/generate_skills_index.py"
    )
    expected_lines = expected_index.split("\n")
    actual_lines = actual_index.split("\n")
    shown = 0
    for index in range(max(len(expected_lines), len(actual_lines))):
        if shown >= 5:
            break
        expected_line = expected_lines[index] if index < len(expected_lines) else "<missing>"
        actual_line = actual_lines[index] if index < len(actual_lines) else "<missing>"
        if expected_line != actual_line:
            print(f"[llm-instructions]   line {index + 1}: expected '{expected_line}' but found '{actual_line}'")
            shown += 1


def _check_context(
    state: _LintState,
    context_path: Path,
    package_json_path: Path,
    fix: bool,
) -> None:
    """Verify the context file's H1 count, skills link, and version sync."""
    context = read_utf8_text(context_path)
    h1_count = len(_H1_PATTERN.findall(context))
    if h1_count != 1:
        state.write_error(f"context.md must contain exactly one H1; found {h1_count}")
    if "./skills/index.md" not in context:
        state.write_error("context.md must link the generated skills index (./skills/index.md)")

    try:
        package_version = _package_version(package_json_path)
        version_pattern = re.compile(r"\*\*Version\*\*:\s*([^\r\n]+)")
        version_match = version_pattern.search(context)
        if version_match is None:
            state.write_error("context.md must carry a '**Version**: x.y.z' line")
        else:
            context_version = version_match.group(1).strip()
            if context_version != package_version:
                if fix and _update_context_version(context_path, package_version):
                    state.write_fix(f"synced context.md version to {package_version}")
                else:
                    state.write_error(
                        f"context.md version '{context_version}' does not match package.json "
                        f"version '{package_version}'"
                    )
    except ValueError as error:
        state.write_error(str(error))


def run_lint(repo_root: Path, fix: bool, verbose: bool) -> int:
    """Run every .llm contract check and return the process exit code."""
    state = _LintState()
    context_path = repo_root / CONTEXT_RELATIVE_PATH
    skills_root = repo_root / SKILLS_RELATIVE_PATH
    package_json_path = repo_root / "package.json"
    generator_path = repo_root / "scripts" / "generate_skills_index.py"
    index_path = skills_root / SKILLS_INDEX_FILE_NAME

    required: List[Tuple[Path, str]] = [
        (context_path, "context file"),
        (skills_root, "skills directory"),
        (package_json_path, "package manifest"),
        (generator_path, "skills index generator"),
    ]
    required.extend(
        (repo_root / entrypoint.path, f"agent entrypoint {entrypoint.path}") for entrypoint in AGENT_ENTRYPOINTS
    )
    for path, label in required:
        if not path.exists():
            state.write_error(f"required {label} is missing at '{path}'")

    skills: List[SkillMetadata] = []
    skills_valid = True
    if skills_root.exists():
        try:
            skills = get_skill_metadata_list(skills_root)
        except (ValueError, OSError) as error:
            skills_valid = False
            state.write_error(str(error))
        if not skills and skills_valid:
            skills_valid = False
            state.write_error("no skills found; add a skill directory with a SKILL.md")
    else:
        skills_valid = False

    if skills_valid and skills:
        _check_index(state, skills_root, skills, index_path, fix)

    if context_path.exists():
        _check_context(state, context_path, package_json_path, fix)

    for entrypoint in AGENT_ENTRYPOINTS:
        pointer_path = repo_root / entrypoint.path
        if not pointer_path.exists():
            continue
        pointer_text = read_utf8_text(pointer_path)
        if entrypoint.link not in pointer_text:
            state.write_error(
                f"agent entrypoint '{entrypoint.path}' must delegate to the context file via "
                f"markdown link '{entrypoint.link}'"
            )

    if state.errors > 0:
        print(f"[llm-instructions] FAILED: {state.errors} error(s)")
        return 1
    skill_count = len(skills)
    if not verbose:
        print(f"[llm-instructions] OK: {skill_count} skill(s), index fresh, entrypoints delegated")
    else:
        categories = ", ".join(ALLOWED_SKILL_CATEGORIES)
        print(f"[llm-instructions] OK: {skill_count} skill(s), index fresh, entrypoints delegated (categories: {categories})")
    return 0


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="llm-instructions",
        description="Lint the .llm agent harness contract.",
    )
    parser.add_argument("--root", default="", help="repository root (default: the checkout holding this script)")
    parser.add_argument("--fix", action="store_true", help="regenerate a stale index and sync the context version")
    parser.add_argument("--verbose-output", action="store_true", help="report categories and near-limit warnings")
    options = parser.parse_args(argv)

    try:
        repo_root = resolve_llm_root(options.root)
    except ValueError as error:
        print(f"[llm-instructions] ERROR: {error}")
        return 1
    try:
        return run_lint(repo_root, options.fix, options.verbose_output)
    except OSError as error:
        print(f"[llm-instructions] ERROR: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
