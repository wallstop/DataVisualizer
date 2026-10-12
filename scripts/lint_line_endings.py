#!/usr/bin/env python3
"""Line-ending contract: the ending CSharpier writes must be the ending git
checks out, on every platform.

Typed Python port of ``lint-line-endings.js`` (issue #171): the same
attribute resolution model, glob subset, fail-closed refusals, and report
lines, driven by the standard library.

``.editorconfig`` and ``.gitattributes`` each describe the working tree, and
they disagreed. ``.editorconfig`` required CRLF for every file,
``.gitattributes`` set ``* text=auto`` with no ``eol``, so git stored LF and
checked out the platform default. CSharpier follows ``.editorconfig``, so
``csharpier -- check`` passed only on Windows and reported every tracked C#
file on Linux and macOS, and the pre-commit hook rewrote staged files to an
ending git normalized straight back so the change never landed. No CI job
ran CSharpier, so the drift was silent. This check fails closed on that
class of drift.

Every path either configuration names is resolved on both sides and the two
endings must be equal, so a narrowed ``.editorconfig`` section cannot
disagree with a broad ``.gitattributes`` rule any more than a broad section
can disagree with a narrow rule. Resolving both directions needs one
representative path per ``.editorconfig`` section that declares an ending
and one per ``.gitattributes`` rule, and one per ``{a,b}`` alternative of
each, because a pattern whose later alternatives are never compared hides
the drift behind them.

Resolution follows git's own attribute model: each attribute takes the value
of the last matching rule that sets it, so a later ``text`` and an earlier
``eol`` combine. A path git treats as binary (``-text``) needs no ending. A
rule that sets ``eol`` without ``text`` is rejected because git ignores it.

Only the glob subset the two files use is supported: ``*``, ``?``, and
``{a,b}`` alternation, where a pattern without a slash matches a basename at
any depth. A recursive ``**``, an anchored or directory-only ``/``, a
character class, and a backslash escape are refused instead of guessed,
because a matcher that reads one as literal text silently stops matching and
would hide the drift behind the rule.

'--verbose' prints the resolved canonical ending and the compared paths.
Exit codes: 0 = in sync, 1 = drift, a malformed rule, or an unreadable
input.

'LINE_ENDINGS_EDITORCONFIG' and 'LINE_ENDINGS_GITATTRIBUTES' override the
inputs so the self-test can point at a fixture tree. Nothing in CI sets
them.
"""

from __future__ import annotations

import os
import posixpath
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Tuple, Union

REPO_ROOT = Path(__file__).resolve().parent.parent

ENDING_NAMES = {"lf", "crlf"}
DEFAULT_GLOB = "*"
SAMPLE_NAME = "sample"

ESCAPE_PATTERN = re.compile(r"[.*+?^${}()|[\]\\]")


def read_file_or_throw(file_path: Path, purpose: str) -> str:
    """Read a file, failing closed with the shared message shape."""
    try:
        # Every line is trimmed before it is parsed, and trimming already
        # drops the byte-order mark some editors put on line one.
        return file_path.read_text(encoding="utf-8")
    except OSError:
        raise ValueError(f"Cannot {purpose}: '{file_path}' is missing or unreadable.") from None


def display_path(file_path: Path) -> str:
    """Show a repo-relative path when inside the repository, else the absolute path."""
    try:
        relative = os.path.relpath(file_path, REPO_ROOT).replace(os.sep, "/")
    except ValueError:
        # A different drive on Windows cannot be relativized; show the
        # absolute path the way the original did.
        return str(file_path)
    return str(file_path) if relative.startswith("../") else relative


def expand_braces(glob: str) -> List[str]:
    """Split one ``{a,b,c}`` glob into every alternative, including nested groups.

    Splitting on the first ``}`` would leave a nested group as literal text
    and then match nothing, so the closing brace is found by depth.
    """
    open_index = glob.find("{")
    if open_index < 0:
        return [glob]
    depth = 0
    close_index = -1
    for index in range(open_index, len(glob)):
        if glob[index] == "{":
            depth += 1
        elif glob[index] == "}":
            depth -= 1
            if depth == 0:
                close_index = index
                break
    if close_index < 0:
        return [glob]
    prefix = glob[:open_index]
    suffix = glob[close_index + 1 :]
    alternatives: List[str] = []
    start = open_index + 1
    depth = 0
    for index in range(open_index + 1, close_index + 1):
        character = glob[index]
        if character == "{":
            depth += 1
        elif character == "}":
            depth -= 1
        elif character == "," and depth == 0:
            alternatives.append(glob[start:index])
            start = index + 1
    alternatives.append(glob[start:close_index])
    expanded: List[str] = []
    for alternative in alternatives:
        expanded.extend(expand_braces(f"{prefix}{alternative}{suffix}"))
    return expanded


def refuse_unsupported_glob(glob: str, origin: str) -> None:
    """Refuse glob constructs this matcher cannot resolve exactly.

    A pattern that uses one would be read as literal text and would silently
    stop matching the files git or ``.editorconfig`` will actually apply it
    to.
    """
    if "**" in glob:
        raise ValueError(f"{origin} uses a recursive '**' segment in '{glob}', which this check cannot match.")
    if glob.startswith("/") or glob.endswith("/"):
        raise ValueError(f"{origin} uses an anchored or directory-only '/' in '{glob}', which this check cannot match.")
    if "[" in glob:
        raise ValueError(f"{origin} uses a character class in '{glob}', which this check cannot match.")
    if "\\" in glob:
        raise ValueError(f"{origin} uses a backslash escape in '{glob}', which this check cannot match.")


def compile_glob(glob: str, origin: str) -> "re.Pattern[str]":
    """Compile one brace-free glob into an anchored matcher."""
    refuse_unsupported_glob(glob, origin)
    source = "^"
    for character in glob:
        if character == "*":
            source += "[^/]*"
        elif character == "?":
            source += "[^/]"
        else:
            source += ESCAPE_PATTERN.sub(lambda match: f"\\{match.group(0)}", character)
    return re.compile(f"{source}$")


def glob_matches(glob: str, file_path: str, origin: str) -> bool:
    """Match one glob against a path, falling back to the basename."""
    for alternative in expand_braces(glob):
        expression = compile_glob(alternative, origin)
        if expression.search(file_path):
            return True
        if "/" not in alternative and expression.search(posixpath.basename(file_path)):
            return True
    return False


def sample_paths(glob: str, origin: str) -> List[str]:
    """Build one representative path per brace alternative.

    ``*.cs`` becomes ``sample.cs``, ``{*.md,*.ps1}`` becomes both
    ``sample.md`` and ``sample.ps1``, a literal path stays itself, and a bare
    ``*`` becomes an extensionless sample. Taking only the first alternative
    would leave the rest uncompared and hide the drift behind them.
    """
    samples: List[str] = []
    for alternative in expand_braces(glob):
        refuse_unsupported_glob(alternative, origin)
        samples.append(alternative.replace("*", SAMPLE_NAME) if "*" in alternative else alternative)
    return samples


def normalize_ending(value: str, origin: str) -> str:
    """Require a known ending name."""
    ending = value.strip().lower()
    if ending not in ENDING_NAMES:
        raise ValueError(f"{origin} names the unknown line ending '{value}'; use lf or crlf.")
    return ending


@dataclass
class EditorSection:
    """One ``.editorconfig`` section: its glob, origin line, and properties."""

    glob: str
    line_number: int
    properties: Dict[str, str]


@dataclass
class AttributeRule:
    """One ``.gitattributes`` rule: its pattern, origin line, and attributes."""

    pattern: str
    line_number: int
    attributes: Dict[str, Union[str, bool]]


@dataclass(frozen=True)
class ResolvedEditorEnding:
    """One editor-config ending and the section line that declared it."""

    ending: str
    line_number: int


@dataclass(frozen=True)
class ResolvedGitEnding:
    """One git checkout resolution: a binary flag, an ending, and the rule line."""

    binary: bool
    ending: Optional[str]
    line_number: int


def parse_editor_config(file_path: Path) -> List[EditorSection]:
    """Parse the editor configuration, requiring a '[*]' section."""
    sections: List[EditorSection] = []
    current: Optional[EditorSection] = None
    lines = read_file_or_throw(file_path, "read the editor configuration").split("\n")
    for index, raw in enumerate(lines):
        line = raw.strip()
        if len(line) == 0 or line.startswith("#") or line.startswith(";"):
            continue
        line_number = index + 1
        if line.startswith("[") and line.endswith("]"):
            current = EditorSection(glob=line[1:-1].strip(), line_number=line_number, properties={})
            sections.append(current)
            continue
        separator = line.find("=")
        if separator < 0:
            raise ValueError(f"{display_path(file_path)}:{line_number} is not a 'key = value' line.")
        # A property before the first section header is a preamble such as
        # `root = true`; it configures the file rather than any path.
        if current is not None:
            current.properties[line[:separator].strip().lower()] = line[separator + 1 :]
    if not any(section.glob == DEFAULT_GLOB for section in sections):
        raise ValueError(
            f"{display_path(file_path)} must carry a '[{DEFAULT_GLOB}]' section so every file has a "
            "declared line ending."
        )
    return sections


def resolve_editor_ending(sections: List[EditorSection], file_path: str, source: Path) -> Optional[ResolvedEditorEnding]:
    """Resolve the last matching section that sets ``end_of_line``.

    That is how ``.editorconfig`` resolves precedence.
    """
    resolved: Optional[ResolvedEditorEnding] = None
    for section in sections:
        value = section.properties.get("end_of_line")
        origin = f"{display_path(source)}:{section.line_number}"
        if value is None or not glob_matches(section.glob, file_path, origin):
            continue
        resolved = ResolvedEditorEnding(ending=normalize_ending(value, origin), line_number=section.line_number)
    return resolved


def parse_attribute(field_text: str) -> Tuple[str, Union[str, bool]]:
    """Parse one attribute token into its name and value."""
    separator = field_text.find("=")
    if separator >= 0:
        return field_text[:separator], field_text[separator + 1 :]
    if field_text.startswith("-"):
        return field_text[1:], False
    if field_text.startswith("!"):
        return field_text[1:], "unset"
    return field_text, True


def parse_git_attributes(file_path: Path) -> List[AttributeRule]:
    """Parse the git attributes into ordered rules."""
    rules: List[AttributeRule] = []
    lines = read_file_or_throw(file_path, "read the git attributes").split("\n")
    for index, raw in enumerate(lines):
        line = raw.strip()
        if len(line) == 0 or line.startswith("#"):
            continue
        fields = re.split(r"\s+", line)
        attributes: Dict[str, Union[str, bool]] = {}
        for field_text in fields[1:]:
            name, value = parse_attribute(field_text)
            attributes[name] = value
        # `binary` is git's macro for `-diff -merge -text`, so it decides the
        # ending the same way an explicit `-text` does.
        if "binary" in attributes:
            attributes["text"] = False
        rules.append(AttributeRule(pattern=fields[0], line_number=index + 1, attributes=attributes))

    return rules


def resolve_git_ending(rules: List[AttributeRule], file_path: str, source: Path) -> ResolvedGitEnding:
    """Resolve each attribute from the last matching rule that sets it.

    The ``text`` and ``eol`` answers can come from different rules. A binary
    path needs no ending at all and is reported as such.
    """
    is_binary = False
    ending: Optional[Tuple[str, int]] = None
    matched = 0
    for rule in rules:
        if not glob_matches(rule.pattern, file_path, f"{display_path(source)}:{rule.line_number}"):
            continue
        matched = rule.line_number
        if "text" in rule.attributes:
            value = rule.attributes["text"]
            is_binary = value is False or value == "unset"
        if "eol" in rule.attributes:
            ending = (str(rule.attributes["eol"]), rule.line_number)
    if is_binary:
        return ResolvedGitEnding(binary=True, ending=None, line_number=matched)
    if ending is None:
        return ResolvedGitEnding(binary=False, ending=None, line_number=matched)
    return ResolvedGitEnding(
        binary=False,
        ending=normalize_ending(ending[0], f"{display_path(source)}:{ending[1]}"),
        line_number=matched,
    )


def editor_config_path() -> Path:
    """Resolve the editor configuration, honoring the self-test override."""
    override = os.environ.get("LINE_ENDINGS_EDITORCONFIG")
    return Path(os.path.abspath(override)) if override else REPO_ROOT / ".editorconfig"


def git_attributes_path() -> Path:
    """Resolve the git attributes, honoring the self-test override."""
    override = os.environ.get("LINE_ENDINGS_GITATTRIBUTES")
    return Path(os.path.abspath(override)) if override else REPO_ROOT / ".gitattributes"


def main() -> int:
    """Compare both sides' resolved endings for every governed path."""
    verbose = "--verbose" in sys.argv
    editor_config = editor_config_path()
    git_attributes = git_attributes_path()
    sections = parse_editor_config(editor_config)
    rules = parse_git_attributes(git_attributes)
    editor_name = display_path(editor_config)
    git_name = display_path(git_attributes)
    failures: List[str] = []

    base_rule = next((rule for rule in rules if rule.pattern == DEFAULT_GLOB), None)
    if base_rule is None:
        failures.append(f"'{git_name}' has no '{DEFAULT_GLOB}' rule, so no path has a declared checkout ending.")
    elif base_rule.attributes.get("text") is False or base_rule.attributes.get("text") == "unset":
        failures.append(f"'{git_name}:{base_rule.line_number}' marks every path binary.")

    for rule in rules:
        origin = f"{git_name}:{rule.line_number}"
        text = rule.attributes.get("text")
        if text is False or text == "unset":
            if "eol" in rule.attributes:
                failures.append(f"{origin} marks '{rule.pattern}' binary and also sets an ending; drop the ending.")
            continue
        if "eol" in rule.attributes and "text" not in rule.attributes:
            failures.append(f"{origin} sets an ending for '{rule.pattern}' without 'text'; git ignores it.")

    canonical = resolve_editor_ending(sections, SAMPLE_NAME, editor_config)
    if canonical is None:
        raise ValueError(
            f"{editor_name} sets no 'end_of_line' for '[{DEFAULT_GLOB}]'; CSharpier and the git "
            "checkout would each pick their own default."
        )

    candidates: List[str] = []
    for section in sections:
        if "end_of_line" in section.properties:
            candidates.extend(sample_paths(section.glob, editor_name))
    for rule in rules:
        candidates.extend(sample_paths(rule.pattern, f"{git_name}:{rule.line_number}"))
    compared = list(dict.fromkeys(candidates))

    for candidate in compared:
        editor = resolve_editor_ending(sections, candidate, editor_config)
        git = resolve_git_ending(rules, candidate, git_attributes)
        if git.binary or editor is None:
            continue
        if git.ending is None:
            failures.append(
                f"'{candidate}': git leaves the checkout ending to the platform "
                f"('{git_name}:{git.line_number}' sets no ending) but "
                f"'{editor_name}:{editor.line_number}' requires {editor.ending}."
            )
            continue
        if git.ending != editor.ending:
            failures.append(
                f"'{candidate}': git checks out {git.ending} ('{git_name}:{git.line_number}') but "
                f"'{editor_name}:{editor.line_number}' requires {editor.ending}."
            )

    if failures:
        print(f"The line-ending contract is broken. CSharpier follows '{editor_name}'; git follows '{git_name}'.", file=sys.stderr)
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        return 1

    if verbose:
        ending_rules = sum(1 for rule in rules if "text" in rule.attributes or "eol" in rule.attributes)
        print(
            f"Line-ending contract in sync: {canonical.ending} across {len(compared)} path(s) "
            f"and {ending_rules} rule(s) in '{git_name}'."
        )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
