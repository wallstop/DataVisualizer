#!/usr/bin/env python3
"""Exercise release orchestration without any real remote writes or publication."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from release import workflow


class WorkflowTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory(prefix='release workflow 日本 ')
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        (self.root / 'package.json').write_text(json.dumps({'name': 'fixture', 'version': '1.2.3'}), encoding='utf-8')
        self.output = self.root / 'outputs'
        self.driver = workflow.ReleaseWorkflow(self.root, {**os.environ, 'RUNNER_TEMP': str(self.root),
            'GITHUB_OUTPUT': str(self.output), 'BRANCH': 'release/v1.2.4', 'VERSION': '1.2.4', 'PREVIOUS_VERSION': '1.2.3'})
        self.addCleanup(patch.stopall)
        patch.object(workflow, 'emit').start()

    def result(self, text: str = '', code: int = 0) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(['fixture'], code, text, '')

    def test_commands_capture_utf8_and_fail_closed(self) -> None:
        result = self.driver.run([sys.executable, '-c', "print('release 日本')"], capture=True)
        self.assertEqual(result.stdout.strip(), 'release 日本')
        with self.assertRaisesRegex(ValueError, r'failed \(3\): fixture failure'):
            self.driver.run([sys.executable, '-c', "import sys; print('fixture failure', file=sys.stderr); sys.exit(3)"], capture=True)
        self.assertEqual(self.driver.run([sys.executable, '-c', 'raise SystemExit(4)'], check=False).returncode, 4)

    def test_preparation_validates_all_outputs_before_publishing_them(self) -> None:
        for explicit in ('', '2.0.0'):
            with self.subTest(explicit=explicit), patch.object(self.driver, 'python', return_value=self.result('previous-version: 1.2.3\nnext-version: 2.0.0\n')) as helper:
                self.driver.environment.update(EXPLICIT_VERSION=explicit, BUMP='major')
                self.driver.prepare()
                self.assertEqual(helper.call_args.args, ('prepare_release.py', '--version' if explicit else '--bump', explicit or 'major'))
                self.assertIn('branch=release/v2.0.0\n', self.output.read_text())
        self.output.unlink()
        with patch.object(self.driver, 'python', return_value=self.result('previous-version: 1.2.3\n')), self.assertRaises(ValueError):
            self.driver.prepare()
        self.assertFalse(self.output.exists())
        with self.assertRaises(ValueError):
            self.driver.outputs({'tag': 'v1.2.3\ncreate_tag=true'})

    def test_validation_precedes_remote_writes_and_stops_on_failure(self) -> None:
        calls: list[str] = []
        def run_command(command: list[str]) -> None:
            calls.append(command[1])
        def run_helper(name: str, *args: str) -> None:
            calls.append(name)
        with patch.object(self.driver, 'run', side_effect=run_command):
            with patch.object(self.driver, 'python', side_effect=run_helper):
                self.driver.validate_tree()
        self.assertEqual(calls, [str(self.root / 'scripts/lint_llm_instructions.py'), 'pack', 'build_unitypackage.py',
                                 'validate_unitypackage.py', 'extract_release_notes.py'])
        with patch.object(self.driver, 'run'), patch.object(self.driver, 'python', side_effect=ValueError('bad archive')) as helper:
            with self.assertRaises(ValueError):
                self.driver.validate_tree()
            self.assertEqual(helper.call_count, 1)

    def test_branch_reruns_reuse_only_identical_remote_content(self) -> None:
        for exists, different in ((False, False), (True, False), (True, True)):
            with self.subTest(exists=exists, different=different):
                def invoke(command: list[str], **kwargs: object) -> subprocess.CompletedProcess[str]:
                    if command[1] == 'ls-remote':
                        return self.result(code=0 if exists else 2)
                    if command[1] == 'diff':
                        return self.result(code=int(different))
                    return self.result('candidate-sha')
                with patch.object(self.driver, 'run', side_effect=invoke) as run:
                    if different:
                        with self.assertRaises(ValueError):
                            self.driver.commit_push()
                    else:
                        self.driver.commit_push()
                    commands = [call.args[0] for call in run.call_args_list]
                    self.assertEqual(any(command[1] == 'push' for command in commands), not exists)
                    self.assertEqual(['git', 'switch', '--detach', 'FETCH_HEAD'] in commands, exists and not different)

    def test_pr_reruns_preserve_open_prs_and_use_body_files(self) -> None:
        for state in ('OPEN', 'CLOSED'):
            with self.subTest(state=state), patch.object(self.driver, 'run', return_value=self.result(state)) as run:
                self.driver.open_pr()
                self.assertEqual(run.call_count, 1 if state == 'OPEN' else 2)
                if state == 'CLOSED':
                    command = run.call_args.args[0]
                    body = Path(command[command.index('--body-file') + 1]).read_text()
                    self.assertTrue(body.startswith('DISCLOSURE: LLM-GENERATED TEXT\n'))
                    self.assertIn('1.2.3 to 1.2.4', body)

    def test_source_guards_reject_non_main_missing_merge_and_invalid_tags(self) -> None:
        for event, ref, head, merge, tag in (('workflow_dispatch', 'refs/heads/topic', '', '', ''),
                ('pull_request', 'refs/heads/main', 'release/v1.2.3', '', ''),
                ('push', 'refs/tags/invalid', '', '', ''), ('workflow_dispatch', 'refs/heads/main', '', '', 'v1.2.3\ninjected')):
            with self.subTest(event=event, ref=ref, tag=tag):
                self.driver.environment.update(EVENT=event, REF=ref, HEAD_REF=head, MERGE_SHA=merge, INPUT_TAG=tag)
                with self.assertRaises(ValueError):
                    self.driver.resolve()
        self.driver.environment.update(EVENT='workflow_dispatch', REF='refs/heads/topic')
        with self.assertRaises(ValueError):
            self.driver.require_main()

    def test_tag_reruns_never_move_existing_tags(self) -> None:
        self.driver.environment.update(TAG='v1.2.4')
        for exists, same in ((False, True), (True, True), (True, False)):
            def invoke(command: list[str], **kwargs: object) -> subprocess.CompletedProcess[str]:
                if '-q' in command:
                    return self.result(code=0 if exists else 1)
                return self.result('head' if command[-1] == 'HEAD' or same else 'other')
            with self.subTest(exists=exists, same=same), patch.object(self.driver, 'run', side_effect=invoke) as run, patch.object(self.driver, 'python') as helper:
                if same:
                    self.driver.tag()
                else:
                    with self.assertRaises(ValueError):
                        self.driver.tag()
                self.assertEqual(helper.call_count, int(not exists))
                self.assertEqual(any(call.args[0][1] == 'push' for call in run.call_args_list), same)

    def test_checksums_and_toolchain_requirements(self) -> None:
        self.driver.environment.update(VERSION='1.2.3', PACKAGE_FILE='fixture.tgz')
        (self.root / 'fixture.tgz').write_bytes(b'payload')
        with patch.object(self.driver, 'python'):
            self.driver.artifacts()
        self.assertEqual((self.root / 'fixture.tgz.sha256').read_text(), hashlib.sha256(b'payload').hexdigest() + '  fixture.tgz\n')
        for version, valid in (('11.5.0', False), ('11.5.1', True), ('11.10.0', True), ('12.0.0', True), ('invalid', False)):
            with self.subTest(version=version), patch.object(self.driver, 'run', return_value=self.result(version)):
                if valid:
                    self.driver.toolchain()
                else:
                    with self.assertRaises(ValueError):
                        self.driver.toolchain()
        with self.assertRaises(ValueError):
            self.driver.validate_archive()

    def test_publication_reruns_skip_npm_and_update_github(self) -> None:
        dist = self.root / 'dist'
        dist.mkdir()
        for name in ('fixture.unitypackage', 'fixture.unitypackage.sha256'):
            (dist / name).write_text('fixture')
        self.driver.environment.update(PKG='fixture', VER='1.2.3', NPM_TAG='next', PACKAGE_FILE='fixture.tgz', TAG='v1.2.3')
        for exists in (True, False):
            with self.subTest(exists=exists), patch.object(self.driver, 'run', return_value=self.result(code=0 if exists else 1)) as run:
                self.driver.publish_npm()
                self.assertEqual(run.call_count, 1 if exists else 2)
                if not exists:
                    self.assertEqual(run.call_args.args[0], ['npm', 'publish', 'fixture.tgz', '--access', 'public', '--tag', 'next'])
                run.reset_mock()
                self.driver.publish_github()
                commands = [call.args[0] for call in run.call_args_list]
                self.assertEqual(commands[1][2], 'edit' if exists else 'create')
                self.assertIn('--prerelease', commands[1])
                self.assertIn('dist/fixture.unitypackage.sha256', commands[-1])
                self.assertIn('--clobber' if exists else '--verify-tag', commands[-1])

    def test_retained_tools_survive_switching_to_an_old_source(self) -> None:
        self.driver.retain_tools()
        retained = self.root / 'release-tools'
        self.assertEqual((retained / 'workflow.py').read_bytes(), Path(workflow.__file__).read_bytes())
        self.assertTrue((retained / 'repository_identity.py').is_file())
        self.assertFalse((retained / '__pycache__').exists())


if __name__ == '__main__':
    unittest.main()
