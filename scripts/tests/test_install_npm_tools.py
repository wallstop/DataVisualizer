#!/usr/bin/env python3
"""Offline npm refresh contracts: never touch the installed coding CLIs."""
from __future__ import annotations

import contextlib
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Dict, List, Optional, Sequence
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / ".devcontainer"))
import install_npm_tools as tool


class InstallerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="npm tools with spaces ")
        self.root = Path(self.temp.name)
        self.prefix = self.root / "prefix"
        self.cache = self.root / "cache"
        self.versions: Dict[str, str] = {package: "1.0" for package, _ in tool.TOOLS}
        self.latest = "1.0"
        self.commands: List[List[str]] = []
        self.registry_code = self.install_code = self.list_code = self.cache_code = 0
        self.bad_json = self.shadow = self.version_failure = False
        self.timeout = False
        self.node_version = "v22.1.0"
        for _, command in tool.TOOLS:
            self.binary(command).parent.mkdir(parents=True, exist_ok=True)
            self.binary(command).touch()
            self.binary(command).chmod(0o755)
        self.original_run = tool.run
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.addCleanup(self.temp.cleanup)
        self.stack.enter_context(patch.dict(os.environ, {"DATAVIZ_NPM_INSTALL_ATTEMPTS": "1", "DATAVIZ_NPM_TOOL_CACHE": str(self.cache), "DATAVIZ_NPM_CLEAN_CACHE": "0"}))
        self.stack.enter_context(patch.object(tool.os, "geteuid", return_value=1000, create=True))
        self.stack.enter_context(patch.object(tool.shutil, "which", side_effect=self.which))
        self.stack.enter_context(patch.object(tool, "run", side_effect=self.fake_run))
        self.stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
        self.stderr = self.stack.enter_context(contextlib.redirect_stderr(io.StringIO()))

    def binary(self, command: str) -> Path:
        return self.prefix / "bin" / command

    def which(self, command: str) -> str:
        if command in ("node", "npm"):
            return command
        return str(self.root / "shadow" if self.shadow else self.binary(command))

    def fake_run(self, command: Sequence[str], timeout: Optional[int] = None) -> subprocess.CompletedProcess[str]:
        args = list(command)
        self.commands.append(args)
        code, output = 0, ""
        if args == ["node", "--version"]:
            output = self.node_version
        elif args == ["npm", "prefix", "--global"]:
            output = str(self.prefix)
        elif args == ["npm", "config", "get", "cache"]:
            output = str(self.cache)
        elif args == ["npm", "list", "--global", "--depth=0", "--json"]:
            code = self.list_code
            output = "bad-json" if self.bad_json else json.dumps({"dependencies": {name: {"version": version} for name, version in self.versions.items()}})
        elif args[:2] == ["npm", "view"]:
            code, output = self.registry_code, self.latest
        elif args[:2] == ["npm", "install"]:
            code = self.install_code
            if code == 0:
                for _, name in tool.TOOLS:
                    self.binary(name).touch()
                    self.binary(name).chmod(0o755)
        elif args[:3] == ["npm", "cache", "clean"]:
            code = self.cache_code
        elif args[-1:] == ["--version"] and args[0] != "npm":
            self.assertEqual(timeout, 30)
            if self.timeout:
                raise subprocess.TimeoutExpired(args, 30)
            code = 1 if self.version_failure else 0
            output = "tool-version"
        return subprocess.CompletedProcess(args, code, output, "")

    def count(self, operation: str) -> int:
        return sum(command[:2] == ["npm", operation] for command in self.commands)

    def test_current_tools_do_not_install_or_clean_cache(self) -> None:
        self.list_code = 1
        self.assertEqual(tool.main([]), 0)
        self.assertEqual(self.count("install"), 0)
        self.assertEqual(self.count("cache"), 0)
        self.assertIn("valid dependency tree will be used", self.stderr.getvalue())
        self.assertEqual(os.environ["NPM_CONFIG_CACHE"], str(self.cache))

    def test_stale_and_missing_commands_update_with_trusted_scripts(self) -> None:
        self.versions[tool.TOOLS[0][0]] = "old"
        self.binary("codex").unlink()
        self.assertEqual(tool.main([]), 0)
        installs = [command for command in self.commands if command[:2] == ["npm", "install"]]
        self.assertEqual(installs, [["npm", "install", "--global", "--allow-scripts=@opencode/cli,@anthropic-ai/claude-code", tool.TOOLS[0][0] + "@latest", tool.TOOLS[2][0] + "@latest"]])
        self.assertEqual(self.count("cache"), 1)

    def test_registry_failure_requires_intact_offline_fallback(self) -> None:
        self.registry_code = 9
        self.assertEqual(tool.main([]), 1)
        self.assertEqual(tool.main(["--offline-ok"]), 0)
        self.binary("claude").unlink()
        self.assertEqual(tool.main(["--offline-ok"]), 1)

    def test_failed_install_preserves_exit_and_checks_all_fallbacks(self) -> None:
        self.latest = "2.0"
        self.install_code = 7
        self.assertEqual(tool.main([]), 7)
        self.assertEqual(tool.main(["--offline-ok"]), 0)
        self.versions.pop(tool.TOOLS[0][0])
        self.assertEqual(tool.main(["--offline-ok"]), 7)
        self.assertEqual(self.count("cache"), 3)

    def test_bad_json_shadowed_path_version_failure_and_timeout_fail(self) -> None:
        for field in ("bad_json", "shadow", "version_failure", "timeout"):
            with self.subTest(field=field):
                setattr(self, field, True)
                self.assertEqual(tool.main([]), 1)
                setattr(self, field, False)

    def test_root_old_node_bad_attempts_and_unwritable_paths_fail(self) -> None:
        if os.name == "posix":
            with patch.object(tool.os, "geteuid", return_value=0):
                self.assertEqual(tool.main([]), 1)
        self.node_version = "v20.0.0"
        self.assertEqual(tool.main([]), 1)
        self.node_version = "v22.0.0"
        with patch.dict(os.environ, {"DATAVIZ_NPM_INSTALL_ATTEMPTS": "0"}):
            self.assertEqual(tool.main([]), 2)
        with patch.object(tool.os, "access", return_value=False):
            self.assertEqual(tool.main([]), 1)

    def test_missing_command_spawn_and_cache_failure_codes(self) -> None:
        with patch.object(tool.shutil, "which", return_value=None):
            self.assertEqual(tool.main([]), 1)
        with patch.object(tool, "run", side_effect=OSError("spawn failed")):
            self.assertEqual(tool.main([]), 1)
        self.latest, self.cache_code = "2.0", 3
        self.assertEqual(tool.main([]), 3)
        self.install_code = 7
        self.assertEqual(tool.main([]), 7)

    def test_cleanup_spawn_failure_preserves_install_failure(self) -> None:
        self.latest, self.install_code = "2.0", 7

        def cleanup_failure(command: Sequence[str], timeout: Optional[int] = None) -> subprocess.CompletedProcess[str]:
            if list(command)[:3] == ["npm", "cache", "clean"]:
                raise OSError("cleanup spawn failed")
            return self.fake_run(command, timeout)

        with patch.object(tool, "run", side_effect=cleanup_failure):
            self.assertEqual(tool.main([]), 7)
        self.assertIn("cleanup spawn failed", self.stderr.getvalue())

    def test_signal_exits_keep_shell_convention(self) -> None:
        with patch.object(tool.subprocess, "run", return_value=subprocess.CompletedProcess(["fixture"], -15, "", "")):
            self.assertEqual(self.original_run(["fixture"]).returncode, 143)


    def test_retries_keep_exponential_delays_and_last_exit(self) -> None:
        self.registry_code = 7
        with patch.object(tool.time, "sleep") as sleep:
            result = tool.retry(["npm", "view", "fixture", "version"], 3)
            self.assertEqual(result.returncode, 7)
            self.assertEqual([call.args for call in sleep.call_args_list], [(2,), (4,)])
        with patch.dict(os.environ, {"DATAVIZ_NPM_CLEAN_CACHE": "1"}):
            self.registry_code = 0
            self.assertEqual(tool.main([]), 0)
            self.assertEqual(self.count("cache"), 1)


if __name__ == "__main__":
    unittest.main()
