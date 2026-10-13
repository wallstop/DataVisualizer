#!/usr/bin/env python3
"""Offline docs-driver contracts: installers, strictness, and failure isolation."""
from __future__ import annotations

import contextlib
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import List, Sequence, Tuple
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import build_docs


class BuildDocsTests(unittest.TestCase):
    def test_installers_build_modes_and_bootstrap(self) -> None:
        for installer, uv, serve, existing in (("auto", "/tools/uv", False, True), ("auto", None, False, False), ("uv", "/tools/uv", True, False), ("pip", "/tools/uv", True, True)):
            with self.subTest(installer=installer), tempfile.TemporaryDirectory(prefix="docs with spaces ") as directory:
                root = Path(directory)
                for name in ("requirements-docs.txt", "mkdocs.yml"):
                    (root / name).touch()
                python = build_docs.venv_python_path(root / ".docs-venv")
                if existing:
                    python.parent.mkdir(parents=True)
                    python.touch()
                calls: List[Tuple[Path, List[str]]] = []

                def invoke(executable: Path, arguments: Sequence[str], description: str) -> None:
                    calls.append((executable, list(arguments)))
                    if arguments[:2] == ["-m", "venv"]:
                        python.parent.mkdir(parents=True)
                        python.touch()

                with patch.object(build_docs.shutil, "which", return_value=uv), patch.object(build_docs, "invoke_checked", side_effect=invoke):
                    self.assertEqual(build_docs.main(["--root", directory, "--installer", installer] + (["--serve"] if serve else [])), 0)
                if not existing:
                    self.assertEqual(calls.pop(0), (Path(sys.executable), ["-m", "venv", str(root / ".docs-venv")]))
                install, build = calls
                use_uv = uv is not None and installer != "pip"
                self.assertEqual(install[0], Path(uv) if use_uv and uv is not None else python)
                self.assertEqual(install[1], ["pip", "install", "--quiet", "--python", str(python), "--requirement", str(root / "requirements-docs.txt")] if use_uv else ["-m", "pip", "install", "--quiet", "--disable-pip-version-check", "--requirement", str(root / "requirements-docs.txt")])
                self.assertEqual(build, (python, ["-m", "mkdocs", "serve" if serve else "build", *([] if serve else ["--strict"]), "--config-file", str(root / "mkdocs.yml")]))

    def test_missing_inputs_or_requested_uv_fail_before_running_tools(self) -> None:
        for missing in ("root", "requirements-docs.txt", "mkdocs.yml", "uv"):
            with self.subTest(missing=missing), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                for name in ("requirements-docs.txt", "mkdocs.yml"):
                    if name != missing:
                        (root / name).touch()
                with patch.object(build_docs.shutil, "which", return_value=None), patch.object(build_docs, "invoke_checked") as invoke:
                    self.assertEqual(build_docs.main(["--root", str(root / "absent" if missing == "root" else root), "--installer", "uv"]), 1)
                    invoke.assert_not_called()

    def test_installer_failure_never_builds_or_falls_back(self) -> None:
        for installer, error in (("uv", RuntimeError("install failed")), ("pip", OSError("spawn failed"))):
            with self.subTest(installer=installer), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                for name in ("requirements-docs.txt", "mkdocs.yml"):
                    (root / name).touch()
                python = build_docs.venv_python_path(root / ".docs-venv")
                python.parent.mkdir(parents=True)
                python.touch()
                output = io.StringIO()
                with patch.object(build_docs.shutil, "which", return_value="/tools/uv"), patch.object(build_docs, "invoke_checked", side_effect=error) as invoke, contextlib.redirect_stdout(output):
                    self.assertEqual(build_docs.main(["--root", directory, "--installer", installer]), 1)
                    self.assertEqual(invoke.call_count, 1)
                self.assertIn(str(error), output.getvalue())

    def test_child_exit_names_the_failure(self) -> None:
        with patch.object(build_docs.subprocess, "run", return_value=subprocess.CompletedProcess(["tool"], 7)):
            with self.assertRaisesRegex(RuntimeError, "strict build failed with exit code 7"):
                build_docs.invoke_checked(Path("tool"), [], "strict build")


if __name__ == "__main__":
    unittest.main()
