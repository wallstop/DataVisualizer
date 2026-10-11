#!/usr/bin/env python3
"""Shared contract for the .llm harness lints (issue #171).

This is the typed Python port of ``scripts/LlmConfig.psm1``: the skill
frontmatter rules, the generated skills-index layout, and the text helpers
the lints share. Every consumer of the skill contract imports this module,
so the frontmatter rules live in exactly one place. ``build_docs.py``
resolves its own repository root.

The parsing behavior mirrors the PowerShell module verdict for verdict:
same accepted frontmatter shapes, same error messages, same generated
index bytes (normalized to LF, UTF-8 without BOM).
"""

from __future__ import annotations

import os
import re
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional, Set, Tuple

ALLOWED_SKILL_CATEGORIES: Tuple[str, ...] = ("Core", "Workflow", "Feature")
DEFAULT_SKILL_CATEGORY = "Feature"
MAX_FILE_LINES = 300
WARN_FILE_LINES = 280
MAX_PLANNING_DOC_LINES = 150
WARN_PLANNING_DOC_LINES = 120
PLANNING_DOC_PATHS: Tuple[str, ...] = ("PLAN.md", "GOAL.md")
SKILL_NAME_PATTERN = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
ASCII_PATTERN = re.compile(r"^[\x20-\x7E]*$")
SKILLS_INDEX_FILE_NAME = "index.md"
CONTEXT_RELATIVE_PATH = ".llm/context.md"
SKILLS_RELATIVE_PATH = ".llm/skills"

_METADATA_INDENTED_PATTERN = re.compile(r"^\s+([A-Za-z][A-Za-z0-9_-]*):\s*(.*)$")
_FRONTMATTER_PATTERN = re.compile(r"^([A-Za-z][A-Za-z0-9_-]*):\s*(.*)$")


@dataclass(frozen=True)
class AgentEntrypoint:
    """One agent frontend file and the context link it must carry."""

    path: str
    link: str


AGENT_ENTRYPOINTS: Tuple[AgentEntrypoint, ...] = (
    AgentEntrypoint("AGENTS.md", "](./.llm/context.md)"),
    AgentEntrypoint("CLAUDE.md", "](./.llm/context.md)"),
    AgentEntrypoint(".cursorrules", "](./.llm/context.md)"),
    AgentEntrypoint(".windsurfrules", "](./.llm/context.md)"),
    AgentEntrypoint(".github/copilot-instructions.md", "](../.llm/context.md)"),
)


@dataclass(frozen=True)
class SkillMetadata:
    """One validated SKILL.md frontmatter record."""

    name: str
    description: str
    category: str
    directory: str
    path: str


def resolve_llm_root(root: Optional[str]) -> Path:
    """Resolve the repository root from the argument, else the script layout."""
    resolved: str
    if root is None or not root.strip():
        resolved = str(Path(__file__).resolve().parent.parent)
    else:
        resolved = os.path.abspath(root)
    if not os.path.exists(resolved):
        raise ValueError(f"repository root '{resolved}' does not exist")
    return Path(resolved)


def normalize_text(text: str) -> str:
    """Normalize CRLF and bare CR line endings to LF."""
    return text.replace("\r\n", "\n").replace("\r", "\n")


def physical_line_count(normalized_text: str) -> int:
    """Count physical lines the way the PowerShell original did.

    A trailing line break closes the last line; text without one adds the
    unterminated tail as its own line.
    """
    if not normalized_text:
        return 0
    count = normalized_text.count("\n")
    if not normalized_text.endswith("\n"):
        count += 1
    return count


def read_utf8_text(path: Path) -> str:
    """Read a UTF-8 file (BOM tolerated) and normalize its line endings."""
    with open(path, "r", encoding="utf-8-sig", errors="strict", newline="") as handle:
        return normalize_text(handle.read())


def write_utf8_no_bom(path: Path, normalized_text: str) -> None:
    """Write normalized text as UTF-8 without a BOM, creating parents."""
    directory = path.parent
    if str(directory) and not directory.exists():
        directory.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(normalized_text)


def read_skill_metadata(skill_path: Path, expected_directory: str) -> SkillMetadata:
    """Parse and validate one SKILL.md frontmatter block.

    Raises ``ValueError`` with the same messages the PowerShell module
    produced, so the lints' output shape stays stable across the port.
    """
    text = read_utf8_text(skill_path)
    if not text:
        raise ValueError("SKILL.md is empty")
    lines = text.split("\n")
    if lines[0].rstrip() != "---":
        raise ValueError("SKILL.md must start with a YAML frontmatter block (---)")

    closing_index = -1
    for index in range(1, min(len(lines), 65)):
        if lines[index].rstrip() == "---":
            closing_index = index
            break
    if closing_index < 0:
        raise ValueError("SKILL.md frontmatter is not terminated by ---")

    name: Optional[str] = None
    description: Optional[str] = None
    category: Optional[str] = None
    in_metadata = False
    seen_keys: Set[str] = set()
    for index in range(1, closing_index):
        line = lines[index]
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if line.startswith(" ") or line.startswith("\t"):
            if not in_metadata:
                raise ValueError(f"unexpectedly indented frontmatter line: '{line}'")
            indented = _METADATA_INDENTED_PATTERN.match(line)
            if indented is None:
                raise ValueError(f"malformed metadata entry: '{line}'")
            if indented.group(1) == "category":
                category = indented.group(2).strip()
            continue
        frontmatter = _FRONTMATTER_PATTERN.match(line)
        if frontmatter is None:
            raise ValueError(f"malformed frontmatter line: '{line}'")
        key = frontmatter.group(1)
        value = frontmatter.group(2).strip()
        if value.startswith('"') and value.endswith('"') and len(value) >= 2:
            value = value[1:-1]
        if key in seen_keys:
            raise ValueError(f"duplicate frontmatter key: '{key}'")
        seen_keys.add(key)
        if key == "metadata":
            if not value:
                in_metadata = True
                continue
            raise ValueError("'metadata' must be a mapping with indented entries")
        in_metadata = False
        if key == "name":
            name = value
        elif key == "description":
            description = value

    if not name:
        raise ValueError("frontmatter is missing required field: name")
    if not description:
        raise ValueError("frontmatter is missing required field: description")
    if len(name) > 64:
        raise ValueError(f"name must be at most 64 characters (got {len(name)})")
    if SKILL_NAME_PATTERN.fullmatch(name) is None:
        raise ValueError(f"name '{name}' is invalid: lowercase letters, numbers, and single hyphens only")
    if name != expected_directory:
        raise ValueError(f"name '{name}' does not match skill directory '{expected_directory}'")
    if len(description) > 1024:
        raise ValueError(f"description must be at most 1024 characters (got {len(description)})")
    for field in (name, description):
        if ASCII_PATTERN.fullmatch(field) is None:
            raise ValueError(f"frontmatter field must be ASCII-only: '{field}'")
    if category and category not in ALLOWED_SKILL_CATEGORIES:
        allowed = ", ".join(ALLOWED_SKILL_CATEGORIES)
        raise ValueError(f"unknown category '{category}' (allowed: {allowed})")
    if not category:
        category = DEFAULT_SKILL_CATEGORY

    return SkillMetadata(
        name=name,
        description=description,
        category=category,
        directory=expected_directory,
        path=str(skill_path),
    )


def get_skill_metadata_list(skills_root: Path) -> List[SkillMetadata]:
    """Read every visible skill directory's metadata in name order."""
    skills: List[SkillMetadata] = []
    directories = sorted(
        entry.name for entry in skills_root.iterdir() if entry.is_dir() and not entry.name.startswith(".")
    )
    for directory in directories:
        skills.append(read_skill_metadata(skills_root / directory / "SKILL.md", directory))
    return skills


def skills_index_content(skills: List[SkillMetadata]) -> str:
    """Build the generated skills-index content; the bytes are deterministic."""
    builder: List[str] = []
    builder.append("<!-- DO NOT EDIT - generated by scripts/generate_skills_index.py. -->")
    builder.append("<!-- Regenerate: uv run scripts/generate_skills_index.py -->")
    builder.append("")
    builder.append("# Skills Index")
    builder.append("")
    for category in ALLOWED_SKILL_CATEGORIES:
        builder.append(f"## {category} Skills")
        builder.append("")
        builder.append("| Skill | When to Use |")
        builder.append("| --- | --- |")
        entries = sorted((skill for skill in skills if skill.category == category), key=lambda skill: skill.name)
        for entry in entries:
            builder.append(f"| [{entry.name}](./{entry.directory}/SKILL.md) | {entry.description} |")
        builder.append("")
    return normalize_text("\n".join(builder) + "\n")
