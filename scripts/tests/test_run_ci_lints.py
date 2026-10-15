#!/usr/bin/env python3
"""Verify the CI ladder keeps each check, its order and its failure boundary."""
from __future__ import annotations

import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import call, patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from run_ci_lints import COMMANDS, main, run_ladder, run_script

EXPECTED = (
    ('lint_llm_instructions.py', ('--verbose-output',)),
    ('lint_csharp_member_order.py', ('--verbose',)),
    ('lint_csharp_null_assertions.py', ('--verbose',)),
    ('lint_csharp_usings.py', ('--verbose',)),
    ('lint_release_secrets.py', ('--verbose',)),
    ('lint_line_endings.py', ('--verbose',)),
    ('lint_changelog_length.py', ('--verbose',)),
    ('lint_assembly_warnings.py', ()),
    ('lint_file_lengths.py', ('--verbose-output',)),
    ('generate_skills_index.py', ()),
)


class CiLadderTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        (self.root / 'scripts').mkdir()

    def test_ladder_preserves_commands_and_arguments(self) -> None:
        self.assertEqual(COMMANDS, EXPECTED)
        with patch('run_ci_lints.run_script', return_value=0) as runner, patch('run_ci_lints.subprocess.check_output', return_value='') as git:
            self.assertEqual(run_ladder(self.root), 0)
            self.assertEqual(runner.call_args_list, [call(self.root / 'scripts' / name, arguments) for name, arguments in EXPECTED])
            self.assertEqual(git.call_args.args[0], ['git', 'status', '--porcelain', '--', '.llm/skills/index.md'])

    def test_each_failure_stops_later_checks_and_index_drift_probe(self) -> None:
        original_directory = Path.cwd()
        for index in range(len(EXPECTED)):
            with self.subTest(index=index), patch('run_ci_lints.run_script', side_effect=[0] * index + [3]) as runner, patch('run_ci_lints.subprocess.check_output') as git:
                self.assertEqual(run_ladder(self.root), 3)
                self.assertEqual(runner.call_count, index + 1)
                git.assert_not_called()
                self.assertEqual(Path.cwd(), original_directory)

    def test_staged_untracked_and_worktree_index_drift_fail(self) -> None:
        for status in (' M .llm/skills/index.md', 'M  .llm/skills/index.md', '?? .llm/skills/index.md'):
            with self.subTest(status=status), patch('run_ci_lints.run_script', return_value=0), patch('run_ci_lints.subprocess.check_output', return_value=status):
                output = io.StringIO()
                with contextlib.redirect_stdout(output):
                    self.assertEqual(run_ladder(self.root), 1)
                self.assertIn('::error::.llm/skills/index.md is stale', output.getvalue())
                self.assertIn(status.strip(), output.getvalue())

    def test_script_gets_its_own_arguments_and_restores_driver_state(self) -> None:
        script = self.root / 'scripts' / 'probe.py'
        script.write_text('import sys\nassert sys.argv[1:] == ["--verbose"]\nprint("original diagnostic")\nraise SystemExit(7)\n', encoding='utf-8')
        previous_argv = sys.argv
        previous_path = list(sys.path)
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            self.assertEqual(run_script(script, ['--verbose']), 7)
        self.assertIs(sys.argv, previous_argv)
        self.assertEqual(sys.path, previous_path)
        self.assertEqual(output.getvalue(), 'original diagnostic\n')
        script.write_text('raise RuntimeError("unexpected")\n', encoding='utf-8')
        with self.assertRaises(RuntimeError):
            run_script(script, [])
        self.assertIs(sys.argv, previous_argv)
        self.assertEqual(sys.path, previous_path)

    def test_script_exit_semantics_and_unexpected_options(self) -> None:
        script = self.root / 'scripts' / 'exit.py'
        for statement, expected in [('pass', 0), ('raise SystemExit()', 0), ('raise SystemExit("failure")', 1)]:
            script.write_text(statement, encoding='utf-8')
            with contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(run_script(script, []), expected)
        with patch('run_ci_lints.run_ladder') as runner, contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(main(['--skip']), 2)
            runner.assert_not_called()

    def test_workflow_invokes_the_driver(self) -> None:
        root = Path(__file__).resolve().parents[2]
        workflow = (root / '.github/workflows/llm-lint.yml').read_text(encoding='utf-8')
        block = workflow.split('- name: Run the repository lints', 1)[1].split('- name:', 1)[0]
        self.assertIn('uv run --no-project --python ">=3.9" scripts/run_ci_lints.py', block)
        self.assertNotIn('scripts/lint_', block)
        self.assertIn('uses: astral-sh/setup-uv@', workflow)
        self.assertIn('uv run --no-project --python ">=3.9" scripts/should_run_self_tests.py --github-actions', workflow)
        self.assertIn('uv run --no-project --python ">=3.9" scripts/tests/run_all.py --names', workflow)
        self.assertNotIn('shell: bash', workflow)
        self.assertEqual(workflow.count('      - "pyproject.toml"'), 2)


if __name__ == '__main__':
    unittest.main()
