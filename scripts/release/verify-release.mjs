#!/usr/bin/env node
/**
 * Release payload verification for com.wallstop-studios.data-visualizer.
 *
 * Verifies that a `vX.Y.Z` tag matches the package version and that the npm
 * package payload matches the declared allowlist, fail-closed, before the
 * tag-driven publish workflow performs any irreversible publication.
 * The `.github/workflows/npm-publish.yml` workflow runs this script on the
 * exact release tag (or on a manual dispatch rerun).
 *
 * Usage:
 *   node scripts/release/verify-release.mjs --tag vX.Y.Z [--root <path>] [--package-file <path>]
 *
 * Fail-closed guards:
 *   - `--tag` must be exactly `v<package.json version>` (supported semver with
 *     the restricted prerelease suffixes used by the release scripts).
 *   - `package.json` must exist, be valid JSON, and carry a name, a version,
 *     and a non-empty `files` array.
 *   - The tree must be a git work tree; the expected payload is derived from
 *     tracked files only, so the verification always sees committed content.
 *   - Every packed file must be allowlisted (npm auto-included root files or
 *     covered by a `files` entry), and every allowlisted tracked file must be
 *     packed. Extra (junk) and missing (shrunk payload) entries both fail.
 *
 * Reports the package version, the npm dist-tag (`next` for restricted
 * prerelease suffixes, `latest` otherwise), the packed tarball filename, and
 * the packed entry count.
 *
 * The git/package/allowlist payload resolution is shared with
 * `build-unitypackage.mjs` via `package-payload.mjs`.
 */

import { spawnSync } from 'node:child_process';
import { basename, dirname, isAbsolute, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { existsSync } from 'node:fs';
import {
    listExpectedPayloadPaths,
    readPackageJson,
    verifyGitWorkTree,
    versionPattern,
} from './package-payload.mjs';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '..', '..');

const usage = [
    'Verify a release tag and its npm package payload before publishing.',
    '',
    'Options:',
    '  --tag <vX.Y.Z>        Release tag to verify, e.g. v0.0.38 (required).',
    '  --package-file <path> Verify this tarball instead of packing the tree.',
    '  --root <path>         Repository root to operate on (default: this repository).',
    '  --help                Show this help.',
].join('\n');

function parseArguments(argv) {
    const parsed = { tag: undefined, packageFile: undefined, root: undefined };
    for (let index = 0; index < argv.length; index++) {
        const argument = argv[index];
        switch (argument) {
            case '--tag':
                parsed.tag = readValue(argv, ++index, argument);
                break;
            case '--package-file':
                parsed.packageFile = readValue(argv, ++index, argument);
                break;
            case '--root':
                parsed.root = readValue(argv, ++index, argument);
                break;
            case '--help':
                console.log(usage);
                process.exit(0);
                break;
            default:
                throw new Error(`Unknown argument '${argument}'.\n\n${usage}`);
        }
    }
    if (parsed.tag === undefined) {
        throw new Error('--tag <vX.Y.Z> is required.\n\n' + usage);
    }
    return parsed;
}

function readValue(argv, index, flag) {
    if (index >= argv.length) {
        throw new Error(`Missing value for ${flag}.`);
    }
    return argv[index];
}

function verifyTag(tag, packageVersion) {
    const expectedTag = `v${packageVersion}`;
    if (tag !== expectedTag) {
        throw new Error(
            `Tag '${tag}' does not match the package version '${packageVersion}' (expected ` +
                `'${expectedTag}'). Only the exact release tag for the checked-out package can publish.`,
        );
    }
    return expectedTag;
}

function runNpm(args, root, purpose) {
    // npm is a .cmd shim on Windows; spawning it without a shell fails there.
    const result = spawnSync('npm', args, {
        cwd: root,
        encoding: 'utf8',
        shell: process.platform === 'win32',
    });
    if (result.error) {
        throw new Error(`Cannot run npm: ${result.error.message}`);
    }
    if (result.status !== 0) {
        const detail = (result.stderr || result.stdout || '').trim();
        throw new Error(`Cannot ${purpose}: ${detail || `npm exited with ${result.status}`}`);
    }
    return result.stdout;
}

function parseNpmPackJson(output) {
    // Extract the JSON array defensively; npm may emit notice lines around it.
    const start = output.indexOf('[');
    const end = output.lastIndexOf(']');
    if (start === -1 || end < start) {
        throw new Error('npm pack --json produced no JSON array.');
    }
    return JSON.parse(output.slice(start, end + 1));
}

function resolvePackedPaths(root, packageFile) {
    if (packageFile !== undefined) {
        const tarballPath = isAbsolute(packageFile) ? packageFile : resolve(root, packageFile);
        if (!existsSync(tarballPath)) {
            throw new Error(`Cannot read package file '${tarballPath}'.`);
        }
        const result = spawnSync('tar', ['-tzf', tarballPath], { cwd: root, encoding: 'utf8' });
        if (result.error) {
            throw new Error(`Cannot run tar: ${result.error.message}`);
        }
        if (result.status !== 0) {
            const detail = (result.stderr || result.stdout || '').trim();
            throw new Error(`Cannot read tarball '${tarballPath}': ${detail || `tar exited with ${result.status}`}`);
        }
        const paths = new Set();
        for (const line of result.stdout.split(/\r?\n/)) {
            const entry = line.trim();
            // npm package tarballs carry file entries only; directory entries
            // from foreign archives contribute no file, so they are skipped.
            if (entry === '' || entry === 'package' || entry.endsWith('/')) {
                continue;
            }
            if (!entry.startsWith('package/')) {
                throw new Error(
                    `Tarball '${tarballPath}' carries the out-of-place entry '${entry}'; ` +
                        'npm package tarballs must hold every file under the package/ root.',
                );
            }
            paths.add(entry.slice('package/'.length));
        }
        return { paths: [...paths], packageFile: basename(tarballPath) };
    }
    const packed = parseNpmPackJson(runNpm(['pack', '--json'], root, 'pack the package'))[0];
    return {
        paths: packed.files.map((file) => file.path),
        packageFile: packed.filename,
    };
}

function verifyPayload(expected, packedPaths) {
    const packed = new Set(packedPaths);
    const unallowlisted = [...packed].filter((path) => !expected.has(path)).sort();
    const missing = [...expected].filter((path) => !packed.has(path)).sort();
    if (unallowlisted.length > 0) {
        throw new Error(
            `The package carries ${unallowlisted.length} file(s) outside the publish allowlist ` +
                "in 'package.json'; extend the allowlist deliberately or remove the files:\n" +
                unallowlisted.join('\n'),
        );
    }
    if (missing.length > 0) {
        throw new Error(
            `The package is missing ${missing.length} allowlisted file(s) that the repository ` +
                'tracks; the payload would silently shrink:\n' + missing.join('\n'),
        );
    }
}

function resolveDistTag(version) {
    const prerelease = version.match(versionPattern)[4];
    return prerelease !== undefined ? 'next' : 'latest';
}

function main() {
    const parsed = parseArguments(process.argv.slice(2));
    const root = parsed.root !== undefined ? resolve(parsed.root) : repositoryRoot;

    const packageJson = readPackageJson(root);
    verifyTag(parsed.tag, packageJson.version);
    verifyGitWorkTree(root);

    const expected = new Set(listExpectedPayloadPaths(root, packageJson));
    const packed = resolvePackedPaths(root, parsed.packageFile);
    verifyPayload(expected, packed.paths);

    const npmTag = resolveDistTag(packageJson.version);
    console.log(`pkg: ${packageJson.name}`);
    console.log(`version: ${packageJson.version}`);
    console.log(`npm_tag: ${npmTag}`);
    console.log(`package_file: ${packed.packageFile}`);
    console.log(`entries: ${packed.paths.length}`);
}

try {
    main();
} catch (error) {
    console.error(`error: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
}
