#!/usr/bin/env python3
"""mcp-credentials contract tests (typed Python port of ``test-mcp-credentials.sh``, issue #171).

Every case from the bash original is kept with the same assertions; only
the driver language changed. The subjects stay the node scripts
(``configure.mjs``, ``mcp-doctor.mjs``), so each case still drives the
real script; the probe-stub HTTP endpoint becomes a stdlib in-process
server that accepts exactly one bearer token and 401s everything else,
which keeps the repair cases deterministic.

These tests exist because of a real 401 incident: .env.local ended up with
BOTH GITHUB_PERSONAL_ACCESS_TOKEN (stale/revoked) and GITHUB_MCP_PAT
(valid), every generated config referenced the stale name, and both
configure.mjs and mcp-doctor.mjs reported success because they only ever
checked *presence*.

Everything runs in a throwaway workspace with throwaway HOME and synthetic
placeholder credentials. No real secret is read, written, or printed.
Cases skip when node is unavailable, like the bash-required cases above.
"""

from __future__ import annotations

import http.server
import json
import os
import re
import shutil
import subprocess
import sys
import threading
from pathlib import Path
from typing import Dict, Optional

sys.dont_write_bytecode = True

from _test_helpers import assert_true, new_temp_root, remove_temp_root, run_cases

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
SYNC = REPO_ROOT / ".llm" / "mcp" / "sync-mcp.sh"
DOCTOR = REPO_ROOT / ".llm" / "mcp" / "mcp-doctor.mjs"
CONFIGURE = REPO_ROOT / ".llm" / "mcp" / "configure.mjs"

# Synthetic, obviously-fake placeholders. These are NOT credentials.
STALE = "ghp_0000000000000000000000000000000000"
LIVE = "ghp_1111111111111111111111111111111111"

# The credential vars are read from process.env FIRST (env() in both scripts),
# so the sandbox must not inherit the developer's real tokens or the test
# would measure the host environment instead of the fixture.
_SCRUBBED_KEYS = (
    "GITHUB_PERSONAL_ACCESS_TOKEN",
    "GITHUB_MCP_PAT",
    "GITHUB_TOKEN",
    "ZAI_API_KEY",
    "Z_AI_API_KEY",
)


class StubEndpoint:
    """HTTP stub that accepts exactly one bearer token and 401s everything else.

    This lets the repair path be proven deterministically: which name is
    live, and therefore which value must be written, is decided by the stub,
    not by luck. The kernel assigns the port, so there is no wait-for-port
    dance like the bash suite's stub file.
    """

    def __init__(self, accept_token: str) -> None:
        accepted = accept_token

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_POST(self) -> None:  # noqa: N802 - http.server's name
                auth = self.headers.get("Authorization") or ""
                if auth == f"Bearer {accepted}":
                    body = json.dumps({"jsonrpc": "2.0", "id": 1, "result": {"serverInfo": {"name": "stub"}}})
                    self.send_response(200)
                    self.send_header("content-type", "application/json")
                    self.end_headers()
                    self.wfile.write(body.encode("utf-8"))
                else:
                    self.send_response(401)
                    self.send_header("content-type", "application/json")
                    self.end_headers()
                    self.wfile.write(b'{"error": "unauthorized"}')

            def log_message(self, format: str, *args: object) -> None:  # noqa: A002 - http.server's name
                pass

        self._server = http.server.HTTPServer(("127.0.0.1", 0), Handler)
        self.port = self._server.server_address[1]
        self._thread = threading.Thread(target=self._server.serve_forever, daemon=True)
        self._thread.start()

    @property
    def url(self) -> str:
        return f"http://127.0.0.1:{self.port}/mcp/"

    def stop(self) -> None:
        self._server.shutdown()
        self._server.server_close()


class Sandbox:
    """Hermetic fixture workspace for one credential case."""

    def __init__(self) -> None:
        self.root = new_temp_root(prefix="mcp-cred-test-")
        self.home = self.root / "home"
        self.codex_home = self.root / "codex-home"
        self.home.mkdir()
        self.codex_home.mkdir()
        self.env_file = self.root / ".env.local"
        self.last_exit = -1
        self.stdout = ""

    def write_env_file(self, content: str) -> None:
        self.env_file.write_text(content, encoding="utf-8", newline="")

    def read_env_file(self) -> str:
        # newline="" keeps the bytes verbatim: the BOM and line-ending cases
        # pin exact file bytes, so no implicit newline translation may run.
        with open(self.env_file, "r", encoding="utf-8", newline="") as handle:
            return handle.read()

    def run_isolated(self, script: Path, *arguments: str, extra_env: Optional[Dict[str, str]] = None) -> None:
        """Run one node subject inside the sandbox, merged output like ``2>&1``."""
        env = dict(os.environ)
        for key in _SCRUBBED_KEYS:
            env.pop(key, None)
        env.update(
            {
                "DATAVIZ_MCP_WORKSPACE": str(self.root),
                "DATAVIZ_IN_CONTAINER": "1",
                "CODEX_HOME": str(self.codex_home),
                "HOME": str(self.home),
                "UNITY_MCP_HTTP_PORT": os.environ.get("UNITY_MCP_HTTP_PORT") or "9020",
                "DATAVIZ_MCP_STUB_SKIP_PROBES": "1",
            }
        )
        if os.name == "nt":
            # Windows tools resolve the home through USERPROFILE; keep both
            # names pointing inside the sandbox.
            env["USERPROFILE"] = str(self.home)
        if extra_env:
            env.update(extra_env)
        completed = subprocess.run(
            ["node", str(script), *arguments],
            cwd=str(self.root),
            env=env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        self.last_exit = completed.returncode
        self.stdout = (completed.stdout or "") + (completed.stderr or "")

    def destroy(self) -> None:
        remove_temp_root(self.root)


def assert_eq(description: str, expected: str, actual: str) -> None:
    assert_true(expected == actual, f"{description} (expected: [{expected}]; actual: [{actual}])")


def assert_contains(description: str, needle: str, haystack: str) -> None:
    # Case-insensitive: configure.mjs prints uppercase status lines ("DIVERGENT")
    # while the doctor emits prose ("divergent"), and neither casing is contractual.
    assert_true(needle.lower() in haystack.lower(), f"{description} (expected to contain: [{needle}]; actual: [{haystack}])")


def assert_not_contains(description: str, unwanted: str, haystack: str) -> None:
    assert_true(unwanted not in haystack, f"{description} (expected NOT to contain: [{unwanted}]; actual: [{haystack}])")


def read_canonical_value(sandbox: Sandbox, key: str) -> str:
    match = re.search(rf"^{re.escape(key)}=(.*)$", sandbox.read_env_file(), flags=re.MULTILINE)
    return match.group(1) if match else ""


def count_keys(sandbox: Sandbox, key: str) -> int:
    return len(re.findall(rf"^[ \t]*(?:export[ \t]+)?{re.escape(key)}=", sandbox.read_env_file(), flags=re.MULTILINE))


# ---------------------------------------------------------------------------
# --repair writes only after a probe proves which name is live
# ---------------------------------------------------------------------------


def case_repair_reconciles_divergent_github_pair() -> None:
    # GITHUB_MCP_PAT holds a token the endpoint accepts; the canonical name holds
    # a rejected one. --repair must copy the live value over the stale key.
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={STALE}\nGITHUB_MCP_PAT={LIVE}\n")
    stub = StubEndpoint(LIVE)
    try:
        sandbox.run_isolated(
            DOCTOR,
            "--repair",
            extra_env={
                "DATAVIZ_MCP_GITHUB_PROBE_URL": stub.url,
                "DATAVIZ_MCP_ZAI_PROBE_URL": stub.url,
                "DATAVIZ_MCP_PROBE_TIMEOUT_MS": "5000",
            },
        )
    finally:
        stub.stop()
    assert_eq(
        "repair copies the probe-proven live value over the stale canonical key",
        LIVE,
        read_canonical_value(sandbox, "GITHUB_PERSONAL_ACCESS_TOKEN"),
    )
    assert_contains("repair reports which name was live", "GITHUB_MCP_PAT authenticates", sandbox.stdout)
    assert_contains("repair names the key it rewrote", "Repairing .env.local", sandbox.stdout)
    sandbox.destroy()


def case_repair_refuses_when_neither_credential_authenticates() -> None:
    # Neither name works: the correct action is to change nothing and say so.
    # Guessing here is how a stale value gets blessed as "fixed".
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={STALE}\nGITHUB_MCP_PAT={LIVE}\n")
    stub = StubEndpoint("ghp_this-token-is-neither-of-them")
    try:
        sandbox.run_isolated(
            DOCTOR,
            "--repair",
            extra_env={
                "DATAVIZ_MCP_GITHUB_PROBE_URL": stub.url,
                "DATAVIZ_MCP_ZAI_PROBE_URL": stub.url,
                "DATAVIZ_MCP_PROBE_TIMEOUT_MS": "5000",
            },
        )
    finally:
        stub.stop()
    assert_eq(
        "unprovable repair leaves the canonical key untouched",
        STALE,
        read_canonical_value(sandbox, "GITHUB_PERSONAL_ACCESS_TOKEN"),
    )
    assert_contains("unprovable repair says neither name authenticates", "neither name authenticates", sandbox.stdout)
    sandbox.destroy()


def case_repair_respects_reverse_divergence() -> None:
    # The live credential may be the CANONICAL name. Repair must then repair the
    # alternate, i.e. it must not be hardcoded to always bless GITHUB_MCP_PAT.
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\nGITHUB_MCP_PAT={STALE}\n")
    stub = StubEndpoint(LIVE)
    try:
        sandbox.run_isolated(
            DOCTOR,
            "--repair",
            extra_env={
                "DATAVIZ_MCP_GITHUB_PROBE_URL": stub.url,
                "DATAVIZ_MCP_ZAI_PROBE_URL": stub.url,
                "DATAVIZ_MCP_PROBE_TIMEOUT_MS": "5000",
            },
        )
    finally:
        stub.stop()
    assert_eq(
        "repair copies the canonical value over a stale GITHUB_MCP_PAT",
        LIVE,
        read_canonical_value(sandbox, "GITHUB_MCP_PAT"),
    )
    sandbox.destroy()


def case_repair_leaves_matching_pair_untouched() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\nGITHUB_MCP_PAT={LIVE}\n")
    before = sandbox.read_env_file()
    sandbox.run_isolated(DOCTOR, "--repair")
    assert_eq("repair is a no-op when names already agree", before, sandbox.read_env_file())
    sandbox.destroy()


def case_repair_never_prints_secret_values() -> None:
    sandbox = Sandbox()
    sandbox.write_env_file(
        f"GITHUB_PERSONAL_ACCESS_TOKEN={STALE}\nGITHUB_MCP_PAT={LIVE}\nZAI_API_KEY=zai-a-placeholder\nZ_AI_API_KEY=zai-b-placeholder\n"
    )
    sandbox.run_isolated(DOCTOR, "--repair")
    assert_not_contains("repair output omits stale token", STALE, sandbox.stdout)
    assert_not_contains("repair output omits live token", LIVE, sandbox.stdout)
    assert_not_contains("repair output omits zai primary", "zai-a-placeholder", sandbox.stdout)
    assert_not_contains("repair output omits zai alternate", "zai-b-placeholder", sandbox.stdout)
    sandbox.destroy()


def case_repair_preserves_bom_and_line_endings() -> None:
    # A repair rewrites the file, so it must be byte-faithful apart from the one
    # value: a dropped UTF-8 BOM or a mangled trailing newline is an invisible
    # change to a gitignored credential store. The loaders are BOM-tolerant, but
    # other tools reading .env.local may not be.
    sandbox = Sandbox()
    sandbox.env_file.write_bytes(f"\ufeffGITHUB_PERSONAL_ACCESS_TOKEN={STALE}\nGITHUB_MCP_PAT={LIVE}\n".encode("utf-8"))
    stub = StubEndpoint(LIVE)
    try:
        sandbox.run_isolated(
            DOCTOR,
            "--repair",
            extra_env={
                "DATAVIZ_MCP_GITHUB_PROBE_URL": stub.url,
                "DATAVIZ_MCP_ZAI_PROBE_URL": stub.url,
                "DATAVIZ_MCP_PROBE_TIMEOUT_MS": "5000",
            },
        )
    finally:
        stub.stop()
    raw = sandbox.env_file.read_bytes()
    assert_eq("repair preserves the UTF-8 BOM", "efbbbf", raw[:3].hex())
    assert_eq("repair preserves the trailing newline", "0a", raw[-1:].hex())
    sandbox.destroy()


def case_repair_does_not_duplicate_keys() -> None:
    # In-place rewrite only: appending a second copy of a key is last-wins for
    # the parser but grows the file on every run.
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={STALE}\nGITHUB_MCP_PAT={LIVE}\n")
    sandbox.run_isolated(DOCTOR, "--repair")
    assert_eq("GITHUB_PERSONAL_ACCESS_TOKEN appears exactly once after repair", "1", str(count_keys(sandbox, "GITHUB_PERSONAL_ACCESS_TOKEN")))
    assert_eq("GITHUB_MCP_PAT appears exactly once after repair", "1", str(count_keys(sandbox, "GITHUB_MCP_PAT")))
    sandbox.destroy()


# ---------------------------------------------------------------------------
# configure.mjs: alias divergence must be detected, not treated as success
# ---------------------------------------------------------------------------


def case_sync_reports_divergent_github_alias() -> None:
    # RED for the original bug: both names set to different values used to
    # short-circuit to "configured". That is the exact state that produced the
    # 401, so it must be surfaced distinctly.
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={STALE}\nGITHUB_MCP_PAT={LIVE}\n")
    sandbox.run_isolated(CONFIGURE, "--check")
    assert_contains("sync --check flags divergent github alias", "divergent", sandbox.stdout)
    assert_contains("sync --check names the stale var in use", "GITHUB_PERSONAL_ACCESS_TOKEN", sandbox.stdout)
    sandbox.destroy()


def case_sync_reports_divergent_zai_alias() -> None:
    # Same defect shape for the Z.ai key pair.
    sandbox = Sandbox()
    sandbox.write_env_file("ZAI_API_KEY=zai-live-placeholder\nZ_AI_API_KEY=zai-stale-placeholder\n")
    sandbox.run_isolated(CONFIGURE, "--check")
    assert_contains("sync --check flags divergent zai alias", "divergent", sandbox.stdout)
    sandbox.destroy()


def case_sync_reports_matching_alias_as_configured() -> None:
    # Guard against over-eager divergence reporting: identical values are fine.
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={LIVE}\nGITHUB_MCP_PAT={LIVE}\n")
    sandbox.run_isolated(CONFIGURE, "--check")
    assert_not_contains("sync --check does not flag matching alias", "divergent", sandbox.stdout)
    assert_contains("sync --check confirms configured", "configured", sandbox.stdout)
    sandbox.destroy()


def case_sync_still_aliases_single_name() -> None:
    # The original feature must keep working: exactly one name present means it
    # is mirrored into the other.
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_MCP_PAT={LIVE}\n")
    sandbox.run_isolated(CONFIGURE)
    assert_eq("sync exits zero", "0", str(sandbox.last_exit))
    assert_eq(
        "single GITHUB_MCP_PAT is mirrored into GITHUB_PERSONAL_ACCESS_TOKEN",
        LIVE,
        read_canonical_value(sandbox, "GITHUB_PERSONAL_ACCESS_TOKEN"),
    )
    sandbox.destroy()


def case_sync_check_mode_never_writes() -> None:
    # --check must stay read-only even when a repair is pending.
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={STALE}\nGITHUB_MCP_PAT={LIVE}\n")
    before = sandbox.read_env_file()
    sandbox.run_isolated(CONFIGURE, "--check")
    assert_eq("--check leaves .env.local untouched", before, sandbox.read_env_file())
    sandbox.destroy()


# ---------------------------------------------------------------------------
# mcp-doctor.mjs: name the variable, and reconcile presence vs. probe
# ---------------------------------------------------------------------------


def case_doctor_names_the_credential_source_it_used() -> None:
    # The original doctor printed only "GitHub PAT resolved", leaving the
    # operator unable to tell WHICH var fed the endpoint probe.
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={STALE}\n")
    sandbox.run_isolated(DOCTOR)
    assert_contains("doctor names the github variable it resolved", "GITHUB_PERSONAL_ACCESS_TOKEN", sandbox.stdout)
    sandbox.destroy()


def case_doctor_flags_divergence_before_probing() -> None:
    # Divergence must be reported as its own actionable finding, and the doctor
    # must point at the repair command rather than implying the file is fine.
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={STALE}\nGITHUB_MCP_PAT={LIVE}\n")
    sandbox.run_isolated(DOCTOR)
    assert_contains("doctor flags divergent github credentials", "divergent", sandbox.stdout)
    assert_contains("doctor points at the repair command", "mcp:doctor --repair", sandbox.stdout)
    sandbox.destroy()


def case_doctor_never_prints_secret_values() -> None:
    # Safety contract: diagnostic output must never echo a credential.
    sandbox = Sandbox()
    sandbox.write_env_file(f"GITHUB_PERSONAL_ACCESS_TOKEN={STALE}\nGITHUB_MCP_PAT={LIVE}\nZAI_API_KEY=zai-secret-placeholder\n")
    sandbox.run_isolated(DOCTOR)
    assert_not_contains("doctor output omits the stale token", STALE, sandbox.stdout)
    assert_not_contains("doctor output omits the live token", LIVE, sandbox.stdout)
    assert_not_contains("doctor output omits the zai key", "zai-secret-placeholder", sandbox.stdout)
    sandbox.destroy()


CASES = (
    case_sync_reports_divergent_github_alias,
    case_sync_reports_divergent_zai_alias,
    case_sync_reports_matching_alias_as_configured,
    case_sync_still_aliases_single_name,
    case_sync_check_mode_never_writes,
    case_doctor_names_the_credential_source_it_used,
    case_doctor_flags_divergence_before_probing,
    case_doctor_never_prints_secret_values,
    case_repair_reconciles_divergent_github_pair,
    case_repair_refuses_when_neither_credential_authenticates,
    case_repair_respects_reverse_divergence,
    case_repair_leaves_matching_pair_untouched,
    case_repair_never_prints_secret_values,
    case_repair_preserves_bom_and_line_endings,
    case_repair_does_not_duplicate_keys,
)


def main() -> int:
    assert_true(SYNC.is_file(), f"sync script not found at {SYNC}")
    assert_true(DOCTOR.is_file(), f"doctor not found at {DOCTOR}")
    print("== mcp-credentials contract tests ==")
    if shutil.which("node") is None:
        print("    SKIP: node not found on PATH; mcp-credentials contract tests drive node subjects.")
        return 0
    failures = run_cases([(case.__name__, case) for case in CASES])
    print(f"== mcp-credentials: {failures} failure(s) ==")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
