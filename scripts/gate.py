#!/usr/bin/env python3
"""Runs the DxVisualizer local review gate as parallel lanes.

The gate reproduces the repository's full non-Unity review ladder: it
formats C# and Markdown, runs the source, harness, and release lints, the
harness self-test suite, and the packaging dry run. Independent quick
lanes run concurrently, every step reports its wall time, and ``--report``
writes a machine-readable JSON record so gate measurements can be compared
across runs and hosts.

The scoped steps consult the same fail-safe diff verdicts CI's llm-lint
scope step uses: the three C# source lints are per-file policies and scan
the branch's changed C# files when a comparison base resolves and skip
when no C# changed; the Markdown formatter, the packaging dry run, and the
self-test suite skip when the diff cannot affect them, and the suite runs
the same surface selection CI runs when it can be narrowed. The
merge-base tree is the tree CI already validated whole-tree, so the
scoped steps check exactly what the diff can affect, and CI keeps the
whole-tree runs as the safety net. The ``--plan`` output stays the static
whole-tree ladder contract.

The harness self-test suite spawns one interpreter per test file and
saturates the machine, so it runs as its own phase after the quick lanes
rather than competing with them for CPU and filesystem bandwidth.

The driver only orchestrates the repository's existing checks; it owns no
check logic. The Unity EditMode and PlayMode suites stay a separate,
host-dependent gate (see ``.llm/context.md``).

Launch with ``uv run scripts/gate.py``; any Python 3.9+ interpreter works.
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import shutil
import subprocess
import sys
import threading
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Set, Tuple, cast

sys.dont_write_bytecode = True

from harness_scope import (
    available_test_files,
    committed_changed_files,
    is_surface_path,
    markdown_paths_changed,
    self_test_selection,
    working_tree_changed_files,
)

REPO_ROOT = Path(__file__).resolve().parent.parent

REPORT_SCHEMA_VERSION = 1
PLAN_SCHEMA_VERSION = 1
FAILURE_OUTPUT_LINES = 60


def _python_launcher() -> Tuple[str, ...]:
    """Prefix that runs the harness's own Python scripts on this machine.

    ``uv run`` is the documented path because it provisions the project
    environment on machines that have uv; anywhere uv is absent (CI runners
    without it, minimal containers) the gate degrades to the interpreter
    running this script, so the plan always describes commands that can
    actually execute on the host that built it.
    """
    if shutil.which("uv") is not None:
        return ("uv", "run")
    return (sys.executable,)


_PYTHON_LAUNCHER: Tuple[str, ...] = _python_launcher()


@dataclass(frozen=True)
class Step:
    """One gate command: a program, its arguments, and its dependencies."""

    name: str
    title: str
    args: Tuple[str, ...]
    after: Tuple[str, ...] = ()
    check: Optional[str] = None  # "empty-output": the step must print nothing


# The lane table is the single source of truth for the gate. Dependencies may
# cross lanes (Markdown formatting rewrites .llm Markdown, so the harness
# lints wait for it) and are validated as an acyclic graph at import time.
LANES: Tuple[Tuple[str, Tuple[Step, ...]], ...] = (
    (
        "csharp",
        (
            Step("csharp.tool-restore", "dotnet tool restore", ("dotnet", "tool", "restore")),
            Step(
                "csharp.format",
                "CSharpier format",
                ("dotnet", "tool", "run", "csharpier", "--", "format", "Editor", "Runtime", "Tests"),
                ("csharp.tool-restore",),
            ),
            Step(
                "csharp.member-order",
                "C# member-order lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_csharp_member_order.py"),
                ("csharp.format",),
            ),
            Step(
                "csharp.null-assertions",
                "C# null-assertions lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_csharp_null_assertions.py"),
                ("csharp.format",),
            ),
            Step(
                "csharp.usings",
                "C# usings lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_csharp_usings.py"),
                ("csharp.format",),
            ),
        ),
    ),
    (
        "markdown",
        (
            Step("markdown.format", "Prettier Markdown format", ("npm", "run", "format:md")),
            Step(
                "markdown.changelog",
                "Changelog length lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_changelog_length.py"),
                ("markdown.format",),
            ),
        ),
    ),
    (
        "assembly",
        (
            Step(
                "assembly.warnings",
                "Assembly warning-policy lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_assembly_warnings.py"),
            ),
        ),
    ),
    (
        "harness",
        (
            Step(
                "harness.skills-index",
                "Skills index regeneration",
                (*_PYTHON_LAUNCHER, "scripts/generate_skills_index.py"),
                ("markdown.format",),
            ),
            Step(
                "harness.skills-index-drift",
                "Skills index drift check",
                ("git", "status", "--porcelain", "--", ".llm/skills/index.md"),
                ("harness.skills-index",),
                check="empty-output",
            ),
            Step(
                "harness.llm-instructions",
                ".llm instructions lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_llm_instructions.py", "--verbose-output"),
                ("harness.skills-index",),
            ),
            Step(
                "harness.file-lengths",
                ".llm file-length lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_file_lengths.py", "--verbose-output"),
                ("harness.skills-index",),
            ),
            Step(
                "harness.release-secrets",
                "Release credential lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_release_secrets.py"),
                ("harness.skills-index",),
            ),
            Step(
                "harness.line-endings",
                "Line-ending lint",
                (*_PYTHON_LAUNCHER, "scripts/lint_line_endings.py"),
                ("harness.skills-index",),
            ),
        ),
    ),
    (
        "tests",
        (
            Step(
                "tests.self-tests",
                "Harness self-test suite",
                (*_PYTHON_LAUNCHER, "scripts/tests/run_all.py"),
            ),
        ),
    ),
    (
        "pack",
        (
            Step("pack.npm-pack", "npm pack dry run", ("npm", "pack", "--dry-run", "--no-audit", "--no-fund")),
        ),
    ),
)

ALL_STEPS: Tuple[Step, ...] = tuple(step for _, lane_steps in LANES for step in lane_steps)
STEP_NAMES: frozenset[str] = frozenset(step.name for step in ALL_STEPS)

# The three C# source lints are per-file policies over the same walk roots:
# a file the branch did not touch cannot newly violate one, so the gate scans
# them with the branch's changed paths when a comparison base resolves. The
# merge-base tree is the tree CI already validated whole-tree, so a scoped
# scan is equivalent to the whole-tree walk for everything outside the diff;
# CI keeps the whole-tree scans as the safety net (the same split as the fast
# check and the pre-commit hooks, issues #196/#199). Whole-tree stays the
# behavior whenever no base resolves.
CSHARP_LINT_STEPS: Dict[str, str] = {
    "csharp.member-order": "scripts/lint_csharp_member_order.py",
    "csharp.null-assertions": "scripts/lint_csharp_null_assertions.py",
    "csharp.usings": "scripts/lint_csharp_usings.py",
}


@dataclass(frozen=True)
class DiffScope:
    """What the branch's diff can affect, as fail-safe verdicts.

    The verdicts mirror CI's llm-lint scope step (issue #195/#198 lineage):
    every field the diff cannot resolve keeps the guarded step whole-tree.
    ``changed_csharp`` carries the existing changed-file tuple — ``None``
    keeps the whole-tree C# scan, an empty tuple drops the C# steps, and a
    non-empty tuple scopes the per-file lint args. The boolean fields are
    ``None`` (no resolvable base: run the guarded step) or ``False`` (the
    diff cannot affect the step: skip it).
    """

    changed_csharp: Optional[Tuple[str, ...]] = None
    markdown_changed: Optional[bool] = None
    package_changed: Optional[bool] = None
    self_tests_run: bool = True
    self_test_selection: Optional[Tuple[str, ...]] = None  # None = the full suite


# npm packages the manifest's ``files`` entries plus the root manifests npm
# always includes, so a diff touching none of them cannot change the
# packaging dry run's outcome. The globs are read from the manifest instead
# of hardcoded, so a ``files`` edit cannot stale the predicate; an
# unreadable manifest yields no globs, which only ever runs the step more
# often, never skips it falsely.
_NPM_ALWAYS_INCLUDED_FILE_PREFIXES: Tuple[str, ...] = ("changelog", "licence", "license", "readme")


def _packaged_globs() -> Tuple[str, ...]:
    """Return the manifest's ``files`` entries; empty when unreadable."""
    try:
        manifest: Dict[str, Any] = json.loads((REPO_ROOT / "package.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return ()
    entries: Any = manifest.get("files")
    if not isinstance(entries, list):
        return ()
    # The isinstance guard above proves the shape; the element type stays
    # unknown to the checker, so the filter is declared explicitly.
    known_entries = cast(List[Any], entries)
    return tuple(entry for entry in known_entries if isinstance(entry, str))


def package_paths_changed(paths: Sequence[str], packaged_globs: Sequence[str]) -> bool:
    """Return whether any changed path can affect the packaged payload."""
    for path in paths:
        normalized = path.replace("\\", "/")
        if normalized in ("package.json", "package-lock.json"):
            return True
        if "/" not in normalized and normalized.lower().startswith(_NPM_ALWAYS_INCLUDED_FILE_PREFIXES):
            return True
        for entry in packaged_globs:
            glob_root = entry.replace("\\", "/").rstrip("/")
            if normalized == glob_root or normalized.startswith(glob_root + "/"):
                return True
    return False


def scan_diff_scope(base_sha: str) -> DiffScope:
    """Classify the branch's diff for every scoped step in one scan.

    The committed and working-tree scans run concurrently like the fast
    check's (each costs about a second on bind-mounted checkouts). The
    verdicts mirror CI's scope step on the same scans, so the gate and CI
    cannot disagree about what a diff can affect. Paths are filtered to
    files that exist only where a ``--paths`` scan would fail on them:
    deletions and rename old-sides appear in a diff but not on disk.
    """
    committed: List[str] = []
    working: List[str] = []
    threads = [
        threading.Thread(target=lambda: committed.extend(committed_changed_files(REPO_ROOT, base_sha))),
        threading.Thread(target=lambda: working.extend(working_tree_changed_files(REPO_ROOT))),
    ]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()
    paths = committed + working
    existing_csharp = [path for path in paths if path.endswith(".cs") and (REPO_ROOT / path).is_file()]
    selection = self_test_selection(paths, available_test_files())
    return DiffScope(
        changed_csharp=tuple(sorted(existing_csharp)),
        markdown_changed=markdown_paths_changed(paths),
        package_changed=package_paths_changed(paths, _packaged_globs()),
        self_tests_run=any(is_surface_path(path) for path in paths),
        self_test_selection=None if selection.all else selection.names,
    )


# Every step the diff scope can drop or rewrite. An ``--only`` run that
# selects none of them skips the scan's git cost entirely.
SCOPED_STEP_NAMES: frozenset[str] = frozenset(
    {"csharp.tool-restore", "csharp.format", *CSHARP_LINT_STEPS, "markdown.format", "tests.self-tests", "pack.npm-pack"}
)


def scoped_lanes(scope: DiffScope) -> Tuple[Tuple[str, Tuple[Step, ...]], ...]:
    """Return the lane table with the diff-scoped steps dropped or rewritten.

    Each verdict acts independently and fails safe: an unresolved verdict
    keeps the whole-tree step, a negative verdict drops the step, and the
    C# and self-test selections rewrite the step args to exactly the
    changed files or selected tests. Dropped steps are removed from their
    dependents' ``after`` lists, so the scoped table stays a valid
    dependency graph: a skipped formatter rewrites nothing, and the lints
    that waited for it read the same tree immediately.
    """
    rewrites: Dict[str, Tuple[str, ...]] = {}
    drops: Set[str] = set()
    if scope.changed_csharp is not None:
        if scope.changed_csharp:
            rewrites.update(
                {
                    name: (*_PYTHON_LAUNCHER, script, "--paths", *scope.changed_csharp)
                    for name, script in CSHARP_LINT_STEPS.items()
                }
            )
        else:
            drops.update({"csharp.tool-restore", "csharp.format", *CSHARP_LINT_STEPS})
    if scope.markdown_changed is False:
        # Both Markdown steps guard tracked Markdown: the formatter rewrites
        # it and the changelog lint reads it. No changed Markdown file means
        # neither can observe anything new.
        drops.update({"markdown.format", "markdown.changelog"})
    if not scope.self_tests_run:
        drops.add("tests.self-tests")
    elif scope.self_test_selection is not None:
        rewrites["tests.self-tests"] = (
            *_PYTHON_LAUNCHER,
            "scripts/tests/run_all.py",
            "--names",
            ",".join(scope.self_test_selection),
        )
    if scope.package_changed is False:
        drops.add("pack.npm-pack")

    if not drops and not rewrites:
        return LANES

    scoped: List[Tuple[str, Tuple[Step, ...]]] = []
    for lane_name, lane_steps in LANES:
        kept: List[Step] = []
        for step in lane_steps:
            if step.name in drops:
                continue
            after = tuple(dependency for dependency in step.after if dependency not in drops)
            args = rewrites.get(step.name, step.args)
            if after == step.after and args == step.args:
                kept.append(step)
            else:
                kept.append(Step(step.name, step.title, args, after, step.check))
        if kept:
            scoped.append((lane_name, tuple(kept)))
    return tuple(scoped)


def resolve_base_sha(base: str) -> Optional[str]:
    """Resolve the comparison base: the argument, else the merge base with origin/main, else HEAD.

    Mirrors the fast check's resolution; ``None`` means no base resolved (git
    is unusable here), which keeps every check whole-tree.
    """
    if base.strip():
        resolved = _git_text_quiet(["rev-parse", "--verify", "--quiet", f"{base}^{{commit}}"])
        if resolved:
            return resolved
    if _git_text_quiet(["rev-parse", "--verify", "--quiet", "origin/main"]):
        merge_base = _git_text_quiet(["merge-base", "HEAD", "origin/main"])
        if merge_base:
            return merge_base
    return _git_text_quiet(["rev-parse", "--verify", "--quiet", "HEAD"]) or None


def _git_text_quiet(args: Sequence[str]) -> str:
    """Run a git query and return its trimmed stdout, or an empty string on failure."""
    try:
        completed = subprocess.run(
            ["git", "-C", str(REPO_ROOT), *args],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except OSError:
        return ""
    return (completed.stdout or "").strip() if completed.returncode == 0 else ""


# The self-test suite spawns one interpreter per test file and saturates the
# machine, so it runs as its own phase after the quick lanes and keeps its
# full throughput instead of sharing it with the formatting and lint steps.
TEST_LANE_NAMES: frozenset[str] = frozenset({"tests"})


def validate_step_graph() -> None:
    """Fail fast on duplicate names, unknown dependencies, or cycles."""
    seen: Set[str] = set()
    for step in ALL_STEPS:
        if step.name in seen:
            raise SystemExit(f"gate: duplicate step name {step.name!r}")
        seen.add(step.name)

    for step in ALL_STEPS:
        for dependency in step.after:
            if dependency not in STEP_NAMES:
                raise SystemExit(f"gate: step {step.name!r} depends on unknown step {dependency!r}")

    resolved: Set[str] = set()
    while len(resolved) < len(ALL_STEPS):
        progressed = False
        for step in ALL_STEPS:
            if step.name in resolved:
                continue
            if all(dependency in resolved for dependency in step.after):
                resolved.add(step.name)
                progressed = True
        if not progressed:
            raise SystemExit("gate: step dependencies form a cycle")


validate_step_graph()


def expand_selection(requested: Sequence[str]) -> Set[str]:
    """Return the requested steps plus every transitive dependency."""
    by_name = {step.name: step for step in ALL_STEPS}
    selected: Set[str] = set()
    pending = list(requested)
    while pending:
        name = pending.pop()
        if name in selected:
            continue
        selected.add(name)
        pending.extend(by_name[name].after)
    return selected


def select_steps_from(lanes: Tuple[Tuple[str, Tuple[Step, ...]], ...], requested: Sequence[str]) -> Tuple[Tuple[str, Tuple[Step, ...]], ...]:
    """Filter the given lane table down to the selected steps, preserving order.

    Names already scoped out of the table are ignored: a ``--only`` re-run of
    a lint the scope skipped has nothing to re-check, and the scope note on
    the console says so.
    """
    by_name = {step.name: step for _, lane_steps in lanes for step in lane_steps}
    selected = expand_selection([name for name in requested if name in by_name])
    scoped: List[Tuple[str, Tuple[Step, ...]]] = []
    for lane_name, lane_steps in lanes:
        kept = tuple(step for step in lane_steps if step.name in selected)
        if kept:
            scoped.append((lane_name, kept))
    return tuple(scoped)


def select_steps(requested: Sequence[str]) -> Tuple[Tuple[str, Tuple[Step, ...]], ...]:
    """Filter the static lane table down to the selected steps, preserving order."""
    return select_steps_from(LANES, requested)


def resolve_program(program: str) -> Optional[str]:
    """Locate a step program on PATH."""
    return shutil.which(program)


def build_command(step: Step) -> Tuple[List[str], bool]:
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
    """Outcome of one executed step.

    ``failure`` is the single owner of failure detail: every failed launch,
    nonzero exit, and policy check writes it, and every consumer (console
    line, report JSON) surfaces it. A failed step must never report without
    a reason.
    """

    name: str
    title: str
    lane: str
    args: Tuple[str, ...]
    seconds: float
    exit_code: Optional[int]
    ok: bool
    shell: bool
    failure: Optional[str] = None


class Gate:
    """Executes lanes concurrently and reports every step's wall time."""

    def __init__(self, lanes: Tuple[Tuple[str, Tuple[Step, ...]], ...], report_path: Optional[Path]) -> None:
        self._lanes = lanes
        self._report_path = report_path
        self._events: Dict[str, threading.Event] = {
            step.name: threading.Event() for _, lane_steps in lanes for step in lane_steps
        }
        self._results: List[StepResult] = []
        self._print_lock = threading.Lock()
        self._results_lock = threading.Lock()
        self._started = time.monotonic()

    def run(self) -> int:
        environment: Dict[str, object] = {
            "python": platform.python_version(),
            "platform": platform.platform(),
            "cpu_count": os.cpu_count() or 0,
            "git_head": _git_head(),
        }
        started_utc = _utc_now()
        quick = [lane for lane in self._lanes if lane[0] not in TEST_LANE_NAMES]
        tests = [lane for lane in self._lanes if lane[0] in TEST_LANE_NAMES]
        self._run_phase(quick)
        if tests:
            print("[gate] phase: harness self-tests")
            self._run_phase(tests)

        total_seconds = time.monotonic() - self._started
        failed = [result for result in self._results if not result.ok]
        self._results.sort(key=lambda result: result.name)
        if not failed:
            print(f"[gate] passed: {len(self._results)} step(s) in {total_seconds:.1f}s")
        else:
            failed_names = ", ".join(result.name for result in failed)
            print(
                f"[gate] failed: {len(failed)} of {len(self._results)} step(s) in {total_seconds:.1f}s ({failed_names})"
            )

        if self._report_path is not None:
            try:
                self._write_report(self._report_path, started_utc, total_seconds, not failed, environment)
                print(f"[gate] report: {self._report_path}")
            except OSError as error:
                print(f"[gate] report FAILED: could not write {self._report_path}: {error}")
        return 0 if not failed else 1

    def _run_phase(self, lanes: Sequence[Tuple[str, Tuple[Step, ...]]]) -> None:
        threads = [
            threading.Thread(target=self._run_lane, args=(lane_name, lane_steps), name=f"gate-{lane_name}")
            for lane_name, lane_steps in lanes
        ]
        for thread in threads:
            thread.start()
        for thread in threads:
            thread.join()
        # Completeness belt: every expected step must have a recorded result
        # before the verdict is computed. A lane thread that died without
        # recording must count as failed, never as an absent success. The
        # belt is itself a failure path, so it surfaces its reason on the
        # console like every other failure (one owner field surfaced by
        # every consumer), matching the fast check's belt output shape.
        recorded = {result.name for result in self._results}
        for lane_name, lane_steps in lanes:
            for step in lane_steps:
                if step.name not in recorded:
                    result = StepResult(
                        step.name,
                        step.title,
                        lane_name,
                        step.args,
                        0.0,
                        None,
                        False,
                        False,
                        failure="no result recorded; the lane crashed before this step ran",
                    )
                    with self._results_lock:
                        self._results.append(result)
                    with self._print_lock:
                        print(f"[gate] FAIL {step.name} ({result.seconds:.1f}s)")
                        print(f"[gate] ---- {step.name} ----")
                        print(result.failure)

    def _run_lane(self, lane_name: str, lane_steps: Tuple[Step, ...]) -> None:
        try:
            for step in lane_steps:
                self._wait_for_dependencies(step)
                result: StepResult
                try:
                    result = self._run_step(lane_name, step)
                except Exception as error:
                    # A crashed step must record a failure: a dead lane that
                    # records nothing would let the gate report success.
                    result = StepResult(
                        step.name,
                        step.title,
                        lane_name,
                        step.args,
                        0.0,
                        None,
                        False,
                        False,
                        failure=f"the lane crashed running this step: {error!r}",
                    )
                with self._results_lock:
                    self._results.append(result)
                # Released per step, before the next dependency wait can be
                # reached by another lane: in-lane dependent steps wait on
                # this event too, so a lane-level release would deadlock the
                # loop on its own lane's event.
                self._events[step.name].set()
                with self._print_lock:
                    if result.ok:
                        print(f"[gate] ok   {step.name} ({result.seconds:.1f}s)")
                    else:
                        print(f"[gate] FAIL {step.name} ({result.seconds:.1f}s)")
                        if result.failure:
                            print(f"[gate] ---- {step.name} ----")
                            print(result.failure)
        finally:
            # Catastrophic-death insurance only (a raise outside the per-step
            # guard, e.g. from a broken stdout mid-print): release every step
            # event the lane owns so waiting lanes unblock and the phase's
            # completeness belt records the unexecuted steps as failed
            # instead of the gate hanging on their wait() calls. Setting an
            # already-released event is a no-op.
            for step in lane_steps:
                self._events[step.name].set()

    def _wait_for_dependencies(self, step: Step) -> None:
        for dependency in step.after:
            if dependency in self._events:
                self._events[dependency].wait()

    def _run_step(self, lane_name: str, step: Step) -> StepResult:
        try:
            command, shell = build_command(step)
        except FileNotFoundError as error:
            return StepResult(step.name, step.title, lane_name, step.args, 0.0, None, False, False, failure=str(error))

        started = time.monotonic()
        try:
            # shell=True is the Windows .cmd shim path, and cmd.exe must receive
            # the command line as a raw string: list-escaping would apply CRT
            # quote rules that cmd.exe parses differently.
            completed = subprocess.run(
                command[0] if shell else command,
                cwd=str(REPO_ROOT),
                shell=shell,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
            )
        except OSError as error:
            seconds = time.monotonic() - started
            return StepResult(step.name, step.title, lane_name, step.args, seconds, None, False, shell, failure=str(error))

        seconds = time.monotonic() - started
        stdout = completed.stdout or ""
        stderr = completed.stderr or ""
        failure: Optional[str] = None
        if completed.returncode != 0:
            failure = _tail(f"exited {completed.returncode}", stdout, stderr, FAILURE_OUTPUT_LINES)
        elif step.check == "empty-output":
            failure = check_empty_output(stdout, stderr)
        if failure is not None:
            return StepResult(
                step.name,
                step.title,
                lane_name,
                step.args,
                seconds,
                completed.returncode,
                False,
                shell,
                failure=failure,
            )
        return StepResult(step.name, step.title, lane_name, step.args, seconds, completed.returncode, True, shell)

    def _write_report(
        self,
        report_path: Path,
        started_utc: str,
        total_seconds: float,
        passed: bool,
        environment: Dict[str, object],
    ) -> None:
        report = {
            "schema": REPORT_SCHEMA_VERSION,
            "repo_root": str(REPO_ROOT),
            "started_utc": started_utc,
            "finished_utc": _utc_now(),
            "total_seconds": round(total_seconds, 3),
            "passed": passed,
            "environment": environment,
            "invocation": [sys.executable, *sys.argv[1:]],
            "steps": [
                {
                    "name": result.name,
                    "title": result.title,
                    "lane": result.lane,
                    "args": list(result.args),
                    "seconds": round(result.seconds, 3),
                    "exit_code": result.exit_code,
                    "ok": result.ok,
                    "shell": result.shell,
                    **({"failure": result.failure} if result.failure is not None else {}),
                }
                for result in self._results
            ],
        }
        report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


def _tail(header: str, stdout: str, stderr: str, limit: int) -> str:
    combined = (stdout.rstrip() + "\n" + stderr.rstrip()).strip()
    lines = combined.splitlines()
    body = lines[-limit:] if len(lines) > limit else lines
    prefix = f"{header}; showing last {len(body)} line(s)" if len(lines) > limit else header
    return "\n".join([prefix, *body])


def _git_head() -> Optional[str]:
    """Best-effort environment enrichment: an unusable git degrades to None."""
    try:
        completed = subprocess.run(
            ["git", "rev-parse", "--verify", "HEAD"],
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError:
        return None
    head = (completed.stdout or "").strip()
    return head or None


def _utc_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def plan_document(lanes: Tuple[Tuple[str, Tuple[Step, ...]], ...]) -> Dict[str, object]:
    """Build the plan JSON used by --plan and the harness self-test."""
    unresolved: List[str] = []
    lane_documents: List[Dict[str, object]] = []
    for lane_name, lane_steps in lanes:
        step_documents: List[Dict[str, object]] = []
        for step in lane_steps:
            resolved = resolve_program(step.args[0]) is not None
            if not resolved:
                unresolved.append(step.args[0])
            step_documents.append(
                {
                    "name": step.name,
                    "title": step.title,
                    "args": list(step.args),
                    "after": list(step.after),
                    "check": step.check,
                    "resolved": resolved,
                }
            )
        lane_documents.append({"name": lane_name, "steps": step_documents})
    return {
        "schema": PLAN_SCHEMA_VERSION,
        "repo_root": str(REPO_ROOT),
        "lanes": lane_documents,
        "unresolved_programs": sorted(set(unresolved)),
    }


def parse_arguments(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="gate",
        description="Run the full local review gate as parallel lanes.",
    )
    parser.add_argument("--plan", action="store_true", help="print the step plan as JSON without executing anything")
    parser.add_argument("--report", type=Path, default=None, help="write a machine-readable JSON report to this path")
    parser.add_argument(
        "--base",
        default="",
        help="comparison base for the diff-scoped steps; defaults to the merge base with origin/main, else HEAD",
    )
    parser.add_argument(
        "--only",
        default="",
        help="comma-separated step names to run (dependencies are included automatically)",
    )
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = parse_arguments(argv)
    requested = [name.strip() for name in options.only.split(",") if name.strip()]
    unknown = [name for name in requested if name not in STEP_NAMES]
    if unknown:
        known = ", ".join(sorted(STEP_NAMES))
        print(f"gate: unknown step name(s): {', '.join(unknown)}; known steps: {known}", file=sys.stderr)
        return 2

    if options.plan:
        # The plan is the ladder's contract: the static whole-tree table, so
        # the step set stays reviewable and pinned regardless of the diff.
        # Execution below applies the scope; the report carries the actual
        # per-step args.
        lanes = select_steps(requested) if requested else LANES
        print(json.dumps(plan_document(lanes), indent=2))
        return 0

    # The scan only pays for itself when a selected step would consume it:
    # a full run selects every step, and an --only run of a scoped step
    # wants its verdict; anything else keeps whole-tree steps and skips the
    # scan's git cost entirely.
    scan_needed = not requested or any(name in SCOPED_STEP_NAMES for name in requested)
    scope = DiffScope()
    base_sha = resolve_base_sha(options.base) if scan_needed else None
    if base_sha:
        short_base = base_sha[:12]
        scope = scan_diff_scope(base_sha)
        if scope.changed_csharp:
            print(f"[gate] csharp lints: scoped to {len(scope.changed_csharp)} changed file(s) (base {short_base})")
        else:
            print(f"[gate] csharp lints: skipped, no C# changed since {short_base}")
        if scope.markdown_changed is False:
            print(f"[gate] markdown format: skipped, no Markdown changed since {short_base}")
        if scope.package_changed is False:
            print(f"[gate] npm pack: skipped, packaged payload unchanged since {short_base}")
        if not scope.self_tests_run:
            print(f"[gate] self-tests: skipped, no harness surface changed since {short_base}")
        elif scope.self_test_selection is not None:
            print(f"[gate] self-tests: scoped to {len(scope.self_test_selection)} file(s) (base {short_base})")
    elif scan_needed:
        print("[gate] scope: whole-tree (no resolvable base)")

    base_table = scoped_lanes(scope)
    lanes = select_steps_from(base_table, requested) if requested else base_table
    print(f"[gate] repo {REPO_ROOT}")
    step_count = sum(len(lane_steps) for _, lane_steps in lanes)
    print(f"[gate] plan: {step_count} step(s) across {len(lanes)} lane(s)")
    return Gate(lanes, options.report).run()


if __name__ == "__main__":
    sys.exit(main())
