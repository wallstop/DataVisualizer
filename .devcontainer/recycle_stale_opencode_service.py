#!/usr/bin/env python3
"""Recycle the OpenCode background service when its MCP credentials disagree with .env.local.

Typed Python port of ``recycle-stale-opencode-service.sh`` (issue #171): the
same behavior and exit codes, with the orchestration in typed Python.

The problem this solves: OpenCode runs a shared background server
(``opencode serve --service``) that EVERY CLI session attaches to, discovered
through ~/.local/state/opencode/service.json. That service inherits the
environment of whichever client happened to start it and keeps it for its
entire life. So when a credential is repaired in .env.local, new shells and new
sessions still fail: they are fine, the singleton behind them is not. Killing
it lets the next client respawn it from the corrected environment, which is
the only fix that does not require rebuilding the container.

Bash stays in the loop for the operations whose semantics must not fork:

* Reading the env file sources ``env-local.sh`` so the loader's rules (BOM,
  CRLF, ``export `` prefix, symmetric quotes) keep exactly one implementation.
  A hand-rolled parser that disagrees with the loader would compare the service
  against a string the loader never produces and recycle a healthy service on
  every start - the exact bug that moved parsing behind the loader once before.
  The child unsets every candidate key first, so a value inherited from the
  caller can never read as present in the file, and it sets
  ``DATAVIZ_ENV_LOCAL_PRECEDENCE=file`` so the file's value wins for the
  managed credential names.
* Probing, signalling, and reading the environment of the service process run
  through bash's ``kill`` and ``/proc``, so pid namespaces stay consistent on
  every host: MSYS pid tables differ from Windows pids, and ``os.kill(pid, 0)``
  on Windows terminates the process instead of probing it.

Safety properties, each covered by a contract test:

* A service whose credential MATCHES .env.local is never touched.
* A record whose pid is gone is removed (clients must not attach to it).
* No .env.local means nothing to compare against, so nothing to do.
* Credential VALUES are never printed - only variable NAMES.
* Missing evidence fails safe: a missing loader, an unreadable environment, or
  an empty file value leaves a key unjudged, and an unjudged key never kills.

Usage:
    python3 recycle_stale_opencode_service.py [--dry-run] [--state-dir DIR]
                                              [--env-file FILE]

Exit codes: 0 = healthy, recycled, or nothing to do; 1 = unexpected error;
2 = usage error.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

# Credential names worth comparing. The github pair and the z.ai pair are the
# ones that circulate under two names; the rest are single-name secrets whose
# rotation has the same staleness problem.
CREDENTIAL_KEYS: Tuple[str, ...] = (
    "GITHUB_PERSONAL_ACCESS_TOKEN",
    "GITHUB_MCP_PAT",
    "GITHUB_TOKEN",
    "ZAI_API_KEY",
    "Z_AI_API_KEY",
)

SCRIPT_DIR = Path(__file__).resolve().parent

# The bash child templates. Each one is a fixed string, so nothing the record
# or the environment contains can reach a shell parser: pids and paths travel
# as argv, never as interpolated script text.
ENV_VALUES_CHILD = """
loader="$1"
env_file="$2"
shift 2
for key in "$@"; do
    unset "$key"
done
. "$loader"
DATAVIZ_ENV_LOCAL_PRECEDENCE=file
load_env_local "$env_file"
for key in "$@"; do
    value="${!key-}"
    if [ -n "$value" ]; then
        printf '%s=%s\\0' "$key" "$value"
    fi
done
"""

SERVICE_ENVIRON_CHILD = 'cat "/proc/$1/environ" 2>/dev/null'

PID_ALIVE_CHILD = 'kill -0 "$1" 2>/dev/null'

SIGNAL_CHILD_TEMPLATE = 'kill {signal} "$1" 2>/dev/null'


def _bash_child_output(script: str, arguments: Sequence[str]) -> bytes:
    """Run one bash child and return its stdout bytes, or empty on any failure.

    A failed child (missing bash, a loader that cannot source, a dead pid's
    unreadable /proc entry) returns empty output; every caller treats empty as
    "no evidence", which is the fail-safe direction.
    """
    try:
        completed = subprocess.run(
            ["bash", "-c", script, "recycle", *arguments],
            capture_output=True,
            check=False,
        )
    except OSError:
        return b""
    return completed.stdout if completed.returncode == 0 else b""


def _bash_child_exit(script: str, arguments: Sequence[str]) -> int:
    """Run one bash child and return its exit code; 125 when bash cannot run."""
    try:
        completed = subprocess.run(
            ["bash", "-c", script, "recycle", *arguments],
            capture_output=True,
            check=False,
        )
    except OSError:
        return 125
    return completed.returncode


def _parse_env_entries(raw: bytes) -> Dict[str, str]:
    """Parse NUL-separated KEY=VALUE bytes into a dict of the tracked keys.

    Values are decoded with ``surrogateescape`` so a byte sequence that is not
    valid UTF-8 survives the round trip instead of being replaced into a
    different string. Empty values are dropped: the loader's own precedence
    never produces a meaningful empty credential, and the bash original treated
    an empty file value as absent.
    """
    values: Dict[str, str] = {}
    for entry in raw.split(b"\0"):
        key_bytes, separator, value_bytes = entry.partition(b"=")
        if not separator or not value_bytes:
            continue
        key = key_bytes.decode("utf-8", errors="surrogateescape")
        if key in CREDENTIAL_KEYS:
            values[key] = value_bytes.decode("utf-8", errors="surrogateescape")
    return values


def read_env_file_values(env_file: Path) -> Dict[str, str]:
    """Return the tracked credential keys the env file defines, with their values.

    The values come from the sanctioned loader, so the recycle decision compares
    the service against exactly the strings a real session would load.
    """
    loader = SCRIPT_DIR / "env-local.sh"
    if not loader.is_file():
        # Standalone fallback the bash original had (a copied script without
        # its sibling loader) is deliberately gone: without the loader there
        # are no file values, and no file values means nothing is killed.
        return {}
    raw = _bash_child_output(ENV_VALUES_CHILD, [str(loader), str(env_file), *CREDENTIAL_KEYS])
    return _parse_env_entries(raw)


def service_environment(pid: int) -> Dict[str, str]:
    """Return the tracked credential keys the service process environment holds.

    A zombie or otherwise unreadable /proc entry yields nothing; the caller
    treats an absent key as "cannot judge", never as a mismatch.
    """
    raw = _bash_child_output(SERVICE_ENVIRON_CHILD, [str(pid)])
    return _parse_env_entries(raw)


def pid_alive(pid: int) -> bool:
    """Whether the pid is alive, judged by bash's ``kill -0`` exit code.

    An unpermitted live process reads as not alive, exactly as the bash
    original's ``kill -0`` did; in the container the service runs as the same
    user, so the case stays theoretical.
    """
    return _bash_child_exit(PID_ALIVE_CHILD, [str(pid)]) == 0


def signal_service(pid: int, signal: str) -> None:
    """Send a signal to the service through bash's kill builtin, ignoring errors.

    A kill that cannot be delivered leaves the decision to the wait loop and
    the record removal below, like the bash original's ``|| true``.
    """
    _bash_child_exit(SIGNAL_CHILD_TEMPLATE.format(signal=signal), [str(pid)])


def read_service_pid(record: Path) -> Optional[int]:
    """Extract the service pid from the discovery record, or None when unusable.

    opencode writes the record as JSON with an integer pid. Anything else
    (unparsable bytes, a missing pid, a non-positive pid) takes the bash
    original's no-pid path: the record cannot name a live service and is
    removed, so clients never attach to a record that points nowhere.
    """
    try:
        record_data = json.loads(record.read_text(encoding="utf-8", errors="replace"))
    except (OSError, ValueError):
        return None
    if not isinstance(record_data, dict):
        return None
    pid = record_data.get("pid")
    if isinstance(pid, bool) or not isinstance(pid, (int, str)):
        return None
    if isinstance(pid, str):
        if not pid.isdigit():
            return None
        return int(pid)
    return pid if pid > 0 else None


def remove_record(record: Path) -> None:
    """Delete the discovery record; a missing record is already the goal state."""
    record.unlink(missing_ok=True)


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="recycle-stale-opencode-service",
        description=(
            "Recycle the shared OpenCode background service when its MCP "
            "credentials disagree with .env.local."
        ),
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="report findings without killing the service or removing the record",
    )
    state_parent = os.environ.get("XDG_STATE_HOME") or os.path.join(str(Path.home()), ".local", "state")
    parser.add_argument(
        "--state-dir",
        default=os.path.join(state_parent, "opencode"),
        help="the opencode state directory holding service.json (default: %(default)s)",
    )
    workspace = os.environ.get("WORKSPACE_ROOT") or os.getcwd()
    parser.add_argument(
        "--env-file",
        default=os.path.join(workspace, ".env.local"),
        help="the credential file to compare against (default: %(default)s)",
    )
    options = parser.parse_args(argv)

    record = Path(options.state_dir) / "service.json"
    env_file = Path(options.env_file)

    if not record.is_file():
        # No service recorded: nothing to recycle (normal before first use).
        return 0

    pid = read_service_pid(record)
    if pid is None:
        print("recycle: discovery record has no pid; removing it")
        if not options.dry_run:
            remove_record(record)
        return 0

    if not pid_alive(pid):
        print(f"recycle: service pid {pid} not running; removing stale discovery record")
        if not options.dry_run:
            remove_record(record)
        return 0

    if not env_file.is_file():
        # Nothing to compare against. Never kill on no evidence.
        return 0

    file_values = read_env_file_values(env_file)
    service_values = service_environment(pid)
    stale_keys: List[str] = []
    for key in CREDENTIAL_KEYS:
        service_value = service_values.get(key, "")
        file_value = file_values.get(key, "")
        # Compare each credential the service holds against .env.local. Report
        # NAMES only; the values themselves are compared, never displayed.
        if service_value and file_value and service_value != file_value:
            stale_keys.append(key)

    if not stale_keys:
        return 0

    stale_names = "".join(f" {key}" for key in stale_keys)
    print(f"recycle: opencode service pid {pid} holds a stale credential for:{stale_names}")
    print("recycle: .env.local has newer values; the service will respawn from the current environment")

    if options.dry_run:
        print("recycle: dry run — service left running")
        return 0

    signal_service(pid, "")
    # Give it a moment to exit so a client does not race the record removal.
    # ``kill -0`` alone cannot tell a live process from a zombie that nobody
    # reaped yet, but a zombie holds no environment: an empty /proc environ is
    # the state the record removal must not race, because a process in it can
    # no longer serve clients with stale credentials. A service that ignores
    # SIGTERM keeps its environment and still earns the SIGKILL.
    for _ in range(10):
        if not service_environment(pid):
            break
        time.sleep(0.2)
    if service_environment(pid):
        print(f"recycle: service pid {pid} did not exit on SIGTERM; sending SIGKILL")
        signal_service(pid, "-9")
        time.sleep(0.3)

    # The record must go: a client that read it would otherwise attach to a dead pid.
    remove_record(record)
    print(f"recycle: recycled pid {pid}; next agent session starts a fresh service")
    return 0


if __name__ == "__main__":
    sys.exit(main())
