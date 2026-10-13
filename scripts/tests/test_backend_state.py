#!/usr/bin/env python3
"""Offline private profile and approval-state contracts (no real user homes)."""
from __future__ import annotations

import json
import os
import stat
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / ".devcontainer"))
import backend_state as state


class StateTests(unittest.TestCase):
    def test_profiles_are_private_and_unchanged_content_keeps_mtime(self) -> None:
        with tempfile.TemporaryDirectory(prefix="profile with spaces ") as directory:
            root = Path(directory)
            self.assertEqual(state.write_profile("zai", root), 2)
            self.assertEqual(state.write_profile("openrouter", root), 1)
            files = sorted(root.iterdir())
            self.assertEqual(len(files), 3)
            before = [(path.read_bytes(), path.stat().st_mtime_ns) for path in files]
            self.assertEqual(state.write_profile("zai", root), 0)
            self.assertEqual(state.write_profile("openrouter", root), 0)
            self.assertEqual([(path.read_bytes(), path.stat().st_mtime_ns) for path in files], before)
            if os.name == "posix":
                self.assertTrue(all(stat.S_IMODE(path.stat().st_mode) == 0o600 for path in files))
            self.assertEqual(json.loads((root / "devcontainer-zai-models.json").read_text())["models"][0]["slug"], "glm-5.3")
            self.assertIn('echo $OPENROUTER_API_KEY', (root / "devcontainer-openrouter.config.toml").read_text())

    def test_approval_union_preserves_foreign_fields_projects_and_servers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            mcp = root / "mcp.json"
            mcp.write_text(json.dumps({"mcpServers": {"unity": {}, "github": {}}}))
            target = root / ".claude.json"
            target.write_text(json.dumps({"foreign": 7, "projects": {"other": {"keep": True}, "workspace": {"keep": 9, "enabledMcpjsonServers": ["extra", "unity", "extra"]}}}))
            self.assertTrue(state.seed_approvals(root, mcp, "workspace"))
            result = json.loads(target.read_text())
            self.assertEqual(result["foreign"], 7)
            self.assertEqual(result["projects"]["other"], {"keep": True})
            self.assertEqual(result["projects"]["workspace"], {"keep": 9, "hasTrustDialogAccepted": True, "enabledMcpjsonServers": ["extra", "github", "unity"]})
            timestamp = target.stat().st_mtime_ns
            self.assertFalse(state.seed_approvals(root, mcp, "workspace"))
            self.assertEqual(target.stat().st_mtime_ns, timestamp)

    def test_missing_mcp_is_a_noop_and_bad_state_is_never_overwritten(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            mcp = root / "mcp.json"
            self.assertFalse(state.seed_approvals(root, mcp, "workspace"))
            self.assertFalse((root / ".claude.json").exists())
            mcp.write_text('{"mcpServers":{"unity":{}}}')
            target = root / ".claude.json"
            for invalid in ('bad-json', '{"projects":[]}', '{"projects":{"workspace":{"enabledMcpjsonServers":[1]}}}'):
                target.write_text(invalid)
                with self.assertRaises(ValueError):
                    state.seed_approvals(root, mcp, "workspace")
                self.assertEqual(target.read_text(), invalid)
            target.write_text('{}')
            mcp.write_text('bad-json')
            self.assertEqual(state.main(["approve", "--config-dir", str(root), "--mcp-config", str(mcp), "--project", "workspace"]), 1)
            self.assertEqual(target.read_text(), '{}')

    @unittest.skipUnless(os.name == "posix", "file symlinks require POSIX or Windows developer privileges")
    def test_unchanged_symlink_is_replaced_without_changing_its_target(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            foreign = root / "foreign"
            foreign.write_text("profile")
            foreign.chmod(0o644)
            target = root / "profile"
            target.symlink_to(foreign)
            self.assertTrue(state.write_private(target, "profile"))
            self.assertFalse(target.is_symlink())
            self.assertEqual(foreign.read_text(), "profile")
            self.assertEqual(stat.S_IMODE(foreign.stat().st_mode), 0o644)
            self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o600)

    @unittest.skipUnless(os.name == "posix" and shutil.which("bash") is not None, "fixture executables require POSIX bash")
    def test_launcher_falls_back_when_python3_cannot_execute(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            binary = root / "bin"
            binary.mkdir()
            broken = binary / "python3"
            broken.write_text("#!/bin/sh\nexit 1\n")
            broken.chmod(0o755)
            (binary / "python").symlink_to(sys.executable)
            env = {
                "PATH": str(binary) + os.pathsep + os.environ["PATH"],
                "HOME": str(root / "home"),
                "CODEX_HOME": str(root / "codex"),
                "AI_BACKENDS_ENV_FILE": str(root / "missing.env"),
                "AI_BACKENDS_MCP_CONFIG": str(root / "missing-mcp.json"),
                "AI_BACKENDS_BIN_DIR": str(binary),
            }
            launcher = Path(__file__).resolve().parents[2] / ".devcontainer" / "ai-backends.sh"
            result = subprocess.run(["bash", str(launcher), "install"], env=env, capture_output=True, text=True, check=False)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue((root / "codex" / "devcontainer-zai.config.toml").is_file())

    def test_replace_failure_keeps_original_and_removes_staging(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "profile"
            target.write_text("original")
            with patch.object(state.os, "replace", side_effect=OSError("replace failed")):
                with self.assertRaises(OSError):
                    state.write_private(target, "replacement")
            self.assertEqual(target.read_text(), "original")
            self.assertEqual(list(Path(directory).glob(".backend-state-*")), [])


if __name__ == "__main__":
    unittest.main()
