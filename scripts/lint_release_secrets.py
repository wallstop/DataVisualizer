#!/usr/bin/env python3
"""Release credential contract: every secret the release workflows read must
be documented, and every credential the release reference documents must be
read by a release workflow.

Typed Python port of ``lint-release-secrets.js`` (issue #171): the same
documented-block parsing, YAML comment masking, and fail-closed drift
report, driven by the standard library.

The release chain uses GitHub's built-in token and npm OIDC. No custom
credential is needed. This guard detects undocumented credentials if a
future workflow introduces them, and stale credential instructions in the
runbook.

The documented set is read from the ``release-secrets`` block in
``.llm/references/RELEASING.md``, so prose elsewhere in the reference is
free to mention credentials that are not required (for example "no
NPM_TOKEN is involved") without failing the scan.

The release workflow set is the single workflow the reference documents. A
new release workflow must be added to RELEASE_WORKFLOWS and to the
reference.

'--verbose' prints the compared sets when clean. Exit codes: 0 = in sync,
1 = drift, a missing or malformed documented block, or an unreadable input.

'RELEASE_SECRETS_WORKFLOW_DIR' and 'RELEASE_SECRETS_DOC' override the inputs
so the self-test can point at a fixture tree. Nothing in CI sets them.
"""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path
from typing import Set

REPO_ROOT = Path(__file__).resolve().parent.parent

# The workflows `.llm/references/RELEASING.md` documents. Kept explicit so the
# scan does not demand release documentation for unrelated workflows.
RELEASE_WORKFLOWS = ("release.yml",)

# Provided by GitHub for every run; configuring it is not an owner task, so it
# is never part of the documented contract.
BUILT_IN_SECRETS = {"GITHUB_TOKEN"}

BLOCK_START = "<!-- release-secrets:begin -->"
BLOCK_END = "<!-- release-secrets:end -->"

# Both documented GitHub Actions secret forms: `secrets.NAME` and
# `secrets['NAME']`. A computed index such as `secrets[format(...)]` cannot be
# resolved statically and is outside this check's coverage.
SECRET_REFERENCE = re.compile(r"\bsecrets(?:\.([A-Za-z_][A-Za-z0-9_]*)|\[\s*['\"]([A-Za-z_][A-Za-z0-9_]*)['\"]\s*\])")
DOCUMENTED_SECRET = re.compile(r"`([A-Z][A-Z0-9_]*)`")


def read_file_or_throw(file_path: Path, purpose: str) -> str:
    """Read a file, failing closed with the shared message shape."""
    try:
        return file_path.read_text(encoding="utf-8")
    except OSError:
        raise ValueError(f"Cannot {purpose}: '{file_path}' is missing or unreadable.") from None


def mask_yaml_comments(text: str) -> str:
    """Blank YAML comments so a commented-out secret reference cannot require documentation.

    Quoted regions are preserved, because a '#' inside a string is data
    rather than a comment.
    """
    characters = list(text)
    index = 0
    while index < len(text):
        character = text[index]
        if character in ("'", '"'):
            quote = character
            index += 1
            while index < len(text):
                if text[index] == "\\" and quote == '"':
                    index += 2
                    continue
                if text[index] == quote:
                    if index + 1 < len(text) and text[index + 1] == quote:
                        index += 2
                        continue
                    index += 1
                    break
                index += 1
            continue
        if character == "#":
            end = text.find("\n", index)
            stop = len(text) if end < 0 else end
            for position in range(index, stop):
                if characters[position] != "\n":
                    characters[position] = " "
            index = stop
            continue
        index += 1
    return "".join(characters)


def read_workflow_secrets(file_path: Path) -> Set[str]:
    """Collect the secret names one workflow reads, minus the built-ins."""
    masked = mask_yaml_comments(read_file_or_throw(file_path, "read the release workflow"))
    secrets: Set[str] = set()
    for match in SECRET_REFERENCE.finditer(masked):
        # Alternation group 1 is the dot form, group 2 the bracket form.
        name = match.group(1) or match.group(2)
        if name not in BUILT_IN_SECRETS:
            secrets.add(name)
    return secrets


def read_documented_secrets(file_path: Path) -> Set[str]:
    """Parse the documented secret set from the release-secrets block."""
    text = read_file_or_throw(file_path, "read the release reference")
    start = text.find(BLOCK_START)
    end = text.find(BLOCK_END)
    if start < 0 or end < 0 or end < start:
        raise ValueError(
            f"'{file_path}' must carry a '{BLOCK_START}' / '{BLOCK_END}' block listing the "
            "secrets the release workflows read; the documented set is parsed from it."
        )
    block = text[start + len(BLOCK_START) : end]
    secrets = {match.group(1) for match in DOCUMENTED_SECRET.finditer(block)}
    if len(secrets) == 0 and block.strip() != "No custom secrets are required.":
        raise ValueError(
            f"'{file_path}' has an empty release-secrets block; name each secret in "
            "backticks, or state exactly: No custom secrets are required."
        )
    return secrets


def display_path(file_path: Path) -> str:
    """Show a repo-relative path when inside the repository, else the absolute path."""
    try:
        relative = os.path.relpath(file_path, REPO_ROOT).replace(os.sep, "/")
    except ValueError:
        # A different drive on Windows cannot be relativized; show the
        # absolute path the way the original did.
        return str(file_path)
    return str(file_path) if relative.startswith("../") else relative


def main() -> int:
    """Compare the read and documented secret sets, failing closed on drift."""
    verbose = "--verbose" in sys.argv
    read: Set[str] = set()
    for workflow in RELEASE_WORKFLOWS:
        read.update(read_workflow_secrets(workflow_dir() / workflow))
    documented = read_documented_secrets(reference_doc())

    undocumented = sorted(secret for secret in read if secret not in documented)
    unused = sorted(secret for secret in documented if secret not in read)

    if undocumented or unused:
        print(
            "The release credential contract is out of sync. The release workflows read "
            f"{', '.join(sorted(read)) or 'no secret'}; "
            f"'{display_path(reference_doc())}' documents {', '.join(sorted(documented))}.",
            file=sys.stderr,
        )
        for secret in undocumented:
            print(
                f"  {secret}: read by a release workflow but not documented. Add it to the "
                "release-secrets block with the scope an owner must grant.",
                file=sys.stderr,
            )
        for secret in unused:
            print(
                f"  {secret}: documented but read by no release workflow. Remove it or point "
                "the workflow at the credential the owner actually configures.",
                file=sys.stderr,
            )
        return 1

    if verbose:
        print(
            f"Release credential contract in sync: {len(read)} secret(s) documented "
            f"across {len(RELEASE_WORKFLOWS)} release workflow(s)."
        )
    return 0


def workflow_dir() -> Path:
    """Resolve the workflow directory, honoring the self-test override."""
    override = os.environ.get("RELEASE_SECRETS_WORKFLOW_DIR")
    return Path(os.path.abspath(override)) if override else REPO_ROOT / ".github" / "workflows"


def reference_doc() -> Path:
    """Resolve the reference document, honoring the self-test override."""
    override = os.environ.get("RELEASE_SECRETS_DOC")
    return Path(os.path.abspath(override)) if override else REPO_ROOT / ".llm" / "references" / "RELEASING.md"


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
