#!/usr/bin/env python3
"""Generates .llm/skills/index.md from the skill directories (issue #171).

Typed Python port of ``scripts/generate-skills-index.ps1``: the output is
byte-identical for the same skills apart from the banner, which now points
at this script. ``lint_llm_instructions.py`` verifies index freshness
against the same rules, so a stale index fails both.

Launch with ``uv run scripts/generate_skills_index.py``; any Python 3.9+
interpreter works.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import List, Optional, Sequence

# Importing the shared config must not leave scripts/__pycache__ behind:
# the bytecode cache is an untracked surface path the next scan would see,
# fail safe on, and turn every subsequent run into the full suite.
sys.dont_write_bytecode = True

from llm_config import (
    ALLOWED_SKILL_CATEGORIES,
    SKILLS_INDEX_FILE_NAME,
    SKILLS_RELATIVE_PATH,
    SkillMetadata,
    read_skill_metadata,
    resolve_llm_root,
    skills_index_content,
    write_utf8_no_bom,
)


def generate_index(repo_root: Path, output_path: Optional[str]) -> int:
    """Generate the skills index and report the result on stdout."""
    skills_root = repo_root / SKILLS_RELATIVE_PATH
    if not skills_root.exists():
        print(f"[skills-index] ERROR: no skills found; skills directory '{SKILLS_RELATIVE_PATH}' does not exist")
        return 1

    directories = sorted(
        entry.name for entry in skills_root.iterdir() if entry.is_dir() and not entry.name.startswith(".")
    )
    if not directories:
        print("[skills-index] ERROR: no skills found; add a skill directory with a SKILL.md")
        return 1

    skills: List[SkillMetadata] = []
    for directory in directories:
        skill_path = skills_root / directory / "SKILL.md"
        if not skill_path.exists():
            print(f"[skills-index] ERROR: skill directory '{directory}' is missing SKILL.md")
            return 1
        try:
            skills.append(read_skill_metadata(skill_path, directory))
        except ValueError as error:
            print(f"[skills-index] ERROR: {directory}/SKILL.md :: {error}")
            return 1

    target = Path(output_path) if output_path and output_path.strip() else skills_root / SKILLS_INDEX_FILE_NAME
    try:
        write_utf8_no_bom(target, skills_index_content(skills))
    except OSError as error:
        print(f"[skills-index] ERROR: {error}")
        return 1
    categories = len(ALLOWED_SKILL_CATEGORIES)
    print(f"[skills-index] wrote {target} ({len(skills)} skill(s) across {categories} categories)")
    return 0


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="generate-skills-index",
        description="Generate .llm/skills/index.md from the skill directories.",
    )
    parser.add_argument("--root", default="", help="repository root (default: the checkout holding this script)")
    parser.add_argument("--output-path", default="", help="alternate index output path")
    options = parser.parse_args(argv)

    try:
        repo_root = resolve_llm_root(options.root)
    except ValueError as error:
        print(f"[skills-index] ERROR: {error}")
        return 1
    return generate_index(repo_root, options.output_path)


if __name__ == "__main__":
    sys.exit(main())
