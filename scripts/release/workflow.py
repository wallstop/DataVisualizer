#!/usr/bin/env python3
"""Run the release workflow's orchestration through typed Python and uv."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
from typing import Any, Dict, Mapping, Optional, Sequence, cast

sys.dont_write_bytecode = True


def emit(message: str) -> None:
    sys.stdout.buffer.write((message.rstrip('\n') + '\n').encode('utf-8'))


def named_values(output: str, keys: Sequence[str]) -> Dict[str, str]:
    fields = dict(line.split(': ', 1) for line in output.splitlines() if ': ' in line)
    if any(not fields.get(key) for key in keys):
        raise ValueError('The release helper did not report: ' + ', '.join(keys))
    return {key: fields[key] for key in keys}


class ReleaseWorkflow:
    def __init__(self, root: Path, environment: Mapping[str, str]) -> None:
        self.root = root
        self.environment = dict(environment)
        self.tools = Path(__file__).resolve().parent

    def value(self, key: str) -> str:
        return self.environment.get(key, '')

    def run(self, command: Sequence[str], capture: bool = False, check: bool = True) -> subprocess.CompletedProcess[str]:
        result = subprocess.run([shutil.which(command[0]) or command[0], *command[1:]], cwd=self.root, env=self.environment, capture_output=capture,
                                text=True, encoding='utf-8', errors='replace', check=False)
        if check and result.returncode:
            raise ValueError(f"{command[0]} failed ({result.returncode}): {(result.stderr or '').strip()}")
        return result

    def python(self, name: str, *arguments: str, capture: bool = False) -> subprocess.CompletedProcess[str]:
        return self.run([sys.executable, str(self.tools / name), '--root', str(self.root), *arguments], capture=capture)

    def outputs(self, fields: Mapping[str, str]) -> None:
        if any('\n' in value or '\r' in value for value in fields.values()):
            raise ValueError('Release outputs must contain one line per value')
        path = self.value('GITHUB_OUTPUT')
        if not path:
            raise ValueError('GITHUB_OUTPUT is required')
        with Path(path).open('a', encoding='utf-8', newline='\n') as stream:
            stream.write(''.join(f'{key}={value}\n' for key, value in fields.items()))

    def package(self) -> Dict[str, Any]:
        value: object = json.loads((self.root / 'package.json').read_text(encoding='utf-8'))
        if not isinstance(value, dict):
            raise ValueError('package.json must contain an object')
        return cast(Dict[str, Any], value)

    def require_main(self) -> None:
        if self.value('EVENT') == 'workflow_dispatch' and self.value('REF') != 'refs/heads/main':
            raise ValueError('Only prepare releases from main.')

    def prepare(self) -> None:
        arguments = ['--version', self.value('EXPLICIT_VERSION')] if self.value('EXPLICIT_VERSION') else ['--bump', self.value('BUMP')]
        output = self.python('prepare_release.py', *arguments, capture=True).stdout
        emit(output)
        fields = named_values(output, ('previous-version', 'next-version'))
        version = fields['next-version']
        self.outputs({'previous_version': fields['previous-version'], 'version': version, 'branch': f'release/v{version}'})

    def validate_tree(self) -> None:
        self.run([sys.executable, str(self.root / 'scripts/lint_llm_instructions.py')])
        self.run(['npm', 'pack', '--dry-run'])
        with tempfile.TemporaryDirectory(prefix='release-candidate.', dir=self.value('RUNNER_TEMP') or None) as directory:
            package = self.package()
            self.python('build_unitypackage.py', '--output', directory)
            archive = Path(directory) / f"{package['name']}-{package['version']}.unitypackage"
            self.python('validate_unitypackage.py', '--archive', str(archive))
            self.python('extract_release_notes.py', '--version', str(package['version']), '--output', str(Path(directory) / 'release-notes.md'))

    def git_identity(self) -> None:
        self.run(['git', 'config', 'user.name', 'github-actions[bot]'])
        self.run(['git', 'config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com'])

    def commit_push(self) -> None:
        branch = self.value('BRANCH')
        self.git_identity()
        self.run(['git', 'switch', '-c', branch])
        self.run(['git', 'add', 'package.json', 'CHANGELOG.md', '.llm/context.md'])
        self.run(['git', 'commit', '-m', f"Bump version from {self.value('PREVIOUS_VERSION')} to {self.value('VERSION')}"])
        if self.run(['git', 'ls-remote', '--exit-code', '--heads', 'origin', 'refs/heads/' + branch], capture=True, check=False).returncode == 0:
            self.run(['git', 'fetch', 'origin', 'refs/heads/' + branch])
            if self.run(['git', 'diff', '--quiet', 'HEAD', 'FETCH_HEAD'], check=False).returncode:
                raise ValueError(f'{branch} already exists with different content. Inspect it before preparing another release.')
            self.run(['git', 'switch', '--detach', 'FETCH_HEAD'])
            emit(f'::notice::Reusing the existing release branch {branch}.')
        else:
            self.run(['git', 'push', 'origin', branch])
        self.outputs({'head': self.run(['git', 'rev-parse', 'HEAD'], capture=True).stdout.strip()})

    def open_pr(self) -> None:
        branch = self.value('BRANCH')
        result = self.run(['gh', 'pr', 'view', branch, '--json', 'state', '--jq', '.state'], capture=True, check=False)
        if result.returncode == 0 and result.stdout.strip() == 'OPEN':
            emit('::notice::The release pull request is already open.')
            return
        version = self.value('VERSION')
        body = Path(self.value('RUNNER_TEMP')) / 'release-pr-body.md'
        body.write_text(f'DISCLOSURE: LLM-GENERATED TEXT\n\n**Why:** Release v{version} publishes the prepared package changes.\n\n'
                        f'**What:**\n\n- Bump {self.value("PREVIOUS_VERSION")} to {version} and date the release notes.\n'
                        '- Publish npm and GitHub artifacts after automated validation and merge.\n', encoding='utf-8')
        self.run(['gh', 'pr', 'create', '--base', 'main', '--head', branch, '--title', f'Release v{version}', '--body-file', str(body)])

    def dry_summary(self) -> None:
        emit(f"Dry run: would prepare release v{self.value('VERSION')} (branch {self.value('BRANCH')}); candidate validated on the runner; no branch pushed and no PR opened.")

    def retain_tools(self) -> None:
        shutil.copytree(self.tools, Path(self.value('RUNNER_TEMP')) / 'release-tools', dirs_exist_ok=True,
                        ignore=shutil.ignore_patterns('__pycache__'))

    def resolve(self) -> None:
        branch = ''
        create_tag = False
        if self.value('PREPARED_SHA'):
            branch = self.value('PREPARED_BRANCH')
            tag = branch.removeprefix('release/')
            checkout = self.value('PREPARED_SHA')
            create_tag = True
        elif self.value('EVENT') == 'pull_request':
            branch = self.value('HEAD_REF')
            tag = branch.removeprefix('release/')
            checkout = self.value('MERGE_SHA')
            create_tag = True
            if not checkout:
                raise ValueError('The merged release pull request has no merge SHA')
        elif self.value('EVENT') == 'push':
            tag = self.value('REF').removeprefix('refs/tags/')
            checkout = 'refs/tags/' + tag
        else:
            if self.value('REF') != 'refs/heads/main':
                raise ValueError('Only publish releases from main.')
            tag = self.value('INPUT_TAG') or 'v' + str(self.package()['version'])
            checkout = 'refs/tags/' + tag
        if not re.match(r'^v[0-9]', tag):
            raise ValueError('Publication requires an existing vX.Y.Z tag or a merged release/vX.Y.Z PR.')
        self.run(['git', 'check-ref-format', 'refs/tags/' + tag])
        self.outputs({'tag': tag, 'branch': branch, 'checkout_ref': checkout, 'create_tag': str(create_tag).lower()})

    def tag(self) -> None:
        tag = self.value('TAG')
        self.git_identity()
        if self.run(['git', 'rev-parse', '-q', '--verify', 'refs/tags/' + tag], capture=True, check=False).returncode == 0:
            existing = self.run(['git', 'rev-parse', f'refs/tags/{tag}^{{commit}}'], capture=True).stdout.strip()
            head = self.run(['git', 'rev-parse', 'HEAD'], capture=True).stdout.strip()
            if existing != head:
                raise ValueError(f'Existing {tag} points at a different commit.')
        else:
            self.python('tag_release.py', '--branch', self.value('BRANCH'))
        self.run(['git', 'push', 'origin', 'refs/tags/' + tag])

    def verify(self) -> None:
        output = self.python('verify_release.py', '--tag', self.value('TAG'), capture=True).stdout
        emit(output)
        self.outputs(named_values(output, ('pkg', 'version', 'npm_tag', 'package_file', 'entries')))

    def toolchain(self) -> None:
        self.run(['node', '--version'])
        version = self.run(['npm', '--version'], capture=True).stdout.strip()
        emit(version)
        match = re.match(r'^(\d+)\.(\d+)\.(\d+)', version)
        if match is None or tuple(int(part) for part in match.groups()) < (11, 5, 1):
            raise ValueError(f'npm {version} is too old; trusted publishing requires npm >= 11.5.1.')

    def build(self) -> None:
        self.python('build_unitypackage.py', '--output', 'dist')
        emit('\n'.join(sorted(path.name for path in (self.root / 'dist').iterdir())))

    def validate_archive(self) -> None:
        archive = next(iter(sorted((self.root / 'dist').glob('*.unitypackage'))), None)
        if archive is None:
            raise ValueError('The builder produced no .unitypackage.')
        self.python('validate_unitypackage.py', '--archive', str(archive))

    def artifacts(self) -> None:
        self.python('extract_release_notes.py', '--version', self.value('VERSION'), '--output', 'dist/release-notes.md')
        name = self.value('PACKAGE_FILE')
        package = self.root / name
        digest = hashlib.sha256()
        with package.open('rb') as stream:
            while True:
                chunk = stream.read(1024 * 1024)
                if not chunk:
                    break
                digest.update(chunk)
        checksum = f'{digest.hexdigest()}  {name}\n'
        with Path(str(package) + '.sha256').open('w', encoding='utf-8', newline='\n') as stream:
            stream.write(checksum)
        emit(checksum)

    def publish_npm(self) -> None:
        identity = self.value('PKG') + '@' + self.value('VER')
        if self.run(['npm', 'view', identity, 'version'], capture=True, check=False).returncode == 0:
            emit(f'::notice::{identity} is already on the registry; skipping publish.')
        else:
            self.run(['npm', 'publish', self.value('PACKAGE_FILE'), '--access', 'public', '--tag', self.value('NPM_TAG')])
            emit(f"::notice::Published {identity} to dist-tag '{self.value('NPM_TAG')}'.")

    def publish_github(self) -> None:
        tag = self.value('TAG')
        prerelease = ['--prerelease'] if self.value('NPM_TAG') == 'next' else []
        archives = sorted((self.root / 'dist').glob('*.unitypackage'))
        checksums = sorted((self.root / 'dist').glob('*.unitypackage.sha256'))
        if not archives or not checksums:
            raise ValueError('Validated Unity archives and checksums are required before publication')
        artifacts = [self.value('PACKAGE_FILE'), self.value('PACKAGE_FILE') + '.sha256',
                     *(str(path.relative_to(self.root)) for path in [*archives, *checksums])]
        common = ['--title', tag, '--notes-file', 'dist/release-notes.md', *prerelease]
        if self.run(['gh', 'release', 'view', tag], capture=True, check=False).returncode == 0:
            self.run(['gh', 'release', 'edit', tag, *common])
            self.run(['gh', 'release', 'upload', tag, '--clobber', *artifacts])
            emit(f'::notice::Updated GitHub Release {tag}.')
        else:
            self.run(['gh', 'release', 'create', tag, '--verify-tag', *common, *artifacts])
            emit(f'::notice::Created GitHub Release {tag}.')


def main(arguments: Optional[Sequence[str]] = None) -> int:
    runner = ReleaseWorkflow(Path.cwd(), os.environ)
    actions = {name.replace('_', '-'): method for name, method in (
        ('require_main', runner.require_main), ('prepare', runner.prepare), ('validate_tree', runner.validate_tree),
        ('commit_push', runner.commit_push), ('open_pr', runner.open_pr), ('dry_summary', runner.dry_summary),
        ('retain_tools', runner.retain_tools), ('resolve', runner.resolve), ('tag', runner.tag), ('verify', runner.verify),
        ('toolchain', runner.toolchain), ('build', runner.build), ('validate_archive', runner.validate_archive),
        ('artifacts', runner.artifacts), ('publish_npm', runner.publish_npm), ('publish_github', runner.publish_github),
    )}
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=tuple(actions))
    options = parser.parse_args(arguments)
    try:
        actions[options.action]()
        return 0
    except (OSError, ValueError, KeyError) as error:
        sys.stderr.buffer.write(f'::error::{error}\n'.encode('utf-8', errors='replace'))
        return 1


if __name__ == '__main__':
    sys.exit(main())
