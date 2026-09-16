#!/usr/bin/env node
/**
 * Release tagging for com.wallstop-studios.data-visualizer.
 *
 * Verifies that a merged `release/vX.Y.Z` tree is internally consistent and creates
 * exactly one annotated `vX.Y.Z` tag. The `.github/workflows/release-tag.yml`
 * workflow runs this script on the merged release pull request (or on a manual
 * dispatch) and pushes the tag so the tag event can trigger the downstream publish
 * workflow.
 *
 * Usage:
 *   node scripts/release/tag-release.mjs --branch release/vX.Y.Z [--commit <sha>] [--root <path>] [--dry-run]
 *
 * Fail-closed guards:
 *   - The branch must be exactly `release/v<package.json version>` (supported semver
 *     with the restricted prerelease suffixes used by prepare-release.mjs).
 *   - CHANGELOG.md must carry exactly one `## [<version>] - YYYY-MM-DD` section and
 *     that section must have release notes.
 *   - The tag must not already exist; an existing tag is never retargeted.
 *   - The working tree must be clean, so the tag always points at committed content.
 *   - `--commit` must resolve to an existing commit.
 */

import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '..', '..');

const versionPattern =
    /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(rc|alpha|beta|preview)(?:\.(0|[1-9]\d*))?)?$/;
const sectionHeadingPattern = /^## /;
const sectionDatePattern = /^\d{4}-\d{2}-\d{2}\s*$/;

const usage = [
    'Tag a merged release: verify the release tree and create one annotated vX.Y.Z tag.',
    '',
    'Options:',
    '  --branch <release/vX.Y.Z>  Release branch that prepared this version (required).',
    '  --commit <sha>             Commit to tag (default: HEAD).',
    '  --root <path>              Repository root to operate on (default: this repository).',
    '  --dry-run                  Verify the tree without creating any tag.',
    '  --help                     Show this help.',
].join('\n');

function parseArguments(argv) {
    const parsed = { branch: undefined, commit: undefined, root: undefined, dryRun: false };
    for (let index = 0; index < argv.length; index++) {
        const argument = argv[index];
        switch (argument) {
            case '--branch':
                parsed.branch = readValue(argv, ++index, argument);
                break;
            case '--commit':
                parsed.commit = readValue(argv, ++index, argument);
                break;
            case '--root':
                parsed.root = readValue(argv, ++index, argument);
                break;
            case '--dry-run':
                parsed.dryRun = true;
                break;
            case '--help':
                console.log(usage);
                process.exit(0);
                break;
            default:
                throw new Error(`Unknown argument '${argument}'.\n\n${usage}`);
        }
    }
    if (parsed.branch === undefined) {
        throw new Error('--branch <release/vX.Y.Z> is required.\n\n' + usage);
    }
    return parsed;
}

function readValue(argv, index, flag) {
    if (index >= argv.length) {
        throw new Error(`Missing value for ${flag}.`);
    }
    return argv[index];
}

function runGit(root, args) {
    const result = spawnSync('git', args, { cwd: root, encoding: 'utf8' });
    if (result.error) {
        throw new Error(`Cannot run git: ${result.error.message}`);
    }
    return result;
}

function gitOutput(root, args, purpose) {
    const result = runGit(root, args);
    if (result.status !== 0) {
        const detail = (result.stderr || result.stdout || '').trim();
        throw new Error(`Cannot ${purpose}: ${detail || `git exited with ${result.status}`}`);
    }
    return (result.stdout || '').trim();
}

function readPackageVersion(root) {
    const packagePath = join(root, 'package.json');
    let packageRaw;
    try {
        packageRaw = readFileSync(packagePath, 'utf8');
    } catch {
        throw new Error(`Cannot read required release file '${packagePath}'.`);
    }
    let packageJson;
    try {
        packageJson = JSON.parse(packageRaw);
    } catch (error) {
        throw new Error(`'package.json' is not valid JSON: ${error.message}`);
    }
    const version = packageJson.version;
    if (typeof version !== 'string' || version.trim() === '') {
        throw new Error("'package.json' is missing a version field.");
    }
    if (!versionPattern.test(version)) {
        throw new Error(
            `'package.json' version '${version}' is not supported semver. Use bare x.y.z ` +
                'with an optional -rc/-alpha/-beta/-preview (optionally .N) prerelease suffix.',
        );
    }
    return version;
}

function verifyBranch(parsed, version) {
    const expectedBranch = `release/v${version}`;
    if (parsed.branch !== expectedBranch) {
        throw new Error(
            `Branch '${parsed.branch}' does not match the release branch for version ` +
                `'${version}' (expected '${expectedBranch}'). Only a merged ` +
                '`release/vX.Y.Z` pull request can be tagged.',
        );
    }
    return expectedBranch;
}

function verifyChangelogSection(root, version) {
    const changelogPath = join(root, 'CHANGELOG.md');
    let changelog;
    try {
        changelog = readFileSync(changelogPath, 'utf8');
    } catch {
        throw new Error(`Cannot read required release file '${changelogPath}'.`);
    }
    const lines = changelog.split(/\r?\n/);
    const headingPrefix = `## [${version}] - `;
    const sectionIndexes = [];
    for (let index = 0; index < lines.length; index++) {
        const line = lines[index];
        if (!line.startsWith(headingPrefix)) {
            continue;
        }
        if (!sectionDatePattern.test(line.slice(headingPrefix.length).trim())) {
            continue;
        }
        sectionIndexes.push(index);
    }
    if (sectionIndexes.length === 0) {
        throw new Error(
            `CHANGELOG.md carries no '## [${version}] - YYYY-MM-DD' section for version ` +
                `'${version}'. The merged changelog section must match the package version exactly.`,
        );
    }
    if (sectionIndexes.length > 1) {
        throw new Error(
            `CHANGELOG.md carries ${sectionIndexes.length} '## [${version}] - ...' sections; ` +
                'exactly one is required.',
        );
    }
    const sectionIndex = sectionIndexes[0];
    let sectionEnd = sectionIndex + 1;
    while (sectionEnd < lines.length && !sectionHeadingPattern.test(lines[sectionEnd])) {
        sectionEnd++;
    }
    const hasNotes = lines
        .slice(sectionIndex + 1, sectionEnd)
        .some((line) => line.trim() !== '');
    if (!hasNotes) {
        throw new Error(
            `The '## [${version}] - ...' section has no release notes; a release tag requires notes.`,
        );
    }
}

function resolveCommit(root, commitArgument) {
    if (commitArgument !== undefined) {
        return gitOutput(root, ['rev-parse', '--verify', `${commitArgument}^{commit}`], `resolve '${commitArgument}'`);
    }
    return gitOutput(root, ['rev-parse', 'HEAD'], 'resolve HEAD');
}

function verifyGitState(root, tag) {
    const inside = runGit(root, ['rev-parse', '--is-inside-work-tree']);
    if (inside.status !== 0 || inside.stdout.trim() !== 'true') {
        throw new Error(`'${root}' is not a git work tree.`);
    }
    const status = runGit(root, ['status', '--porcelain']);
    if (status.status !== 0) {
        const detail = (status.stderr || '').trim();
        throw new Error(`Cannot read git status: ${detail || `git exited with ${status.status}`}`);
    }
    if (status.stdout.trim() !== '') {
        throw new Error(
            'The working tree is dirty; a release tag must point at committed content only.\n' +
                status.stdout.trim(),
        );
    }
    const existing = runGit(root, ['rev-parse', '-q', '--verify', `refs/tags/${tag}`]);
    if (existing.status === 0) {
        throw new Error(
            `Tag '${tag}' already exists; an existing release tag is never retargeted. ` +
                'Cut a new version or delete the stale tag deliberately.',
        );
    }
}

function createTag(root, tag, commit, dryRun) {
    const args = ['tag', '-a', tag, '-m', `Release ${tag}`];
    if (commit !== undefined) {
        args.push(commit);
    }
    if (dryRun) {
        console.log(`dry-run: would create annotated tag ${tag} at ${commit ?? 'HEAD'}`);
        return;
    }
    gitOutput(root, args, `create tag '${tag}'`);
    console.log(`Created annotated tag ${tag} at ${commit}.`);
}

function main() {
    const parsed = parseArguments(process.argv.slice(2));
    const root = parsed.root !== undefined ? resolve(parsed.root) : repositoryRoot;

    const version = readPackageVersion(root);
    verifyBranch(parsed, version);
    verifyChangelogSection(root, version);

    const tag = `v${version}`;
    verifyGitState(root, tag);
    const commit = resolveCommit(root, parsed.commit);

    createTag(root, tag, commit, parsed.dryRun);
    console.log(`version: ${version}`);
    console.log(`tag: ${tag}`);
    console.log(`commit: ${commit}`);
}

try {
    main();
} catch (error) {
    console.error(`error: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
}
