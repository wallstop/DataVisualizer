#!/usr/bin/env node
/**
 * Deterministic `.unitypackage` builder for com.wallstop-studios.data-visualizer.
 *
 * Builds the release `.unitypackage` and a `.sha256` checksum file from the
 * tracked, `files`-allowlisted package payload with no Unity invocation, so
 * the archive can be assembled locally or in cloud CI. The payload rules are
 * shared with `verify-release.mjs` via `package-payload.mjs`, so the npm
 * tarball and the `.unitypackage` always carry the same shipped set.
 *
 * Usage:
 *   node scripts/release/build-unitypackage.mjs [--root <path>] [--output <dir>] [--help]
 *
 * Archive layout (standard `.unitypackage` GUID directories, staged under
 * `Packages/<package name>/`):
 *   - Every payload file that is not a `.meta` yields `<guid>/pathname`,
 *     `<guid>/asset`, and `<guid>/asset.meta`, where the GUID is derived
 *     deterministically from the staged path and `asset.meta` carries the
 *     committed `.meta` content.
 *   - Every payload `.meta` folds into its target's `asset.meta`; it never
 *     becomes a separate GUID entry.
 *   - A `.meta` whose target is a tracked directory (e.g. `Editor.meta`)
 *     yields `<guid>/pathname` with a trailing `/` plus `<guid>/asset.meta`
 *     and no `asset` member, matching Unity's own folder exports.
 *
 * Fail-closed guards:
 *   - The tree must be a git work tree; the payload is derived from tracked
 *     files only, so the builder always sees committed content.
 *   - `package.json` must carry a name, a supported semver version, and a
 *     non-empty `files` allowlist selecting at least one tracked file.
 *   - Every payload file must have its committed `.meta` companion, and every
 *     payload `.meta` must target a tracked file or directory. Missing and
 *     orphan `.meta` files both fail.
 *   - Payload paths must be safe relative paths (no absolute paths, no
 *     backslashes, no `.`/`..`/empty segments).
 *
 * Determinism: GUIDs hash the staged path, members are emitted in sorted
 * staged-path order, tar headers use zeroed ownership and timestamps, and the
 * gzip stream carries no timestamp, so two builds of the same tree are
 * byte-identical. The archive carries only GUID-directory members, so staged
 * path length never approaches the ustar name field.
 */

import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { gzipSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { listExpectedPayloadPaths, readPackageJson, verifyGitWorkTree } from './package-payload.mjs';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '..', '..');
const metaSuffix = '.meta';

const usage = [
    'Build the deterministic release .unitypackage and its .sha256 checksum.',
    '',
    'Options:',
    '  --root <path>     Repository root to operate on (default: this repository).',
    '  --output <dir>    Output directory (default: <root>/dist).',
    '  --help            Show this help.',
].join('\n');

function parseArguments(argv) {
    const parsed = { root: undefined, output: undefined };
    for (let index = 0; index < argv.length; index++) {
        const argument = argv[index];
        switch (argument) {
            case '--root':
                parsed.root = readValue(argv, ++index, argument);
                break;
            case '--output':
                parsed.output = readValue(argv, ++index, argument);
                break;
            case '--help':
                console.log(usage);
                process.exit(0);
                break;
            default:
                throw new Error(`Unknown argument '${argument}'.\n\n${usage}`);
        }
    }
    return parsed;
}

function readValue(argv, index, flag) {
    if (index >= argv.length) {
        throw new Error(`Missing value for ${flag}.`);
    }
    return argv[index];
}

function isUnsafePayloadPath(relativePath) {
    if (relativePath.startsWith('/') || relativePath.includes('\\')) {
        return true;
    }
    return relativePath.split('/').some((segment) => segment === '' || segment === '.' || segment === '..');
}

function collectArchiveEntries(root, payloadPaths, packageRootPrefix) {
    const unsafe = payloadPaths.filter((path) => isUnsafePayloadPath(path));
    if (unsafe.length > 0) {
        throw new Error(
            `The payload carries ${unsafe.length} unsafe path(s) that cannot be staged ` +
                'into a .unitypackage:\n' + unsafe.join('\n'),
        );
    }
    const payload = new Set(payloadPaths);
    const files = [];
    const missingMeta = [];
    for (const path of payloadPaths) {
        if (path.endsWith(metaSuffix)) {
            continue;
        }
        const metaPath = `${path}${metaSuffix}`;
        if (!payload.has(metaPath)) {
            missingMeta.push(path);
            continue;
        }
        files.push({ stagedPath: `${packageRootPrefix}/${path}`, assetPath: path, metaPath });
    }
    if (missingMeta.length > 0) {
        throw new Error(
            `${missingMeta.length} payload file(s) have no committed '.meta' companion; a ` +
                '.unitypackage cannot carry them without their importer settings:\n' +
                missingMeta.sort().join('\n'),
        );
    }
    const folders = [];
    const orphanMetas = [];
    for (const path of payloadPaths) {
        if (!path.endsWith(metaSuffix)) {
            continue;
        }
        const target = path.slice(0, -metaSuffix.length);
        if (payload.has(target)) {
            continue;
        }
        if (payloadPaths.some((candidate) => candidate.startsWith(`${target}/`))) {
            folders.push({ stagedPath: `${packageRootPrefix}/${target}/`, metaPath: path });
            continue;
        }
        orphanMetas.push(path);
    }
    if (orphanMetas.length > 0) {
        throw new Error(
            `${orphanMetas.length} payload '.meta' file(s) target neither a tracked file nor a ` +
                'directory with tracked payload children:\n' + orphanMetas.sort().join('\n'),
        );
    }
    return [...files, ...folders].sort((left, right) => compareOrdinal(left.stagedPath, right.stagedPath));
}

function compareOrdinal(left, right) {
    return left < right ? -1 : left > right ? 1 : 0;
}

function resolveGuid(stagedPath) {
    return createHash('md5').update(stagedPath, 'utf8').digest('hex');
}

function resolveMembers(root, entries) {
    const members = [];
    for (const entry of entries) {
        const guid = resolveGuid(entry.stagedPath);
        const metaContent = readFileSync(join(root, entry.metaPath));
        members.push({ name: `${guid}/pathname`, data: Buffer.from(`${entry.stagedPath}\n`, 'utf8') });
        if (!entry.stagedPath.endsWith('/')) {
            members.push({ name: `${guid}/asset`, data: readFileSync(join(root, entry.assetPath)) });
        }
        members.push({ name: `${guid}/asset.meta`, data: metaContent });
    }
    return members;
}

function writeOctal(value, length) {
    return `${value.toString(8).padStart(length - 1, '0')}\0`;
}

function writeTarHeader(name, size) {
    if (Buffer.byteLength(name, 'utf8') > 100) {
        throw new Error(`Staged member '${name}' does not fit a ustar header.`);
    }
    const header = Buffer.alloc(512, 0);
    header.write(name, 0, 100, 'utf8');
    header.write(writeOctal(0o644, 8), 100, 8, 'latin1');
    header.write(writeOctal(0, 8), 108, 8, 'latin1');
    header.write(writeOctal(0, 8), 116, 8, 'latin1');
    header.write(writeOctal(size, 12), 124, 12, 'latin1');
    header.write(writeOctal(0, 12), 136, 12, 'latin1');
    header.write('        ', 148, 8, 'latin1');
    header.write('0', 156, 1, 'latin1');
    header.write('ustar\0', 257, 6, 'latin1');
    header.write('00', 263, 2, 'latin1');
    header.write('0000000\0', 229, 8, 'latin1');
    header.write('0000000\0', 237, 8, 'latin1');
    let checksum = 0;
    for (const byte of header) {
        checksum += byte;
    }
    header.write(`${checksum.toString(8).padStart(6, '0')}\0 `, 148, 8, 'latin1');
    return header;
}

function writeTar(members) {
    const chunks = [];
    for (const member of members) {
        chunks.push(writeTarHeader(member.name, member.data.length));
        chunks.push(member.data);
        const padding = (512 - (member.data.length % 512)) % 512;
        if (padding > 0) {
            chunks.push(Buffer.alloc(padding, 0));
        }
    }
    chunks.push(Buffer.alloc(1024, 0));
    return Buffer.concat(chunks);
}

function main() {
    const parsed = parseArguments(process.argv.slice(2));
    const root = parsed.root !== undefined ? resolve(parsed.root) : repositoryRoot;
    const outputDirectory = parsed.output !== undefined ? resolve(parsed.output) : join(root, 'dist');

    verifyGitWorkTree(root);
    const packageJson = readPackageJson(root);
    const payloadPaths = listExpectedPayloadPaths(root, packageJson);
    const packageRootPrefix = `Packages/${packageJson.name}`;
    const entries = collectArchiveEntries(root, payloadPaths, packageRootPrefix);
    const tarBuffer = writeTar(resolveMembers(root, entries));
    const gzipBuffer = gzipSync(tarBuffer, { mtime: 0 });

    const archiveName = `${packageJson.name}-${packageJson.version}.unitypackage`;
    const archivePath = join(outputDirectory, archiveName);
    mkdirSync(outputDirectory, { recursive: true });
    writeFileSync(archivePath, gzipBuffer);
    const sha256 = createHash('sha256').update(gzipBuffer).digest('hex');
    writeFileSync(`${archivePath}.sha256`, `${sha256}  ${archiveName}\n`);

    console.log(`package_file: ${archiveName}`);
    console.log(`sha256: ${sha256}`);
    console.log(`entries: ${entries.length}`);
    console.log(`output: ${outputDirectory}`);
}

try {
    main();
} catch (error) {
    console.error(`error: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
}
