#!/usr/bin/env python3
# Adapted from Ambiguous-Interactive/unity-helpers at edbfe9b245c2ca20c461c746566b9c46fc133457 (MIT).
"""C# source policy enforcement: member ordering (#672), nested-type placement (#575),
method naming (#47), block formatting for multi-line comments (#57), bounded LINQ-free
source areas (#61), and foreach iteration for array/string loops whose index only reads
elements (rule 21).

Typed Python port of ``lint-csharp-member-order.js`` (issue #171): the same tokenizer,
ordering tiers, conditional-region model, fixer permutation, and report lines, driven by
the standard library.

The member ordering is the owner's, from #672. Every tier below is ordered public → protected →
internal → private, including const:

  1. const
  2. events
  3. delegates
  4. static properties
  5. static fields
  6. properties
  7. fields
  8. constructors
  9. static methods
 10. methods

Two deliberate details the issue records so an implementer does not "fix" them back: static
properties come before static fields and properties before fields (the reverse of StyleCop's
SA1201 default), and const takes the accessibility ordering too. Nested types are resolved to
stay LAST (issue option 1): this gate already enforces that, 1918 files were clean under it, and
moving nested types first would invert a shipped rule and every file it swept.

The rule is the owner's, from review on #574: "Please place class/struct definitions at the end
of the file, or in their own file, do not intersperse with code." A reader scrolling a type for
a method should not have to step over a nested type to find it, and a type declaration in the
middle of a body reads as the start of a new file's worth of content. Recorded in
.llm/context.md (rule 6) and create-csharp-file (rule 5b); this is what stops it decaying (#575).

Both rules tokenize rather than grep, because the interesting words are not always declarations:
  * ``where T : class`` and ``where T : struct`` are constraints, not nested types.
  * ``record`` is contextual and is a perfectly good field or parameter name.
  * ``[Attr(new[] { 1, 2 })]`` puts a brace before the member's own body brace.
  * a field initializer -- ``private int[] _a = { 1, 2 };`` -- does the same.
  * ``private (int X, int Y) _point;`` opens a tuple type, not a parameter list. Skip
    balanced type-position tuples before looking for the method name and parameter list.
Parameter lists, attribute sections and bracketed types are skipped as balanced spans, and
comments and string literals are masked to spaces first so a brace inside either cannot count.

The ordering comparison resets at a conditional boundary: members on opposite sides of an
``#if``/``#else`` pair are never compared, because the compiler sees at most one of them. A member
that does not sit wholly inside ONE conditional region is reported but never rewritten. A member
inside ``#if UNITY_EDITOR`` is compiled only there, so moving it past the ``#endif`` changes which
build sees it -- and a slice that carries an unbalanced ``#if`` lands the directive mid-line, where
C# refuses it outright. Two files in the #575 sweep hit exactly that. Members are reordered only
when every one of them opens and closes in the same region, which leaves a conditional wholly
inside a member (the common ``#if UNITY_EDITOR`` inside a method) free to move with it.

'--fix' reorders members into the canonical order and moves each nested type to the end of its
containing type body. The rewrite is a PERMUTATION of exact source slices: every member carries
its own leading trivia (doc comment, attributes) and its trailing same-line comment, the slices
tile the body with no gaps, and the rewritten file is asserted to have the same length as the
original before it is written. A fix that changes a byte of anything other than order is a bug,
and the length check is what says so.

Exit codes: 0 = clean (or every violation fixed), 1 = at least one violation remains.
"""

from __future__ import annotations

import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Dict, List, Optional, Tuple, Union

REPO_ROOT = Path(__file__).resolve().parent.parent

# Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it.
SCAN_ROOTS = (
    [root for root in os.environ["CSHARP_MEMBER_ORDER_ROOTS"].split(os.pathsep) if root]
    if os.environ.get("CSHARP_MEMBER_ORDER_ROOTS")
    else ["Runtime", "Editor", "Tests", "Generator~"]
)

# Vendored upstream verbatim. `lint:comparison-direction` excludes it for the same reason: a local
# reordering makes the next upstream diff unreadable.
EXCLUDED_PREFIXES = ("Runtime/Utils/SevenZip",)

TYPE_DECLARATION = re.compile(r"\b(class|struct|interface|record(?:\s+(?:class|struct))?|enum)\s+(@?[A-Za-z_]\w*)")

# `class` and `struct` also introduce a generic constraint, and `where T : class where U : new()`
# puts an identifier-shaped token right where a type name would be. Every word that can legally
# follow one of the five keywords WITHOUT being a declared name is refused here.
NOT_A_TYPE_NAME = {
    "where",
    "in",  # foreach (var record in records) uses record as a contextual identifier.
    "new",
    "class",
    "struct",
    "record",
    "interface",
    "enum",
    "unmanaged",
    "notnull",
    "default",
    "null",
    "this",
    "base",
    "void",
    "delegate",
}

OPENERS = {"(": ")", "[": "]", "{": "}"}

# The characters the mask's scanner dispatches on; the scanner can jump between them because
# every other character is copied verbatim by the outer loop.
NOISE_STARTS = re.compile(r"[/'\"@$]")

SPACEY = re.compile(r"\s")


@dataclass
class Member:
    """One member slice that tiles a type body exactly."""

    start: int
    end: int
    header_start: int
    is_type: bool
    trailing: bool = False
    kind: Optional[str] = None
    name: Optional[str] = None


@dataclass
class Piece:
    """One body piece: a reordered member slice, or an immovable directive's raw text."""

    start: int = 0
    end: int = 0
    raw: Optional[str] = None


@dataclass(frozen=True)
class Edit:
    """One whole-body rewrite: the source slice [start, end) becomes replacement."""

    start: int
    end: int
    replacement: str


@dataclass(frozen=True)
class Violation:
    """One reported policy violation; the optional fields carry its naming context."""

    line: int
    kind: str
    name: Optional[str] = None
    container: Optional[str] = None
    access: Optional[str] = None
    after: Optional[str] = None
    after_access: Optional[str] = None


@dataclass(frozen=True)
class Analysis:
    """Every violation in one file, plus the rewrites that would remove them."""

    violations: List[Violation]
    edits: List[Edit]


@dataclass(frozen=True)
class TypeBody:
    """One type body: its declaration keyword, name, and the span inside its braces."""

    kind: str
    name: str
    start: int
    end: int


@dataclass(frozen=True)
class Classification:
    """One member's ordering tier and access, or the reason it cannot be classified.

    ``rank`` is the combined ordering key; the ``-1`` sentinel marks a
    classification that never reaches the ordering comparison (an
    unclassifiable member is reported and skipped instead).
    """

    tier: Optional[str] = None
    access: Optional[str] = None
    unclassifiable: bool = False
    exempt: bool = False
    rank: int = -1


def mask_noise(text: str, on_line_comment: Optional[Callable[[int], None]] = None) -> str:
    """Replace every comment and string/char literal with spaces.

    Length and newlines are preserved, so brace matching cannot be thrown by a
    brace inside either. Interpolated strings are masked whole, braces
    included, which keeps the balance the caller relies on. The scanner jumps
    between the characters it dispatches on, so the pass costs the number of
    literals and comments, not the text length.
    """
    out = list(text)

    def blank(start: int, end: int) -> None:
        if end <= start:
            return
        end = min(end, len(out))
        # \r survives masking: only the mask's own view of line content matters.
        out[start:end] = [" " if out[position] not in ("\n", "\r") else out[position] for position in range(start, end)]

    index = 0
    length = len(text)
    while index < length:
        jump = NOISE_STARTS.search(text, index)
        if jump is None:
            break
        index = jump.start()
        char = text[index]
        next = text[index + 1] if index + 1 < length else ""

        if char == "/" and next == "/":
            if on_line_comment is not None:
                on_line_comment(index)
            end = text.find("\n", index)
            end = length if end < 0 else end
            blank(index, end)
            index = end
            continue
        if char == "/" and next == "*":
            end = text.find("*/", index + 2)
            end = length if end < 0 else end + 2
            blank(index, end)
            index = end
            continue
        if char == "'":
            cursor = index + 1
            while cursor < length and text[cursor] != "'":
                cursor += 2 if text[cursor] == "\\" else 1
            blank(index, cursor + 1)
            index = cursor + 1
            continue

        verbatim = char == "@" and next == '"'
        interpolated_verbatim = (
            (char == "$" and next == "@" and index + 2 < length and text[index + 2] == '"')
            or (char == "@" and next == "$" and index + 2 < length and text[index + 2] == '"')
        )
        interpolated = char == "$" and next == '"'
        if char == '"' or verbatim or interpolated or interpolated_verbatim:
            raw = verbatim or interpolated_verbatim
            has_holes = interpolated or interpolated_verbatim
            quote = text.find('"', index)
            cursor = quote + 1
            # Interpolated strings carry C# code inside `{...}` holes, and that code can hold nested
            # strings -- `"{(x == null ? "(null)" : $"\"{y}\"")}"` -- whose quotes would otherwise end
            # the outer string early and leak braces into the balance. Track hole depth: inside a hole,
            # strings (plain, char, and nested interpolated) are skipped whole, `{` deepens and `}`
            # surfaces; outside one, `\\` escapes, `{{` is a literal brace, and the closing quote ends
            # the string.
            hole_depth = 0
            while cursor < length:
                c = text[cursor]
                if hole_depth == 0:
                    if raw:
                        if c == '"' and cursor + 1 < length and text[cursor + 1] == '"':
                            cursor += 2
                            continue
                    elif c == "\\":
                        cursor += 2
                        continue
                    if c == '"':
                        break
                    if has_holes and c == "{":
                        if cursor + 1 < length and text[cursor + 1] == "{":
                            cursor += 2
                            continue
                        hole_depth += 1
                        cursor += 1
                        continue
                    if not raw and c == "\n":
                        break
                    cursor += 1
                    continue
                if c == "'":
                    nested = cursor + 1
                    while nested < length and text[nested] != "'" and text[nested] != "\n":
                        nested += 2 if text[nested] == "\\" else 1
                    cursor = nested + 1
                    continue
                if c == '"':
                    nested = cursor + 1
                    while nested < length:
                        if not raw and text[nested] == "\\":
                            nested += 2
                            continue
                        if raw and text[nested] == '"' and nested + 1 < length and text[nested + 1] == '"':
                            nested += 2
                            continue
                        if text[nested] == '"' or text[nested] == "\n":
                            break
                        nested += 1
                    cursor = nested + 1
                    continue
                if c == "{":
                    hole_depth += 1
                    cursor += 1
                    continue
                if c == "}":
                    hole_depth -= 1
                    cursor += 1
                    continue
                if c == "\n":
                    break
                cursor += 1
            blank(index, min(cursor + 1, length))
            index = cursor + 1
            continue
        index += 1
    return "".join(out)


def region_keys(text: str) -> List[str]:
    """Build a key per index naming the conditional branch that is open there.

    Two positions share a key only when the same ``#if``/``#elif``/``#else``
    branches are active, so comparing keys answers "would moving this change
    which build compiles it".
    """
    keys: List[str] = [""] * (len(text) + 1)
    stack: List[int] = []
    issued = 0
    index = 0

    while index <= len(text):
        newline = text.find("\n", index)
        line_end = len(text) if newline < 0 else newline + 1
        line = text[index:line_end].strip()

        if line.startswith("#if"):
            issued += 1
            stack.append(issued)
        elif line.startswith("#elif") or line.startswith("#else"):
            issued += 1
            if stack:
                stack[-1] = issued
            else:
                stack.append(issued)
        elif line.startswith("#endif"):
            if stack:
                stack.pop()

        key = "/".join(str(item) for item in stack)
        keys[index:line_end] = [key] * (line_end - index)
        if newline < 0 or len(text) <= newline:
            keys[len(text)] = key
            break
        index = line_end
    return keys


def skip_balanced(masked: str, open_index: int) -> int:
    """Return the index just past the span opened at ``open_index``, or -1 when it never closes."""
    stack = [OPENERS[masked[open_index]]]
    for index in range(open_index + 1, len(masked)):
        char = masked[index]
        if char in OPENERS:
            stack.append(OPENERS[char])
            continue
        if char == stack[-1]:
            stack.pop()
            if not stack:
                return index + 1
    return -1


def end_of_line_after(text: str, masked: str, end: int) -> int:
    """Extend a member's end through a trailing same-line comment and its newline."""
    cursor = end
    while cursor < len(text) and text[cursor] in (" ", "\t"):
        cursor += 1
    if cursor < len(masked) and masked[cursor] in (" ", "\n", "\r"):
        # Whitespace in the mask where the source has content is a comment; either way the rest of the
        # line belongs to the member that just ended, not to the one that starts on the next line.
        while cursor < len(text) and text[cursor] != "\n":
            if masked[cursor] not in (" ", "\r"):
                return end
            cursor += 1
        return cursor + 1 if cursor < len(text) else len(text)
    return end


def members_of(text: str, masked: str, body_start: int, body_end: int) -> Optional[List[Member]]:
    """Split a type body into members that tile it exactly.

    Each member owns its leading trivia and ends at its own terminator, so
    concatenating the members in any order reproduces valid source.
    """
    members: List[Member] = []
    cursor = body_start

    while cursor < body_end:
        # Trivia and whitespace belong to the member that follows them.
        scan = cursor
        while scan < body_end and SPACEY.search(masked[scan]):
            scan += 1
        if body_end <= scan:
            break

        header_start = scan
        body_brace = -1
        terminator = -1
        while scan < body_end:
            char = masked[scan]
            if char == "(" or char == "[":
                close = skip_balanced(masked, scan)
                if close < 0:
                    return None
                scan = close
                continue
            if char == "{":
                body_brace = scan
                break
            if char == ";":
                terminator = scan
                break
            scan += 1

        end: int
        if terminator >= 0:
            end = terminator + 1
        elif body_brace >= 0:
            close = skip_balanced(masked, body_brace)
            if close < 0:
                return None
            # Extend through an expression tail: a body-opening brace is not always the end of the
            # statement. `} = 5;` (accessor initializer), `}.ToImmutableHashSet();` (object-initializer
            # chain), `});` (initializer inside a call argument) all continue past the brace. A member
            # declared next can never begin with one of these characters, so they always mean "the
            # statement goes on": scan to the terminating `;` at bracket depth zero.
            tail_cursor = close
            for _ in range(8):
                after = tail_cursor
                while after < body_end and SPACEY.search(masked[after]):
                    after += 1
                char = masked[after] if after < len(masked) else ""
                if char == ";":
                    tail_cursor = after + 1
                    break
                if char == "" or char not in ".)+=,?:]":
                    break
                reached = False
                inner_scan = after
                while inner_scan < body_end:
                    c = masked[inner_scan]
                    if c == "(" or c == "[" or c == "{":
                        inner = skip_balanced(masked, inner_scan)
                        if inner < 0:
                            return None
                        inner_scan = inner
                        continue
                    if c == ";":
                        tail_cursor = inner_scan + 1
                        reached = True
                        break
                    inner_scan += 1
                if not reached:
                    return None
            after = tail_cursor
            while after < body_end and SPACEY.search(masked[after]):
                after += 1
            if after < len(masked) and masked[after] == ";":
                end = after + 1
            else:
                end = tail_cursor
        else:
            # No terminator before the body closes. That is a preprocessor line, an attribute on the
            # closing brace, or source this cannot parse; it becomes trailing trivia rather than
            # discarding the whole body, because a body this cannot split is a body it silently stops
            # checking (#556).
            members.append(Member(start=cursor, end=body_end, header_start=header_start, is_type=False, trailing=True))
            return members

        end = end_of_line_after(text, masked, min(end, body_end))
        if end <= cursor:
            return None

        header = masked[header_start : body_brace if body_brace >= 0 else end]
        match = TYPE_DECLARATION.search(header)
        if match is not None and match.group(2) not in NOT_A_TYPE_NAME:
            is_type = True
            kind: Optional[str] = match.group(1)
            type_name: Optional[str] = match.group(2)
        else:
            is_type = False
            kind = None
            type_name = None

        members.append(
            Member(
                start=cursor,
                end=end,
                header_start=header_start,
                is_type=is_type,
                kind=kind,
                name=type_name,
            )
        )
        cursor = end

    if cursor < body_end:
        members.append(Member(start=cursor, end=body_end, header_start=cursor, is_type=False, trailing=True))
    return members


# The #672 member ordering. Each tier is ordered public → protected → internal → private, including
# const; `none` is the absence of an access modifier (a static constructor, an implicitly-local
# member) and sorts after every named access within its tier.
ORDER_TIERS = (
    "const",
    "event",
    "delegate",
    "static property",
    "static field",
    "property",
    "field",
    "constructor",
    "static method",
    "method",
)
TIER_RANK = {tier: index for index, tier in enumerate(ORDER_TIERS)}
ACCESS_RANK = {"public": 0, "protected": 1, "internal": 2, "private": 3, "none": 4}

MODIFIER_KEYWORDS = {
    "public",
    "protected",
    "internal",
    "private",
    "static",
    "readonly",
    "const",
    "volatile",
    "new",
    "virtual",
    "override",
    "abstract",
    "sealed",
    "unsafe",
    "extern",
    "async",
    "partial",
    "required",
    "file",
    "ref",
    "event",
}


@dataclass(frozen=True)
class DeclarationPrefix:
    """A member's declaration prefix and the character that terminated it."""

    prefix: str
    break_char: str
    break_index: int


def declaration_prefix(masked: str, start: int, end: int) -> Optional[DeclarationPrefix]:
    """Scan a member's declaration prefix through its header terminator.

    Everything from the first token through the character that opens its body
    or terminates its header. Balanced ``[...]`` spans (attributes, an
    indexer's parameter brackets) and type-position tuples are skipped whole. Returns None when a span
    never closes (the member is reported through the placement rules instead).
    """
    scan = start
    while scan < end:
        char = masked[scan]
        if char == "(" and callable_name_before(masked, scan) is None:
            # A tuple in a return/member type is not a method parameter list.
            close = skip_balanced(masked, scan)
            if close < 0:
                return None
            scan = close
            continue
        if char in "{(=;":
            return DeclarationPrefix(prefix=masked[start:scan].strip(), break_char=char, break_index=scan)
        if char == "[":
            close = skip_balanced(masked, scan)
            if close < 0:
                return None
            scan = close
            continue
        scan += 1
    return DeclarationPrefix(prefix=masked[start:end].strip(), break_char="", break_index=end)


def is_plain_equals(masked: str, index: int) -> bool:
    """True when ``=`` at ``index`` starts a field initializer rather than being part of
    ``==``, ``=>``, ``+=``, ``<=`` or ``>=``."""
    previous = masked[index - 1] if index - 1 >= 0 else ""
    if previous and previous in "=!<>=+-*/%&|^?:":
        return False
    return index + 1 >= len(masked) or masked[index + 1] not in ("=", ">")


def accessor_kind(masked: str, start: int, end: int) -> Optional[str]:
    """Decide property or field for a member that did not break at a ``(``.

    Both end in ``;`` and can both carry an initializer, so the first
    body-opening token decides: an accessor list ``{`` or an arrow ``=>``
    makes a property, a real ``=`` initializer or a bare ``;`` makes a field.
    A method never reaches here (it broke at ``(``), and an expression-bodied
    method's ``(`` always precedes its arrow.
    """
    scan = start
    while scan < end:
        char = masked[scan]
        if char == "[":
            close = skip_balanced(masked, scan)
            if close < 0:
                return None
            scan = close - 1
            continue
        if char == "=":
            if scan + 1 < len(masked) and masked[scan + 1] == ">":
                return "property"
            if is_plain_equals(masked, scan):
                return "field"
            continue
        if char == "{":
            return "property"
        if char == ";":
            return "field"
        scan += 1
    return "field"


def callable_name_before(masked: str, paren_index: int) -> Optional[str]:
    """Return the identifier (or ``~Identifier``) immediately before the member's opening paren."""
    cursor = paren_index - 1
    while cursor >= 0 and SPACEY.search(masked[cursor]):
        cursor -= 1
    if cursor >= 0 and masked[cursor] == ">":
        depth = 1
        cursor -= 1
        while cursor >= 0 and depth > 0:
            if masked[cursor] == ">":
                depth += 1
            elif masked[cursor] == "<":
                depth -= 1
            cursor -= 1
        if depth != 0:
            return None
        while cursor >= 0 and SPACEY.search(masked[cursor]):
            cursor -= 1
    end = cursor + 1
    while cursor >= 0 and re.search(r"[A-Za-z0-9_]", masked[cursor]):
        cursor -= 1
    if cursor >= 0 and masked[cursor] == "~":
        cursor -= 1
    name = masked[cursor + 1 : end]
    return name if re.fullmatch(r"[A-Za-z_]\w*", name) and name not in MODIFIER_KEYWORDS else None


def classify_member(masked: str, member: Member, type_name: str) -> Classification:
    """Classify one non-type member into its ordering tier and access.

    ``type_name`` is the containing type's name; a declaration prefix whose
    last identifier is that name (or ``~`` + that name) is a constructor.
    """
    prefix = declaration_prefix(masked, member.header_start, member.end)
    if prefix is None:
        return Classification(unclassifiable=True)
    access = (
        "public"
        if re.search(r"(?<!@)\bpublic\b", prefix.prefix)
        else "protected"
        if re.search(r"(?<!@)\bprotected\b", prefix.prefix)
        else "internal"
        if re.search(r"(?<!@)\binternal\b", prefix.prefix)
        else "private"
        if re.search(r"(?<!@)\bprivate\b", prefix.prefix)
        else "none"
    )
    is_static = re.search(r"(?<!@)\bstatic\b", prefix.prefix) is not None
    is_const = re.search(r"(?<!@)\bconst\b", prefix.prefix) is not None

    if re.search(r"(?<!@)\bevent\b", prefix.prefix):
        return Classification(tier="event", access=access)
    if re.search(r"(?<!@)\bdelegate\b", prefix.prefix):
        return Classification(tier="delegate", access=access)

    if is_const:
        return Classification(tier="const", access=access)

    if prefix.break_char == "(":
        if re.search(r"(\boperator\b|\bimplicit\b|\bexplicit\b)", prefix.prefix):
            return Classification(tier="static method" if is_static else "method", access=access)
        last_ident_match = re.search(r"~?[A-Za-z_]\w*\s*$", prefix.prefix)
        last_ident = last_ident_match.group(0).strip() if last_ident_match else None
        if last_ident is not None and (last_ident == type_name or last_ident == f"~{type_name}"):
            return Classification(tier="constructor", access=access)
        return Classification(tier="static method" if is_static else "method", access=access)

    if prefix.break_char == "{":
        return Classification(tier="static property" if is_static else "property", access=access)

    kind = accessor_kind(masked, member.header_start, member.end)
    if kind is None:
        return Classification(unclassifiable=True)
    if kind == "property":
        return Classification(tier="static property" if is_static else "property", access=access)
    return Classification(tier="static field" if is_static else "field", access=access)


def member_rank(masked: str, member: Member, type_name: str) -> Classification:
    """Return the ordering rank used for both comparison and the stable reordering."""
    classified = classify_member(masked, member, type_name)
    if classified.exempt or classified.unclassifiable:
        return classified
    return Classification(
        tier=classified.tier,
        access=classified.access,
        rank=TIER_RANK[str(classified.tier)] * 8 + ACCESS_RANK[str(classified.access)],
    )



def type_bodies(masked: str) -> List[TypeBody]:
    """Return every type body in a file, outermost first.

    Nested types are bodies too, so this does not skip past one it has just
    found -- the scan continues inside it.
    """
    bodies: List[TypeBody] = []
    position = 0
    while (match := TYPE_DECLARATION.search(masked, position)) is not None:
        position = match.end()
        if match.group(2) in NOT_A_TYPE_NAME:
            continue
        cursor = match.end()
        open_index = -1
        while cursor < len(masked):
            char = masked[cursor]
            if char == "(" or char == "[":
                close = skip_balanced(masked, cursor)
                if close < 0:
                    break
                cursor = close
                continue
            if char == "{":
                open_index = cursor
                break
            if char == ";" or char == "}":
                break
            cursor += 1
        if open_index < 0:
            continue
        close = skip_balanced(masked, open_index)
        if close < 0:
            continue
        bodies.append(TypeBody(kind=match.group(1), name=match.group(2), start=open_index + 1, end=close - 1))
    bodies.sort(key=lambda body: (body.start, -body.end))
    return bodies


def line_of(text: str, index: int) -> int:
    """Return the 1-based line that holds character offset ``index``."""
    return text.count("\n", 0, index) + 1


def multiline_line_comment_violations(text: str) -> List[Violation]:
    """Report consecutive ordinary ``//`` blocks, excluding XML docs and tool directives."""
    lines = text.split("\n")
    violations: List[Violation] = []
    comments: Dict[int, bool] = {}
    line = 1
    line_start = 1 if text and ord(text[0]) == 0xFEFF else 0

    def on_line_comment(comment_start: int) -> None:
        nonlocal line, line_start
        while line_start <= comment_start:
            newline = text.find("\n", line_start)
            if newline < 0 or comment_start < newline:
                break
            line += 1
            line_start = newline + 1

        content_start = comment_start + 2
        if re.fullmatch(r"[ \t]*", text[line_start:comment_start]) and not (
            content_start < len(text) and text[content_start] == "/"
        ):
            line_end = text.find("\n", content_start)
            content = text[content_start : len(text) if line_end < 0 else line_end]
            comments[line] = re.search(r"^[ \t]*(?:ReSharper\b|noinspection\b)", content) is not None

    mask_noise(text, on_line_comment)

    index = 1
    while index <= len(lines):
        if index not in comments:
            index += 1
            continue

        start = index
        while index <= len(lines) and index in comments:
            index += 1
        block_length = index - start
        all_tool_directives = True
        for comment_line in range(start, index):
            all_tool_directives = all_tool_directives and comments[comment_line]
        if 1 < block_length and not all_tool_directives:
            violations.append(Violation(line=start, kind="multiline line comment"))

    return violations


def prohibited_linq_violations(text: str) -> List[Violation]:
    """Report ``System.Linq`` references in LINQ-free source areas, excluding comments and literals."""
    masked = mask_noise(text)
    violations: List[Violation] = []
    for match in re.finditer(r"\bSystem\s*\.\s*Linq\b", masked):
        violations.append(Violation(line=line_of(text, match.start()), kind="prohibited LINQ dependency"))
    return violations


ASSIGNMENT_OPERATOR_AFTER_ELEMENT = {
    "=",
    "+=",
    "-=",
    "*=",
    "/=",
    "%=",
    "&=",
    "|=",
    "^=",
}


def counted_loop_violations(text: str) -> List[Violation]:
    """Report counted loops whose index only reads elements of a value-enumerator collection.

    Rule 21: arrays and other value-enumerator collections iterate by foreach
    unless the loop needs the index. The safe mechanical subset:

        for (int V = 0; V < E.Length; V++)

    is flagged when the header has this exact simple shape and every
    occurrence of V in the braced body is a pure element read ``E[V]`` of the
    same E in a non-assignment position. ``.Count`` bounds are deliberately
    skipped: a counted loop is REQUIRED for IReadOnlyList<T>/IList<T>
    receivers (interface enumerators allocate, rule 21) and a source check
    cannot see the declared type behind ``.Count``.
    """
    masked = mask_noise(text)
    violations: List[Violation] = []
    for match in re.finditer(r"\bfor\s*\(", masked):
        open_index = match.end() - 1
        close = skip_balanced(masked, open_index)
        if close < 0:
            continue
        # skipBalanced returns the index just past the closing parenthesis.
        if counted_loop_index_is_pure_element_read(masked, open_index, close - 1):
            violations.append(Violation(line=line_of(text, match.start()), kind="counted loop over value enumerator"))
    return violations


def counted_loop_index_is_pure_element_read(masked: str, open_index: int, close: int) -> bool:
    """Check the counted-loop header shape and its body's pure element reads."""
    parts = [part.strip() for part in masked[open_index + 1 : close].split(";")]
    if len(parts) != 3:
        return False
    declared = re.fullmatch(r"int\s+([A-Za-z_]\w*)\s*=\s*0", parts[0])
    if declared is None:
        return False
    variable = declared.group(1)
    bound = re.fullmatch(rf"{variable}\s*<\s*([A-Za-z_]\w*)\s*\.\s*Length", parts[1])
    if bound is None:
        return False
    collection = bound.group(1)
    increment = re.fullmatch(
        rf"(?:{variable}\+\+|\+\+{variable}|{variable}\s*\+=\s*1|{variable}\s*=\s*{variable}\s*\+\s*1)",
        parts[2],
    )
    if increment is None:
        return False

    cursor = close + 1
    while cursor < len(masked) and SPACEY.search(masked[cursor]):
        cursor += 1
    if cursor >= len(masked) or masked[cursor] != "{":
        return False
    body_close = skip_balanced(masked, cursor)
    if body_close < 0:
        return False
    body = masked[cursor + 1 : body_close - 1]

    occurrence = re.compile(rf"(?<![\w]){variable}(?![\w])")
    found = 0
    for element_match in occurrence.finditer(body):
        found += 1
        if not is_pure_element_read(masked, cursor + 1 + element_match.start(), collection):
            return False
    # A body that reassigns the collection or hands it to ref/out makes foreach read a different
    # sequence than the counted loop; treat that as needing the counted form. (`in` cannot
    # reassign its argument, so it stays legal; a nested `foreach (x in E)` is also just a read.)
    mutated_collection = re.search(
        rf"(?<![\w.]){collection}(?!\w)\s*(?:=(?!=)|\+=|-=|\*=|/=|%=|&=|\|=|\^=|\+\+|--)|\b(?:ref|out)\s+{collection}(?!\w)",
        body,
    )
    return found > 0 and mutated_collection is None


def is_pure_element_read(masked: str, at: int, collection: str) -> bool:
    """Require the loop variable's occurrence at ``at`` to be the read ``collection[at]``."""
    cursor = at - 1
    while cursor >= 0 and SPACEY.search(masked[cursor]):
        cursor -= 1
    if cursor < 0 or masked[cursor] != "[":
        return False
    name_end = cursor - 1
    while name_end >= 0 and SPACEY.search(masked[name_end]):
        name_end -= 1
    name_start = name_end
    while name_start >= 0 and re.search(r"[A-Za-z0-9_]", masked[name_start]):
        name_start -= 1
    if masked[name_start + 1 : name_end + 1] != collection:
        return False
    keyword_end = name_start
    while keyword_end >= 0 and SPACEY.search(masked[keyword_end]):
        keyword_end -= 1
    preceding_word = re.search(r"\w+$", masked[0 : keyword_end + 1])
    if preceding_word is not None and re.fullmatch(r"(?:out|ref|in)", preceding_word.group(0)):
        return False

    cursor = at + variable_width(masked, at)
    while cursor < len(masked) and SPACEY.search(masked[cursor]):
        cursor += 1
    if cursor >= len(masked) or masked[cursor] != "]":
        return False
    after = cursor + 1
    while after < len(masked) and SPACEY.search(masked[after]):
        after += 1
    two = masked[after : after + 2]
    if two == "++" or two == "--":
        return False
    if two in ASSIGNMENT_OPERATOR_AFTER_ELEMENT:
        return False
    if after < len(masked) and masked[after] == "=" and (after + 1 >= len(masked) or masked[after + 1] != "="):
        return False
    return True


def variable_width(masked: str, at: int) -> int:
    """Return the width of the identifier occurrence at ``at`` on already-masked text."""
    end = at
    while end < len(masked) and re.search(r"[A-Za-z0-9_]", masked[end]):
        end += 1
    return end - at


def member_name(masked: str, member: Member) -> Optional[str]:
    """Return the member's declaring identifier, for the violation message."""
    prefix = declaration_prefix(masked, member.header_start, member.end)
    if prefix is None:
        return None
    if prefix.break_char == "(":
        return callable_name_before(masked, prefix.break_index)
    last = re.search(r"~?[A-Za-z_]\w*\s*$", prefix.prefix)
    return last.group(0).strip() if last is not None else None


def directive_prefix_end(text: str, start: int, end: int) -> int:
    """Return the index just past a member's leading full directive lines, or 0.

    If a member's slice opens with full directive lines (``#if``, ``#endif``,
    ``#pragma``, ... -- the leading trivia ``membersOf`` fused onto it), the
    fixer holds the directives in place while the member itself reorders.
    """
    cursor = start
    last = 0
    while cursor < end:
        scan = cursor
        while scan < end and text[scan] in (" ", "\t", "\r", "\n"):
            scan += 1
        if scan < end and text[scan] == "#":
            newline = text.find("\n", scan)
            if newline < 0 or end <= newline:
                return last
            cursor = newline + 1
            last = cursor
            continue
        break
    return last


def analyze_file(text: str, prohibit_linq: bool = False) -> Analysis:
    """Collect every violation in one file, plus the rewrite that removes them.

    Three rules produce violations here: the #47 method naming, #672 member
    ordering, and #575 nested-type placement. When every member of a body
    sits in one conditional region and classifies cleanly, one edit reorders
    the whole body (members stable-sorted into the canonical tiers, nested
    types appended after them). Otherwise the ordering is still compared --
    with the baseline reset at every conditional boundary, so members on
    opposite sides of an ``#if`` are never compared -- and the nested types
    fall back to the targeted move that keeps every ``#if`` member in place.
    """
    masked = mask_noise(text)
    keys = region_keys(text)
    bodies = type_bodies(masked)
    violations = multiline_line_comment_violations(text)
    if prohibit_linq:
        violations.extend(prohibited_linq_violations(text))
    violations.extend(counted_loop_violations(text))
    edits: List[Edit] = []

    for body in bodies:
        if body.kind == "enum":
            continue
        members = members_of(text, masked, body.start, body.end)
        if members is None:
            continue

        # --- #672 member ordering -------------------------------------------------------------
        ordered_members = [member for member in members if not member.is_type and not member.trailing]
        ordering_offenders: List[Tuple[Member, int]] = []
        unclassifiable = 0

        # --- #47 method naming ---------------------------------------------------------------
        for member in ordered_members:
            classified = classify_member(masked, member, body.name)
            if classified.tier not in ("method", "static method"):
                continue
            name = member_name(masked, member)
            if name is not None and "_" in name:
                violations.append(
                    Violation(
                        line=line_of(text, member.header_start),
                        kind="underscored method",
                        name=name,
                        container=body.name,
                    )
                )

        previous_rank = -1
        previous_region: Optional[str] = None
        previous_tier: Optional[str] = None
        previous_access: Optional[str] = None
        for member in ordered_members:
            classified = member_rank(masked, member, body.name)
            if classified.unclassifiable:
                unclassifiable += 1
                continue
            region = keys[member.header_start]
            if region != previous_region:
                previous_rank = -1
                previous_region = region
            if previous_rank >= 0 and classified.rank < previous_rank:
                violations.append(
                    Violation(
                        line=line_of(text, member.header_start),
                        kind=str(classified.tier),
                        access=classified.access,
                        name=member_name(masked, member),
                        container=body.name,
                        after=previous_tier,
                        after_access=previous_access,
                    )
                )
                ordering_offenders.append((member, classified.rank))
            # Adjacent comparison: each member is judged against the one before it, which is what the
            # violation message claims. A scrambled body reports each adjacent descent; the fix
            # reorders the body once.
            previous_rank = classified.rank
            previous_tier = classified.tier
            previous_access = classified.access
        if unclassifiable != 0:
            violations.append(Violation(line=line_of(text, body.start), kind="unclassified member", name=None, container=body.name))

        last_non_type = max(
            (index for index, member in enumerate(members) if not member.is_type and not member.trailing),
            default=-1,
        )
        has_type_offenders = any(
            member.is_type and index < last_non_type for index, member in enumerate(members)
        )
        if len(ordering_offenders) == 0 and not has_type_offenders:
            continue

        if len(ordering_offenders) > 0:
            if any(edit.start <= body.start and body.end <= edit.end for edit in edits):
                continue
            # Reorder the body in RUNS. A run is a maximal stretch of classifiable non-type members
            # between barriers; everything else -- a nested type, a trailing `#endif`, a `#pragma
            # warning restore`, a member that straddles an `#if` boundary -- is a barrier that keeps
            # its exact position, because moving it changes which build compiles it or the scope of
            # what it scopes. Directives fused into a member's leading trivia are split into their own
            # barrier chunk first, so the member itself can still sort within its side of the
            # conditional. Each run stable-sorts into the canonical tiers, the permutation never
            # crosses a directive line, and the pieces still tile the body byte for byte.
            changed = False
            pieces: List[Piece] = []
            run: List[Member] = []

            def flush_run() -> None:
                nonlocal changed, run
                if 1 < len(run):
                    decorated = sorted(
                        ((member, index, member_rank(masked, member, body.name).rank) for index, member in enumerate(run)),
                        key=lambda entry: (entry[2], entry[1]),
                    )
                    sorted_members = [entry[0] for entry in decorated]
                    if any(sorted_members[index] is not run[index] for index in range(len(run))):
                        changed = True
                    pieces.extend(Piece(start=member.start, end=member.end) for member in sorted_members)
                else:
                    pieces.extend(Piece(start=member.start, end=member.end) for member in run)
                run = []

            # Static members whose initializer runs real code at type-initialization time are
            # initialization-order hazards: static initializers execute in textual order, so moving
            # such a member past another static member it can read changes what is null when it runs
            # (#672: `Instance = new()` before the permutation table it copies was a standalone-leg
            # NRE). A self-typed construction, or an initializer that names another member of this
            # body, is treated as able to read any of them and becomes a barrier.
            static_member_names: List[str] = []
            for member in members:
                if member.is_type or member.trailing:
                    continue
                prefix = declaration_prefix(masked, member.header_start, member.end)
                if prefix is None:
                    continue
                name_match = re.search(r"([A-Za-z_]\w*)\s*(<[^<>]*>)?\s*$", prefix.prefix)
                if name_match is not None:
                    static_member_names.append(name_match.group(1))

            def initializes_at_type_load(member: Member) -> bool:
                prefix = declaration_prefix(masked, member.header_start, member.end)
                if prefix is None or re.search(r"\bstatic\b", prefix.prefix) is None or prefix.break_char == "(":
                    return False
                slice = masked[member.header_start : member.end]
                assignment = re.search(r"[^=!<>]([+|&^]?|\|\||&&)=([^=>]|$)", slice)
                if assignment is None:
                    return False
                eq = assignment.start() + 1
                init = slice[eq:]
                name_match = re.search(r"([A-Za-z_]\w*)\s*(<[^<>]*>)?\s*$", prefix.prefix)
                name = name_match.group(1) if name_match else ""
                declared_type = prefix.prefix[: max(0, prefix.prefix.rfind(name))]
                # The OUTER type identifier decides self-typedness: `Dictionary<string, Noise> M = new()`
                # constructs a Dictionary, not the containing type, so a substring match on a generic
                # argument would barrier members that are safe to move. The type name is the identifier
                # immediately before the declared name.
                declared_type_match = re.search(r"([A-Za-z_]\w*)\s*(<[^<>]*>)?\s*$", declared_type.strip())
                declared_type_name = declared_type_match.group(1) if declared_type_match else ""
                self_typed = (
                    re.search(rf"\bnew\s+{body.name}\b", init) is not None
                    or (declared_type_name == body.name and re.search(r"=\s*new\s*[\s(]", init) is not None)
                )
                reads_sibling = (
                    not self_typed
                    and any(
                        sibling != name and re.search(rf"\b{sibling}\b", init) is not None
                        for sibling in static_member_names
                    )
                )
                return self_typed or reads_sibling

            for member in members:
                directive_end = directive_prefix_end(text, member.start, member.end)
                if directive_end > 0:
                    stripped = text[member.start : directive_end]
                    last: Optional[Union[Member, Piece]] = run[-1] if run else (pieces[-1] if pieces else None)
                    # A `#pragma warning restore` closes a disable that wraps the PREVIOUS member; fold it
                    # back into that member so the pair travels as one unit and the member after it can
                    # still sort. Any other directive is a barrier that stays exactly where it is.
                    closes_wrapped_member = (
                        re.search(r"^\s*#pragma\s+warning\s+restore\b", stripped) is not None
                        and last is not None
                        and not (isinstance(last, Piece) and last.raw is not None)
                        and re.search(r"#pragma\s+warning\s+disable\b", text[last.start : last.end]) is not None
                    )
                    if closes_wrapped_member and last is not None:
                        last.end = directive_end
                    else:
                        flush_run()
                        pieces.append(Piece(raw=stripped))
                start = directive_end if directive_end > 0 else member.start
                slice = text[start : member.end]
                movable = (
                    not member.is_type
                    and not member.trailing
                    and len(slice.strip()) != 0
                    and re.match(r"^[ \t]*#", slice) is None
                    and keys[start] == keys[max(member.end - 1, 0)]
                    and not member_rank(masked, member, body.name).unclassifiable
                    and not initializes_at_type_load(member)
                )
                if movable:
                    moved = Member(
                        start=start,
                        end=member.end,
                        header_start=member.header_start,
                        is_type=member.is_type,
                        trailing=member.trailing,
                        kind=member.kind,
                        name=member.name,
                    )
                    run.append(moved)
                else:
                    flush_run()
                    if start < member.end:
                        pieces.append(
                            Piece(
                                start=start,
                                end=member.end,
                            )
                        )
            flush_run()
            if changed:
                edits.append(
                    Edit(
                        start=body.start,
                        end=body.end,
                        replacement="".join(
                            piece.raw if piece.raw is not None else text[piece.start : piece.end] for piece in pieces
                        ),
                    )
                )
            continue

        # --- #575 nested-type placement (targeted fallback) -----------------------------------
        if not has_type_offenders:
            continue
        offenders = [member for index, member in enumerate(members) if member.is_type and index < last_non_type]
        for offender in offenders:
            violations.append(
                Violation(
                    line=line_of(text, offender.header_start),
                    kind=str(offender.kind),
                    name=offender.name,
                    container=body.name,
                )
            )

        # The move is targeted rather than a whole-body permutation: only the offending types are
        # relocated, and everything else -- including every `#if` member -- keeps its position and so
        # its directive order. A type is movable only when it carries no conditional boundary of its
        # own and the end of the body is in the same region it is, which is what stops a member being
        # compiled into a different build than the one it was written for.
        # The end of the body is the closing brace itself, past any trailing `#endif`, so a type that
        # was unconditional before the move is still unconditional after it.
        destination = keys[body.end]
        movable_types = [
            member
            for member in offenders
            if keys[member.start] == destination and keys[max(member.end - 1, 0)] == destination
        ]
        if len(movable_types) == 0:
            continue

        if any(edit.start <= body.start and body.end <= edit.end for edit in edits):
            # An enclosing body is already being rewritten this pass, and its replacement was built from
            # the text as it is now. Two overlapping edits would clobber each other; the caller
            # re-analyzes until the file settles, which reaches this one on the next pass.
            continue

        moved_ids = {id(member) for member in movable_types}
        # Trailing whitespace before the closing brace reads better after the moved types; a trailing
        # `#endif` must stay where it is, which is what put the destination past it.
        trailing_member = next(
            (
                member
                for member in members
                if member.trailing and re.search(r"^[ \t]*#", text[member.start : member.end], re.MULTILINE) is None
            ),
            None,
        )
        ordered = [
            member
            for member in members
            if id(member) not in moved_ids and member is not trailing_member
        ] + movable_types + ([trailing_member] if trailing_member is not None else [])
        edits.append(
            Edit(
                start=body.start,
                end=body.end,
                replacement="".join(text[member.start : member.end] for member in ordered),
            )
        )

    return Analysis(violations=violations, edits=edits)


def apply_edits(text: str, edits: List[Edit]) -> str:
    """Apply the edits back-to-front so the earlier offsets stay valid."""
    updated = text
    for edit in sorted(edits, key=lambda edit: -edit.start):
        updated = updated[: edit.start] + edit.replacement + updated[edit.end :]
    return updated


def source_files(directory: Path, found: List[Path]) -> List[Path]:
    """Collect every C# file under a directory, skipping build-output trees."""
    if not directory.exists():
        return found
    for entry in sorted(directory.iterdir()):
        full = directory / entry.name
        if entry.is_dir():
            if entry.name in ("bin", "obj", "node_modules"):
                continue
            source_files(full, found)
        elif entry.name.endswith(".cs"):
            found.append(full)
    return found


def is_runtime_source(relative: str) -> bool:
    """Whether a relative path is runtime assembly source."""
    return "Runtime" in relative.split("/")


def is_persisted_state_source(relative: str) -> bool:
    """Whether a relative path is persisted editor-state source."""
    return re.search(r"(^|/)Editor/DataVisualizer/Data/", relative) is not None


def is_search_model_source(relative: str) -> bool:
    """Whether a relative path is global-search model source."""
    return re.search(r"(^|/)Editor/DataVisualizer/Search/", relative) is not None


def is_asset_postprocessing_source(relative: str) -> bool:
    """Whether a relative path is asset postprocessing source."""
    return re.search(r"(^|/)Editor/DataVisualizer/Unity/", relative) is not None


def is_namespace_discovery_source(relative: str) -> bool:
    """Whether a relative path is the namespace/type catalog controller."""
    return re.search(r"(^|/)Editor/DataVisualizer/NamespaceController\.cs$", relative) is not None


def display_for(file: Path) -> str:
    """Show a repo-relative path, or an absolute one when it cannot be relativized.

    A self-test root can live on another Windows drive; relpath then raises
    where the original returned a drive-absolute path, which is kept so the
    caller joins nothing beneath the checkout.
    """
    try:
        return os.path.relpath(file, REPO_ROOT).replace(os.sep, "/")
    except ValueError:
        return str(file).replace(os.sep, "/")


def in_scan_roots(display: str) -> bool:
    """Whether a display path sits under a scan root.

    The whole-tree walk only ever sees files under the roots, so scoped
    mode applies the same membership or its verdict could diverge from the
    walk's on an out-of-root file (a code sample, for example) that CI
    never checks.
    """
    for root in SCAN_ROOTS:
        root_display = display_for(Path(root) if os.path.isabs(root) else REPO_ROOT / root)
        if display == root_display or display.startswith(root_display + "/"):
            return True
    return False


def scoped_scanned(argv: List[str]) -> Optional[List[str]]:
    """Resolve an explicit ``--paths`` scan list of display paths, or None for the walk.

    The fast check passes the changed C# files so a two-file edit scans two
    files instead of the tree, and the pre-commit hook passes the staged C#
    files; whole-tree stays the default for CI. Names are
    repository-relative or absolute. Root
    membership and the exclusion prefixes still apply, so a scoped verdict
    is exactly the whole-tree verdict restricted to the named files, and a
    name that is not a readable C# file fails closed: the scan can never
    quietly verify nothing.
    """
    if "--paths" not in argv:
        return None
    names: List[str] = []
    for item in argv[argv.index("--paths") + 1 :]:
        if item.startswith("--"):
            break
        names.append(item)
    if not names:
        print("[csharp-member-order] --paths needs at least one file.", file=sys.stderr)
        raise SystemExit(2)
    scanned: List[str] = []
    for name in names:
        full_path = Path(name) if os.path.isabs(name) else REPO_ROOT / name
        if full_path.suffix != ".cs" or not full_path.is_file():
            print(f"[csharp-member-order] --paths entry is not a readable C# file: {name}", file=sys.stderr)
            raise SystemExit(2)
        relative = display_for(full_path)
        if not in_scan_roots(relative):
            continue
        if any(relative.startswith(prefix) for prefix in EXCLUDED_PREFIXES):
            continue
        scanned.append(relative)
    return sorted(scanned)


def main(argv: List[str]) -> int:
    """Scan the roots and report every C# source policy violation."""
    fix = "--fix" in argv
    verbose = "--verbose" in argv

    explicit = scoped_scanned(argv)
    if explicit is None:
        files: List[Path] = []
        for root in SCAN_ROOTS:
            source_files(Path(root) if os.path.isabs(root) else REPO_ROOT / root, files)
        scanned = sorted(
            relative
            for relative in (display_for(file) for file in files)
            if not any(relative.startswith(prefix) for prefix in EXCLUDED_PREFIXES)
        )
        if len(scanned) == 0:
            print("[csharp-member-order] no C# files found; the scan roots are wrong.", file=sys.stderr)
            return 1
    else:
        scanned = explicit

    remaining: List[str] = []
    fixed_files = 0
    fixed_sites = 0

    for relative in scanned:
        # A self-test root can live on another Windows drive. relpath then intentionally returns
        # an absolute drive path, which a join would incorrectly append beneath the checkout.
        file = Path(relative) if os.path.isabs(relative) else REPO_ROOT / relative
        text = file.read_text(encoding="utf-8")
        prohibit_linq = (
            is_runtime_source(relative)
            or is_persisted_state_source(relative)
            or is_search_model_source(relative)
            or is_asset_postprocessing_source(relative)
            or is_namespace_discovery_source(relative)
        )
        result = analyze_file(text, prohibit_linq)

        if fix and len(result.edits) > 0:
            # Nesting means an outer move can expose an inner one; re-analyze until the file settles.
            # A file keeps whatever this CAN fix even when something in it cannot be moved, or one
            # conditional-bound type would hold a whole file's backlog hostage.
            original = text
            before = len(result.violations)
            guard = 0
            while len(result.edits) > 0 and guard < 12:
                updated = apply_edits(text, result.edits)
                if len(updated) != len(text):
                    print(
                        f"[csharp-member-order] refusing to rewrite {relative}: the reordering changed "
                        f"{len(text)} bytes into {len(updated)}. Fix by hand.",
                        file=sys.stderr,
                    )
                    break
                if updated == text:
                    break
                text = updated
                result = analyze_file(text, prohibit_linq)
                guard += 1
            if text != original:
                file.write_bytes(text.encode("utf-8"))
                fixed_files += 1
                fixed_sites += before - len(result.violations)

        for violation in result.violations:
            if violation.kind == "prohibited LINQ dependency":
                remaining.append(
                    f"{relative}:{violation.line}: Runtime, persisted editor-state, search-model, "
                    f"asset postprocessing, and namespace-discovery code must not depend on "
                    f"System.Linq (#61)"
                )
                continue
            if violation.kind == "multiline line comment":
                remaining.append(
                    f"{relative}:{violation.line}: consecutive ordinary line comments must use one "
                    f"indented block comment; keep // for single-line comments and tool directives (#57)"
                )
                continue
            if violation.kind == "counted loop over value enumerator":
                remaining.append(
                    f"{relative}:{violation.line}: use foreach for this loop; arrays and strings have "
                    f"value-based enumerators and the index is used only to read elements (rule 21)"
                )
                continue
            if violation.kind == "underscored method":
                remaining.append(
                    f"{relative}:{violation.line}: method '{violation.name}' in "
                    f"'{violation.container}' must use PascalCase without underscores (#47)"
                )
                continue
            if violation.kind == "unclassified member":
                remaining.append(
                    f"{relative}:{violation.line}: a member of '{violation.container}' defeats the "
                    f"declaration parser and cannot be classified for the member ordering; classify it "
                    f"by hand or extend the parser, and never let a file pass on a silent skip"
                )
                continue
            if violation.kind in ORDER_TIERS:
                def described(tier: Optional[str], access: Optional[str]) -> str:
                    return tier or "" if access is None or access == "none" else f"{access} {tier}"

                remaining.append(
                    f"{relative}:{violation.line}: {described(violation.kind, violation.access)} "
                    f"'{violation.name}' is declared after a {described(violation.after, violation.after_access)} "
                    f"in '{violation.container}' (#672)"
                )
                continue
            remaining.append(
                f"{relative}:{violation.line}: nested {violation.kind} '{violation.name}' is declared "
                f"between members of '{violation.container}'"
            )

    if fix:
        print(f"[csharp-member-order] fixed {fixed_sites} member-order violation(s) across {fixed_files} file(s).")
    if len(remaining) > 0:
        print(
            f"[csharp-member-order] {len(remaining)} C# source policy violation(s). "
            "Use block comments for multi-line prose; use underscore-free PascalCase method names; "
            "iterate arrays, strings, and other value-enumerator collections with foreach unless the "
            "loop needs the index; "
            "keep Runtime, persisted editor-state, search-model, asset postprocessing, and "
            "namespace-discovery code free of System.Linq; "
            "reorder members into the #672 ordering "
            "(const, events, delegates, static properties, "
            "static fields, properties, fields, constructors, static methods, methods; each tier public → "
            "protected → internal → private) and move nested types to the end of their containing "
            "type (issue #575).",
            file=sys.stderr,
        )
        for entry in remaining:
            print(f"  {entry}", file=sys.stderr)
        return 1
    if verbose:
        print(f"[csharp-member-order] {len(scanned)} file(s) clean.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
