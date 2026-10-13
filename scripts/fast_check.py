#!/usr/bin/env python3
"""Runs only the validation relevant to the files changed since the base.

An agent iterating on a two-file change checks in seconds instead of running
the whole ladder; the full ladder (``npm run check:gate``) still gates review.
The plan classifies the changed paths into check groups and prints as JSON
with ``--plan`` without executing anything, which is what the harness
self-tests exercise on machines without the .NET toolchain.

The port from ``scripts/fast-check.ps1`` (issue #171) keeps the verdict and
fail-safe semantics and changes only how the work is scheduled: the two
per-call git scans run concurrently (on bind-mounted checkouts each scan
costs about a second and they used to run sequentially), and independent
check groups run as a dependency DAG instead of sequential blocks. Like the
gate, quick steps run concurrently and the harness self-tests run alone as
a second phase, because the suite spawns one interpreter per test file and
saturates the machine.

Launch with ``uv run scripts/fast_check.py``; any Python 3.9+ interpreter works.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import threading
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Dict, List, Optional, Sequence, Tuple

# Importing the local scope module must not leave scripts/__pycache__ behind:
# the bytecode cache is an untracked surface path the next scan would see,
# fail safe on, and turn every subsequent run into the full suite.
sys.dont_write_bytecode = True

from harness_scope import (
    REPO_ROOT,
    available_test_files,
    committed_changed_files,
    is_surface_path,
    self_test_selection,
    working_tree_changed_files,
)

PLAN_SCHEMA_VERSION = 1


# Child steps run the interpreter that is already executing this driver: the
# harness scripts are standard library only, so no environment provisioning is
# needed, the step commands cannot drift from the driver's own runtime, and
# the per-step ``uv run`` resolution cost (measured at about 0.15s per spawn
# on a bind-mounted checkout) plus its lockfile-drift failure mode leave the
# per-edit loop. The documented outer entries (``npm run check:fast``,
# ``uv run scripts/fast_check.py``) are unchanged.
_PYTHON_LAUNCHER: Tuple[str, ...] = (sys.executable,)


def _git_text(repo_root: Path, args: Sequence[str]) -> str:
    """Run a git query and return its trimmed stdout, or an empty string on failure."""
    try:
        completed = subprocess.run(
            ["git", "-C", str(repo_root), *args],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except OSError:
        return ""
    return (completed.stdout or "").strip() if completed.returncode == 0 else ""


def resolve_base_sha(repo_root: Path, base: str) -> str:
    """Resolve the comparison base: the argument, else the merge base with origin/main, else HEAD."""
    if base.strip():
        resolved = _git_text(repo_root, ["rev-parse", "--verify", "--quiet", f"{base}^{{commit}}"])
        if resolved:
            return resolved

    if _git_text(repo_root, ["rev-parse", "--verify", "--quiet", "origin/main"]):
        merge_base = _git_text(repo_root, ["merge-base", "HEAD", "origin/main"])
        if merge_base:
            return merge_base
    return _git_text(repo_root, ["rev-parse", "HEAD"])


@dataclass(frozen=True)
class FastCheckPlan:
    """The classification of one change: which groups run, and which self-tests."""

    repo_root: Path
    base_sha: str
    csharp: Tuple[str, ...]
    markdown: Tuple[str, ...]
    harness: bool
    packaging: bool
    assembly_config: bool
    release_surface: bool
    line_endings: bool
    self_tests_all: bool
    self_test_names: Tuple[str, ...]


def build_plan(repo_root: Path, base_sha: str) -> FastCheckPlan:
    """Classify the changed paths into check groups.

    Deletions and rename old-paths appear in a git diff but not on disk.
    Group triggers below still see every changed path - deleting a harness
    file must keep running the self-tests - but the formatter lists feed
    file-argument tools (csharpier, prettier), which fail on missing paths.
    """
    committed, working, scan_errors = _scan_paths_concurrently(repo_root, base_sha)
    if scan_errors:
        # A crashed scan cannot produce an honest plan: fail closed instead
        # of classifying nothing and reporting success on zero checks.
        raise SystemExit("fast-check: the change scan crashed: " + "; ".join(scan_errors))
    changed = sorted(set(committed + working))
    existing = [path for path in changed if (repo_root / path).exists()]
    selection = self_test_selection(changed, available_test_files())
    return FastCheckPlan(
        repo_root=repo_root,
        base_sha=base_sha,
        csharp=tuple(path for path in existing if path.endswith(".cs")),
        markdown=tuple(path for path in existing if path.endswith(".md")),
        harness=any(is_surface_path(path) for path in changed),
        packaging=any(path.startswith(("Editor/", "Runtime/")) or path == "package.json" for path in changed),
        assembly_config=any(
            path.endswith((".asmdef", ".ruleset", ".rsp")) or path.endswith(".dll.meta") for path in changed
        ),
        release_surface=any(
            path == ".github/workflows/release.yml" or path == ".llm/references/RELEASING.md" for path in changed
        ),
        line_endings=any(path in (".editorconfig", ".gitattributes") for path in changed),
        self_tests_all=selection.all,
        self_test_names=selection.names,
    )


def _scan_paths_concurrently(repo_root: Path, base_sha: str) -> Tuple[List[str], List[str], List[str]]:
    """Run the committed and working-tree scans side by side.

    Both scans are independent read-only git queries, and on bind-mounted
    checkouts each costs about a second of filesystem time; running them
    sequentially doubled the plan floor's wall time. A crashed scan is
    recorded as an error instead of degrading to an empty list: an empty
    plan built on a crashed scan would report success while checking
    nothing.
    """
    committed: List[str] = []
    working: List[str] = []
    errors: List[str] = []
    lock = threading.Lock()

    def collect(target: List[str], producer: Callable[[], List[str]], label: str) -> None:
        try:
            result = producer()
        except Exception as error:
            with lock:
                errors.append(f"{label}: {error!r}")
            return
        with lock:
            target.extend(result)

    threads = [
        threading.Thread(target=collect, args=(committed, lambda: committed_changed_files(repo_root, base_sha), "committed scan")),
        threading.Thread(target=collect, args=(working, lambda: working_tree_changed_files(repo_root), "working-tree scan")),
    ]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()
    return committed, working, errors


def plan_document(plan: FastCheckPlan) -> Dict[str, object]:
    """Build the plan JSON used by --plan and the harness self-tests."""
    return {
        "schema": PLAN_SCHEMA_VERSION,
        "repo_root": str(plan.repo_root),
        "base_sha": plan.base_sha,
        "csharp": list(plan.csharp),
        "markdown": list(plan.markdown),
        "harness": plan.harness,
        "packaging": plan.packaging,
        "assembly_config": plan.assembly_config,
        "release_surface": plan.release_surface,
        "line_endings": plan.line_endings,
        "self_tests": {"all": plan.self_tests_all, "names": list(plan.self_test_names)},
    }


@dataclass(frozen=True)
class CheckStep:
    """One fast-check command: a program, its arguments, and its dependencies."""

    name: str
    title: str
    args: Tuple[str, ...]
    after: Tuple[str, ...] = ()
    check: Optional[str] = None  # "empty-output": the step must print nothing


def build_steps(plan: FastCheckPlan) -> Tuple[Tuple[CheckStep, ...], Tuple[CheckStep, ...]]:
    """Build the quick-phase and test-phase steps the plan selects.

    The harness self-tests spawn one interpreter per test file and saturate
    the machine, so they run as their own phase after the quick steps rather
    than competing with them for CPU and filesystem bandwidth.
    """
    quick: List[CheckStep] = []

    if plan.csharp:
        quick.append(
            CheckStep(
                "csharp.format",
                "CSharpier format",
                ("dotnet", "tool", "run", "csharpier", "--", "format", *plan.csharp),
            )
        )
        # The three source lints are independent read-only scans of the same
        # C# tree, so they run side by side after the format. They are
        # per-file policies, so they scan only the changed files: an
        # unchanged file cannot newly violate one, and the per-edit loop
        # stays at scan-the-diff cost instead of whole-tree cost.
        for name, script in (
            ("csharp.member-order", "scripts/lint_csharp_member_order.py"),
            ("csharp.null-assertions", "scripts/lint_csharp_null_assertions.py"),
            ("csharp.usings", "scripts/lint_csharp_usings.py"),
        ):
            quick.append(
                CheckStep(name, name, (*_PYTHON_LAUNCHER, script, "--paths", *plan.csharp), ("csharp.format",))
            )


    if plan.markdown:
        quick.append(
            CheckStep("markdown.format", "Prettier format", ("npx", "--no-install", "prettier", "--write", *plan.markdown))
        )
        if "CHANGELOG.md" in plan.markdown:
            quick.append(
                CheckStep(
                    "markdown.changelog",
                    "changelog length",
                    (*_PYTHON_LAUNCHER, "scripts/lint_changelog_length.py"),
                    ("markdown.format",),
                )
            )

    if plan.harness:
        # Prettier rewrites .llm Markdown, so the harness lints wait for the
        # Markdown format when this run formats any.
        skills_dependencies: Tuple[str, ...] = ("markdown.format",) if plan.markdown else ()
        quick.append(
            CheckStep(
                "harness.skills-index",
                "skills index",
                (*_PYTHON_LAUNCHER, "scripts/generate_skills_index.py"),
                skills_dependencies,
            )
        )
        quick.append(
            CheckStep(
                "harness.skills-index-drift",
                "skills index drift",
                ("git", "status", "--porcelain", "--", ".llm/skills/index.md"),
                ("harness.skills-index",),
                check="empty-output",
            )
        )
        quick.append(
            CheckStep(
                "harness.llm-instructions",
                ".llm instructions lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_llm_instructions.py"),
                ("harness.skills-index",),
            )
        )
        quick.append(
            CheckStep(
                "harness.file-lengths",
                ".llm file-length lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_file_lengths.py"),
                ("harness.skills-index",),
            )
        )

    if plan.packaging:
        quick.append(CheckStep("pack.npm-pack", "npm pack dry run", ("npm", "pack", "--dry-run", "--no-audit", "--no-fund")))

    if plan.assembly_config:
        quick.append(
            CheckStep(
                "assembly.warnings",
                "assembly warning policy",
                (*_PYTHON_LAUNCHER, "scripts/lint_assembly_warnings.py"),
            )
        )

    if plan.release_surface:
        quick.append(
            CheckStep("release.secrets", "release credential lint", (*_PYTHON_LAUNCHER, "scripts/lint_release_secrets.py"))
        )

    if plan.line_endings:
        quick.append(
            CheckStep("line-endings.check", "line-ending lint", (*_PYTHON_LAUNCHER, "scripts/lint_line_endings.py"))
        )

    tests: List[CheckStep] = []
    if plan.harness:
        # The self-tests wait for the harness lints they verify; a drift
        # failure must not suppress them, so the drift check is not a
        # dependency and every failure still reaches the verdict.
        test_args: List[str] = [*_PYTHON_LAUNCHER, "scripts/tests/run_all.py"]
        if not plan.self_tests_all:
            test_args.extend(("--names", ",".join(plan.self_test_names)))
        tests.append(
            CheckStep(
                "harness.self-tests",
                "harness self-tests",
                tuple(test_args),
                ("harness.llm-instructions", "harness.file-lengths"),
            )
        )
    return tuple(quick), tuple(tests)


def resolve_program(program: str) -> Optional[str]:
    """Locate a step program on PATH."""
    return shutil.which(program)


def build_command(step: CheckStep) -> Tuple[List[str], bool]:
    """Build the launch command for one step.

    Windows resolves npm/npx through ``.cmd`` shims that only ``cmd.exe``
    can launch, so those steps run through a shell command line built with
    cmd-style quoting. Every other program launches directly.
    """
    resolved = resolve_program(step.args[0])
    if resolved is None:
        raise FileNotFoundError(f"program not found on PATH: {step.args[0]!r}")
    if os.name == "nt" and resolved.lower().endswith((".cmd", ".bat")):
        return ([_windows_shell_line([resolved, *step.args[1:]])], True)
    return ([resolved, *step.args[1:]], False)


def _windows_shell_line(argv: List[str]) -> str:
    """Quote arguments the way cmd.exe parses them (no CRT escaping)."""

    def quote(value: str) -> str:
        if not value or any(character in value for character in ' \t"&|<>^'):
            return '"' + value.replace('"', '""') + '"'
        return value

    return " ".join(quote(argument) for argument in argv)


def check_empty_output(stdout: str, stderr: str) -> Optional[str]:
    """Return a failure message when a command printed unexpected output."""
    combined = (stdout + stderr).strip()
    if combined:
        return "expected no output, got:\n" + combined
    return None


@dataclass
class StepResult:
    """Outcome of one executed step; ``failure`` is the single owner of failure detail."""

    name: str
    seconds: float
    exit_code: Optional[int]
    ok: bool
    failure: Optional[str] = None


class Runner:
    """Executes each phase's steps concurrently and reports every wall time."""

    def __init__(self, repo_root: Path, phases: Sequence[Tuple[str, Tuple[CheckStep, ...]]]) -> None:
        self._repo_root = repo_root
        self._phases = phases
        self._events: Dict[str, threading.Event] = {
            step.name: threading.Event() for _, steps in phases for step in steps
        }
        self._results: List[StepResult] = []
        self._print_lock = threading.Lock()
        self._results_lock = threading.Lock()

    def run(self) -> int:
        for phase_name, steps in self._phases:
            if not steps:
                continue
            if phase_name != "quick":
                print(f"[fast-check] phase: {phase_name}")
            threads = [threading.Thread(target=self._run_step, args=(step,), name=f"fast-check-{step.name}") for step in steps]
            for thread in threads:
                thread.start()
            for thread in threads:
                thread.join()
            # Completeness belt: every expected step must have a recorded
            # result before the verdict is computed; a thread that died
            # without recording counts as failed, never as absent success.
            recorded = {result.name for result in self._results}
            for step in steps:
                if step.name not in recorded:
                    result = StepResult(
                        step.name,
                        0.0,
                        None,
                        False,
                        failure="no result recorded; the step crashed before reporting",
                    )
                    with self._results_lock:
                        self._results.append(result)
                    with self._print_lock:
                        print(f"[fast-check] FAIL {step.name} ({result.seconds:.1f}s)")
                        print(f"[fast-check] ---- {step.name} ----")
                        print(result.failure)

        failed = [result for result in self._results if not result.ok]
        if not failed:
            print("[fast-check] passed")
        else:
            print(f"[fast-check] failed: {', '.join(result.name for result in failed)}")
        return 0 if not failed else 1

    def _run_step(self, step: CheckStep) -> None:
        try:
            for dependency in step.after:
                if dependency in self._events:
                    self._events[dependency].wait()
            result: StepResult
            try:
                result = self._execute(step)
            except Exception as error:
                # A crashed step must record a failure: a dead thread that
                # records nothing would let the fast check report success.
                result = StepResult(
                    step.name,
                    0.0,
                    None,
                    False,
                    failure=f"the step crashed before reporting: {error!r}",
                )
            with self._results_lock:
                self._results.append(result)
            with self._print_lock:
                if result.ok:
                    print(f"[fast-check] ok   {step.name} ({result.seconds:.1f}s)")
                else:
                    print(f"[fast-check] FAIL {step.name} ({result.seconds:.1f}s)")
                    if result.failure:
                        print(f"[fast-check] ---- {step.name} ----")
                        print(result.failure)
        finally:
            # The event releases on every path so waiting steps unblock and
            # the phase's completeness belt records any unexecuted step.
            self._events[step.name].set()

    def _execute(self, step: CheckStep) -> StepResult:
        try:
            command, shell = build_command(step)
        except FileNotFoundError as error:
            return StepResult(step.name, 0.0, None, False, failure=str(error))

        started = time.monotonic()
        try:
            # shell=True is the Windows .cmd shim path, and cmd.exe must receive
            # the command line as a raw string: list-escaping would apply CRT
            # quote rules that cmd.exe parses differently.
            completed = subprocess.run(
                command[0] if shell else command,
                cwd=str(self._repo_root),
                shell=shell,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
            )
        except OSError as error:
            return StepResult(step.name, time.monotonic() - started, None, False, failure=str(error))

        seconds = time.monotonic() - started
        stdout = completed.stdout or ""
        stderr = completed.stderr or ""
        failure: Optional[str] = None
        if completed.returncode != 0:
            failure = _tail(f"exited {completed.returncode}", stdout, stderr)
        elif step.check == "empty-output":
            failure = check_empty_output(stdout, stderr)
        if failure is not None:
            return StepResult(step.name, seconds, completed.returncode, False, failure=failure)
        return StepResult(step.name, seconds, completed.returncode, True)


def _tail(header: str, stdout: str, stderr: str, limit: int = 60) -> str:
    combined = (stdout.rstrip() + "\n" + stderr.rstrip()).strip()
    lines = combined.splitlines()
    body = lines[-limit:] if len(lines) > limit else lines
    prefix = f"{header}; showing last {len(body)} line(s)" if len(lines) > limit else header
    return "\n".join([prefix, *body])


def parse_arguments(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="fast-check",
        description="Run only the validation relevant to the files changed since the base.",
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=REPO_ROOT,
        help="repository root (default: the checkout holding this script)",
    )
    parser.add_argument("--base", default="", help="comparison base; defaults to the merge base with origin/main, else HEAD")
    parser.add_argument("--plan", action="store_true", help="print the check plan as JSON without executing anything")
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = parse_arguments(argv)
    repo_root = options.repo_root.resolve()
    plan = build_plan(repo_root, resolve_base_sha(repo_root, options.base))

    if options.plan:
        print(json.dumps(plan_document(plan), indent=2))
        return 0

    quick, tests = build_steps(plan)
    print(f"[fast-check] validating changes since {plan.base_sha}")
    if not plan.self_tests_all:
        print(f"[fast-check] self-test selection: {', '.join(plan.self_test_names)}")
    return Runner(repo_root, (("quick", quick), ("self-tests", tests))).run()


if __name__ == "__main__":
    sys.exit(main())
