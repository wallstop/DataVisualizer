#!/usr/bin/env python3
"""opencode-service-recycle contract tests (typed Python port of the bash contract suite, issue #171).

Every case from ``.devcontainer/tests/test-opencode-service-recycle.sh`` is
kept with the same assertions; the suite now drives the ported
``recycle_stale_opencode_service.py`` natively, and the bash original retired
with the script it tested.

The cases keep the bash suite's fixture discipline: synthetic placeholder
values, throwaway state dirs, no real ``.env.local`` or ``service.json``, and
the issue #175 readiness-marker guard so a fixture that never exec'd fails its
own case with a named diagnostic instead of degrading into a wrong-verdict
failure. One adaptation is deliberate: bash read the marker's ``$$`` and
compared it against its own ``$!``, which Python cannot reproduce across the
MSYS/Windows pid boundary, so readiness is proven by the marker naming a pid
that is alive in the bash namespace the tool itself uses.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import time
from pathlib import Path
from typing import Dict, Optional, Tuple

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    failure_count,
    find_python,
    new_temp_root,
    remove_temp_root,
    run_case,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
RECYCLE_SCRIPT = REPO_ROOT / ".devcontainer" / "recycle_stale_opencode_service.py"

# Synthetic, obviously-fake placeholders. NOT credentials.
STALE = "ghp_0000000000000000000000000000000000"
LIVE = "ghp_1111111111111111111111111111111111"

PID_ALIVE_CHILD = 'kill -0 "$1" 2>/dev/null'
SIGNAL_CHILD_TEMPLATE = 'kill {signal} "$1" 2>/dev/null'


def _bash_exit(script: str, argument: str) -> int:
    """Run one bash child and return its exit code; 125 when bash cannot run.

    The probes and signals go through bash so the test and the tool address
    the service in the same pid namespace on every host.
    """
    try:
        completed = subprocess.run(
            ["bash", "-c", script, "recycle-test", argument],
            capture_output=True,
            check=False,
        )
    except OSError:
        return 125
    return completed.returncode


def _bash_signal(pid: int, signal: str) -> None:
    """Send a signal through bash's kill builtin, ignoring a failed delivery."""
    _bash_exit(SIGNAL_CHILD_TEMPLATE.format(signal=signal), str(pid))


class _ServiceFixture:
    """A real background process standing in for the long-lived service.

    The fixture's own command chain writes a readiness marker naming its pid
    before exec'ing the sleeper (issue #175): under suite concurrency a spawn
    can fail to exec or die immediately, which would silently degrade the case
    into the script's dead-pid path. Readiness is verified after each spawn,
    with one retry, and a fixture that still never becomes ready raises before
    the case acts, mirroring the stub-readiness pattern of
    test-mcp-credentials.sh.
    """

    def __init__(self) -> None:
        self.root = new_temp_root(prefix="oc-recycle-")
        (self.root / "state" / "opencode").mkdir(parents=True)
        (self.root / "home").mkdir()
        self.pid: Optional[int] = None
        self._process: Optional[subprocess.Popen[bytes]] = None

    def env_file(self) -> Path:
        return self.root / ".env.local"

    def state_dir(self) -> Path:
        return self.root / "state" / "opencode"

    def record(self) -> Path:
        return self.state_dir() / "service.json"

    def write_env_file(self, content: str) -> None:
        write_fixture_file(self.root, ".env.local", content)

    def write_record(self) -> None:
        assert_true(self.pid is not None, "the fixture must start before a record can name its pid")
        content = "\n".join(
            [
                "{",
                '  "id": "00000000-0000-0000-0000-000000000000",',
                '  "version": "2.0.26",',
                '  "url": "http://127.0.0.1:49374",',
                f'  "pid": {self.pid}',
                "}",
                "",
            ]
        )
        write_fixture_file(self.state_dir(), "service.json", content)

    def start(self, service_env_value: str) -> None:
        """Spawn the sleeper with the chosen credential value in its environment."""
        for attempt in (1, 2):
            marker = self.root / f"fixture-ready.{attempt}"
            marker.unlink(missing_ok=True)
            env: Dict[str, str] = dict(os.environ)
            # Every other credential name is explicitly cleared: the test must
            # measure the fixture, not whatever the developer happens to have
            # exported.
            for name in ("ZAI_API_KEY", "Z_AI_API_KEY", "GITHUB_TOKEN"):
                env.pop(name, None)
            env["GITHUB_PERSONAL_ACCESS_TOKEN"] = service_env_value
            env["GITHUB_MCP_PAT"] = service_env_value
            self._process = subprocess.Popen(
                ["bash", "-c", 'printf "%s\\n" "$$" >"$1" && exec sleep 300', "fixture", str(marker)],
                env=env,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            for _ in range(20):
                text = ""
                if marker.is_file():
                    text = marker.read_text(encoding="utf-8", errors="replace").strip()
                if text.isdigit() and _bash_exit(PID_ALIVE_CHILD, text) == 0:
                    self.pid = int(text)
                    return
                if self._process.poll() is not None:
                    # The spawn died before it could exec the sleeper: retry.
                    break
                time.sleep(0.05)
            # This attempt never became ready: kill first so a live stray cannot
            # block the reaping wait, then retry.
            self._abandon_attempt()
        raise AssertionError("fake service fixture never became ready after two attempts")

    def _abandon_attempt(self) -> None:
        if self._process is not None:
            self._process.terminate()
            try:
                self._process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self._process.kill()
                self._process.wait(timeout=5)
            self._process = None
        if self.pid is not None:
            _bash_signal(self.pid, "-9")
            self.pid = None

    def alive(self) -> bool:
        # Reap our own spawn first: on Linux the sleeper IS this spawn, so a
        # SIGTERMed sleeper stays a zombie - and answers ``kill -0`` - until
        # the parent reaps it. On hosts where exec replaced the spawn's image,
        # the reaped exit says nothing about the sleeper, and the bash probe
        # below stays the verdict in the namespace the tool addresses.
        if self._process is not None:
            self._process.poll()
        return self.pid is not None and _bash_exit(PID_ALIVE_CHILD, str(self.pid)) == 0

    def wait_until_gone(self) -> bool:
        """Whether the pid stops answering within a bounded settle window."""
        for _ in range(20):
            if not self.alive():
                return True
            time.sleep(0.1)
        return False

    def require_live(self) -> None:
        """Fail the calling case exactly once, with a named diagnostic, when the
        fixture is not alive right before the case acts on it (issue #175).

        A dead fixture must not degrade into wrong-verdict failures from the
        recycle script's dead-pid path.
        """
        if self.alive():
            return
        raise AssertionError(f"fake service (pid {self.pid}) is not running; the case needs a live fixture")

    def stop(self) -> None:
        """Terminate the service and reap the spawn, in both pid namespaces."""
        if self.pid is not None:
            _bash_signal(self.pid, "")
            if not self.wait_until_gone():
                _bash_signal(self.pid, "-9")
                time.sleep(0.2)
            self.pid = None
        if self._process is not None:
            try:
                self._process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self._process.kill()
                self._process.wait(timeout=5)
            self._process = None

    def cleanup(self) -> None:
        self.stop()
        remove_temp_root(self.root)


def run_recycle(python: str, fixture: _ServiceFixture, *arguments: str) -> Tuple[str, int]:
    """Run the recycle script against the fixture's state dir and env file."""
    return run_tool_output(
        [
            python,
            str(RECYCLE_SCRIPT),
            "--state-dir",
            str(fixture.state_dir()),
            "--env-file",
            str(fixture.env_file()),
            *arguments,
        ]
    )


def run_recycle_with_env(
    python: str,
    fixture: _ServiceFixture,
    extra_env: Dict[str, str],
    *arguments: str,
) -> Tuple[str, int]:
    """Run the recycle script with extra environment variables exported."""
    completed = subprocess.run(
        [
            python,
            str(RECYCLE_SCRIPT),
            "--state-dir",
            str(fixture.state_dir()),
            "--env-file",
            str(fixture.env_file()),
            *arguments,
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
        env={**os.environ, **extra_env},
    )
    return (completed.stdout or "") + (completed.stderr or ""), completed.returncode


# ---------------------------------------------------------------------------
# Detection: a service holding a credential that disagrees with .env.local
# ---------------------------------------------------------------------------


def case_recycles_service_with_stale_credential(python: str) -> None:
    # The exact incident: .env.local holds the repaired value, the running
    # service still holds the old one. New sessions keep attaching to the stale
    # singleton, so the service must be replaced.
    fixture = _ServiceFixture()
    try:
        fixture.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\n")
        fixture.start(STALE)
        fixture.write_record()
        fixture.require_live()
        output, exit_code = run_recycle(python, fixture)
        assert_exit_code(0, "a stale service must be recycled successfully", exit_code)
        assert_true("stale credential" in output, f"the output must report the stale credential: {output}")
        assert_true(str(fixture.pid) in output, f"the output must name the recycled pid: {output}")
        assert_true(fixture.wait_until_gone(), "the stale service process must be terminated")
    finally:
        fixture.cleanup()


def case_removes_stale_discovery_record(python: str) -> None:
    # The record is how clients find the singleton. Leaving it behind would let
    # a client attach to a pid that is gone.
    fixture = _ServiceFixture()
    try:
        fixture.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\n")
        fixture.start(STALE)
        fixture.write_record()
        fixture.require_live()
        run_recycle(python, fixture)
        assert_true(not fixture.record().exists(), "the stale discovery record must be removed")
    finally:
        fixture.cleanup()


# ---------------------------------------------------------------------------
# Safety: never recycle a healthy service
# ---------------------------------------------------------------------------


def case_leaves_matching_service_alone(python: str) -> None:
    # The common case must cost nothing - no kill, no record churn.
    fixture = _ServiceFixture()
    try:
        fixture.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\n")
        fixture.start(LIVE)
        fixture.write_record()
        fixture.require_live()
        output, _ = run_recycle(python, fixture)
        assert_true(fixture.alive(), "the matching service must be left running")
        assert_true(fixture.record().exists(), "the matching discovery record must be preserved")
        assert_true(
            "stale credential" not in output,
            f"no recycle may be reported when the service is healthy: {output}",
        )
    finally:
        fixture.cleanup()


def case_dry_run_reports_without_killing(python: str) -> None:
    # Reports must be verifiable before acting on them.
    fixture = _ServiceFixture()
    try:
        fixture.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\n")
        fixture.start(STALE)
        fixture.write_record()
        fixture.require_live()
        output, exit_code = run_recycle(python, fixture, "--dry-run")
        assert_exit_code(0, "a dry run must exit 0", exit_code)
        assert_true(fixture.alive(), "a dry run must leave the service running")
        assert_true(fixture.record().exists(), "a dry run must preserve the discovery record")
        assert_true("stale credential" in output, f"a dry run must still report the finding: {output}")
    finally:
        fixture.cleanup()


def case_missing_service_record_is_a_no_op(python: str) -> None:
    fixture = _ServiceFixture()
    try:
        fixture.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\n")
        _, exit_code = run_recycle(python, fixture)
        assert_exit_code(0, "a missing discovery record must exit zero", exit_code)
    finally:
        fixture.cleanup()


def case_dead_pid_is_cleaned_up(python: str) -> None:
    # A record whose pid is gone cannot be recycled, but it must not block
    # startup or be treated as a healthy service.
    fixture = _ServiceFixture()
    try:
        fixture.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\n")
        record_content = '{ "id": "dead", "version": "2.0.26", "url": "http://127.0.0.1:1", "pid": 999999 }\n'
        write_fixture_file(fixture.state_dir(), "service.json", record_content)
        output, exit_code = run_recycle(python, fixture)
        assert_exit_code(0, "a record for a dead pid must exit zero", exit_code)
        assert_true(
            "not running" in output,
            f"a record for a dead pid must be reported as stale: {output}",
        )
        assert_true(not fixture.record().exists(), "the record for a dead pid must be removed")
    finally:
        fixture.cleanup()


def case_no_env_file_is_a_no_op(python: str) -> None:
    # With no .env.local there is nothing to compare against, so nothing to do.
    fixture = _ServiceFixture()
    try:
        fixture.start(LIVE)
        fixture.write_record()
        fixture.require_live()
        run_recycle(python, fixture)
        assert_true(fixture.alive(), "a missing .env.local must leave the service alone")
    finally:
        fixture.cleanup()


def case_quoted_and_crlf_env_file_is_parsed_like_the_loader(python: str) -> None:
    # Regression guard. The recycle script originally hand-rolled its own
    # .env.local parser and disagreed with env-local.sh, so it read a different
    # string than the loader produced and reported a HEALTHY service as stale -
    # recycling it on every container start. The script delegates parsing to
    # the loader; this test pins that equivalence for the formats real files
    # use.
    #
    # Symptom of the old bug: reading the value returned a 49-char string whose
    # tail was the SHA of the expected token, proving the key match leaked.
    fixture = _ServiceFixture()
    try:
        fixture.write_env_file("\ufeff" + f'GITHUB_PERSONAL_ACCESS_TOKEN="{LIVE}"\r\n')
        fixture.start(LIVE)
        fixture.write_record()
        fixture.require_live()
        output, _ = run_recycle(python, fixture)
        assert_true(
            "stale credential" not in output,
            f"a quoted+CRLF+BOM env file must not be misread as a mismatch: {output}",
        )
        assert_true(fixture.alive(), "a healthy service must survive a quoted/CRLF/BOM env file")
    finally:
        fixture.cleanup()


def case_inherited_env_is_not_mistaken_for_a_file_value(python: str) -> None:
    # Regression guard for the unset-before-load fix. When the caller runs the
    # script with a credential exported in its own environment, a key the
    # .env.local does NOT define must not read as "present in the file" -
    # otherwise a mismatch is invented against the caller's own value and a
    # healthy service is recycled on every start.
    fixture = _ServiceFixture()
    try:
        # File defines GITHUB_PERSONAL_ACCESS_TOKEN only; GITHUB_MCP_PAT is absent.
        fixture.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\n")
        fixture.start(LIVE)
        fixture.write_record()
        fixture.require_live()
        output, _ = run_recycle_with_env(python, fixture, {"GITHUB_MCP_PAT": STALE})
        assert_true(
            "stale credential" not in output,
            f"an inherited value for a key absent from .env.local must be ignored: {output}",
        )
        assert_true(
            fixture.alive(),
            "a healthy service must survive an inherited out-of-file credential",
        )
    finally:
        fixture.cleanup()


def case_never_prints_credential_values(python: str) -> None:
    fixture = _ServiceFixture()
    try:
        fixture.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\nGITHUB_MCP_PAT={LIVE}\n")
        fixture.start(STALE)
        fixture.write_record()
        fixture.require_live()
        output, _ = run_recycle(python, fixture)
        assert_true(LIVE not in output, f"the output must omit the file credential: {output}")
        assert_true(STALE not in output, f"the output must omit the service credential: {output}")
    finally:
        fixture.cleanup()


def main() -> int:
    print("== opencode-service-recycle contract tests ==")

    if shutil.which("bash") is None:
        print("    SKIP: bash not found on PATH; opencode-service-recycle tests require a POSIX shell.")
        print("== opencode-service-recycle: 0 failure(s) ==")
        return 0

    # The recycle script is typed Python; any working interpreter drives it.
    python = find_python()

    run_case("Recycles_ServiceWithStaleCredential", lambda: case_recycles_service_with_stale_credential(python))
    run_case("Removes_StaleDiscoveryRecord", lambda: case_removes_stale_discovery_record(python))
    run_case("Leaves_MatchingServiceAlone", lambda: case_leaves_matching_service_alone(python))
    run_case("DryRun_ReportsWithoutKilling", lambda: case_dry_run_reports_without_killing(python))
    run_case("MissingServiceRecord_IsANoOp", lambda: case_missing_service_record_is_a_no_op(python))
    run_case("DeadPid_IsCleanedUp", lambda: case_dead_pid_is_cleaned_up(python))
    run_case("NoEnvFile_IsANoOp", lambda: case_no_env_file_is_a_no_op(python))
    run_case(
        "QuotedAndCrlfEnvFile_IsParsedLikeTheLoader",
        lambda: case_quoted_and_crlf_env_file_is_parsed_like_the_loader(python),
    )
    run_case(
        "InheritedEnv_IsNotMistakenForAFileValue",
        lambda: case_inherited_env_is_not_mistaken_for_a_file_value(python),
    )
    run_case("Never_PrintsCredentialValues", lambda: case_never_prints_credential_values(python))

    failures = failure_count()
    print(f"== opencode-service-recycle: {failures} failure(s) ==")
    if failures > 0:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
