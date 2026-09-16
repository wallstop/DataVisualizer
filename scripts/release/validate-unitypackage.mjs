#!/usr/bin/env node
/**
 * Standalone `.unitypackage` validator for com.wallstop-studios.data-visualizer.
 *
 * Validates a built `.unitypackage` against the tracked, `files`-allowlisted
 * package payload with no Unity invocation, so a locally built or cloud-built
 * archive can be fail-closed before it is attached to a release. The archive
 * is parsed with standard `tar` tooling, independent of the tar writer in
 * `build-unitypackage.mjs`, so a builder defect cannot mirror itself into the
 * verification. The payload rules are shared with the builder and
 * `verify-release.mjs` via `package-payload.mjs`.
 *
 * Archive layout checks: the `.sha256` sidecar next to the archive must be
 * sha256sum formatted, name this archive, and carry its actual SHA-256; every
 * member must be `<guid>/(pathname|asset|asset.meta)` inside a lowercase
 * 32-hex GUID directory with no duplicates; every GUID directory carries
 * `pathname` + `asset.meta`, plus `asset` for file entries (folder entries,
 * marked by a trailing-`/` pathname, must not carry an `asset`); every
 * `pathname` is one `\n`-terminated safe relative path under `Packages/`.
 *
 * Payload checks: the root must be a git work tree whose valid `package.json`
 * allowlists at least one tracked file; the expected entries are derived from
 * tracked files only (fail-closed on unsafe paths, missing or orphan `.meta`
 * companions, and malformed or duplicate committed GUIDs). The archive's
 * staged paths must exactly cover the expected payload, and every entry must
 * match the repository: GUID directory name equals the committed `guid:`
 * field, and `asset.meta`/`asset` bytes equal the committed meta/tracked file.
 *
 * Reports the package name, version, archive filename, SHA-256, and GUID
 * directory (entry) count. Gating a release directory's overall artifact set
 * (archive + sidecar + npm tarball) belongs to the release workflow slice.
 */

import { createHash } from 'node:crypto';
import { existsSync, mkdtempSync, readFileSync, rmSync, statSync } from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { listExpectedPayloadPaths, readPackageJson, verifyGitWorkTree } from './package-payload.mjs';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '..', '..');
const metaSuffix = '.meta';
const guidDirectoryPattern = /^([0-9a-f]{32})\/(pathname|asset|asset\.meta)$/;
const metaGuidPattern = /^guid:\s*([0-9a-fA-F]{32})\s*$/m;
const checksumLinePattern = /^([0-9a-fA-F]{64})[ \t]+\*?(.+)$/;

const usage = [
    'Validate a built .unitypackage against the tracked package payload.',
    '',
    'Options:',
    '  --archive <path>          The .unitypackage to validate (required).',
    '  --root <path>             Repository root to operate on (default: this repository).',
    '  --help                    Show this help.',
].join('\n');

function parseArguments(argv) {
    const parsed = { archive: undefined, root: undefined };
    for (let index = 0; index < argv.length; index++) {
        const argument = argv[index];
        switch (argument) {
            case '--archive':
                parsed.archive = readValue(argv, ++index, argument);
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
    if (parsed.archive === undefined) {
        throw new Error('--archive <path> is required.\n\n' + usage);
    }
    return parsed;
}

function readValue(argv, index, flag) {
    if (index >= argv.length) {
        throw new Error(`Missing value for ${flag}.`);
    }
    return argv[index];
}

function runTar(args, purpose) {
    const result = spawnSync('tar', args, { encoding: 'utf8' });
    if (result.error) {
        throw new Error(`Cannot run tar: ${result.error.message}`);
    }
    if (result.status !== 0) {
        const detail = (result.stderr || result.stdout || '').trim();
        throw new Error(`Cannot ${purpose}: ${detail || `tar exited with ${result.status}`}`);
    }
    return result.stdout;
}

function verifyChecksum(archivePath, checksumFile, archiveSha256) {
    if (!existsSync(checksumFile)) {
        throw new Error(
            `Cannot read the required checksum sidecar '${checksumFile}'; a release ` +
                'archive must ship with its .sha256 checksum.',
        );
    }
    const lines = readFileSync(checksumFile, 'utf8')
        .trim()
        .split(/\r?\n/);
    if (lines.length !== 1) {
        throw new Error(`Checksum file '${checksumFile}' must carry exactly one sha256sum line.`);
    }
    const match = lines[0].match(checksumLinePattern);
    if (match === null) {
        throw new Error(
            `Checksum file '${checksumFile}' is not in sha256sum format ` +
                '(<64-hex hash> <whitespace> <filename>).',
        );
    }
    if (match[1].toLowerCase() !== archiveSha256) {
        throw new Error(
            `Checksum file '${checksumFile}' carries '${match[1]}' but the archive hashes to ` +
                `'${archiveSha256}'; the artifact is corrupt or the sidecar is stale.`,
        );
    }
    const recordedName = match[2].trim();
    const archiveName = basename(archivePath);
    if (recordedName !== archiveName) {
        throw new Error(
            `Checksum file '${checksumFile}' names '${recordedName}' but the validated archive ` +
                `is '${archiveName}'.`,
        );
    }
}

function listArchiveMembers(archivePath) {
    const listing = runTar(['-tzf', archivePath], 'list the archive');
    const members = [];
    for (const line of listing.split(/\r?\n/)) {
        const entry = line.trim();
        if (entry !== '') {
            members.push(entry);
        }
    }
    return members;
}

function groupMembers(members) {
    const grouped = new Map();
    const seen = new Set();
    for (const name of members) {
        if (seen.has(name)) {
            throw new Error(
                `The archive carries the duplicate member '${name}'; duplicate members would ` +
                    'stage the same content twice.',
            );
        }
        seen.add(name);
        const match = name.match(guidDirectoryPattern);
        if (match === null) {
            throw new Error(
                `The archive carries the unexpected member '${name}'; every member must be ` +
                    '<guid>/(pathname|asset|asset.meta) inside a lowercase 32-hex GUID directory.',
            );
        }
        const guid = match[1];
        const group = grouped.get(guid) ?? {};
        group[match[2]] = true;
        grouped.set(guid, group);
    }
    if (grouped.size === 0) {
        throw new Error(
            'The archive carries no GUID-directory entries; a .unitypackage must stage at ' +
                'least one asset.',
        );
    }
    return grouped;
}

function isUnsafePayloadPath(relativePath) {
    if (relativePath.startsWith('/') || relativePath.includes('\\')) {
        return true;
    }
    return relativePath.split('/').some((segment) => segment === '' || segment === '.' || segment === '..');
}

function isUnsafeStagedPath(stagedPath) {
    if (!stagedPath.startsWith('Packages/')) {
        return true;
    }
    // Folder entries legitimately end with exactly one trailing '/'.
    const withoutTrailingSlash = stagedPath.endsWith('/') ? stagedPath.slice(0, -1) : stagedPath;
    return isUnsafePayloadPath(withoutTrailingSlash);
}

function readMember(extractDirectory, name) {
    try {
        return readFileSync(join(extractDirectory, name));
    } catch {
        throw new Error(`The archive member '${name}' vanished between listing and extraction.`);
    }
}

function decodeStagedPath(guid, pathnameBytes) {
    const text = pathnameBytes.toString('utf8');
    if (!text.endsWith('\n') || text.slice(0, -1).includes('\n') || text.includes('\r')) {
        throw new Error(
            `The pathname member for guid '${guid}' must be exactly one \\n-terminated staged path.`,
        );
    }
    const stagedPath = text.slice(0, -1);
    if (stagedPath === '' || isUnsafeStagedPath(stagedPath)) {
        throw new Error(
            `The pathname member for guid '${guid}' stages the unsafe path '${stagedPath}'; ` +
                'staged paths must be safe relative paths under Packages/.',
        );
    }
    return stagedPath;
}

function resolveArchiveEntries(grouped, extractDirectory) {
    const entries = [];
    for (const [guid, members] of grouped) {
        if (members.pathname !== true || members['asset.meta'] !== true) {
            throw new Error(
                `GUID directory '${guid}' is incomplete: every entry needs 'pathname' and ` +
                    `'asset.meta' members.`,
            );
        }
        const stagedPath = decodeStagedPath(guid, readMember(extractDirectory, `${guid}/pathname`));
        const isFolder = stagedPath.endsWith('/');
        if (isFolder && members.asset === true) {
            throw new Error(
                `GUID directory '${guid}' stages the folder '${stagedPath}' but carries an ` +
                    `'asset' member; folder entries carry pathname + asset.meta only.`,
            );
        }
        if (!isFolder && members.asset !== true) {
            throw new Error(
                `GUID directory '${guid}' stages the file '${stagedPath}' but is missing its ` +
                    `'asset' member.`,
            );
        }
        entries.push({ guid, stagedPath, isFolder });
    }
    return entries.sort((left, right) => compareOrdinal(left.stagedPath, right.stagedPath));
}

function compareOrdinal(left, right) {
    return left < right ? -1 : left > right ? 1 : 0;
}

function resolveExpectedEntries(root, packageJson) {
    const payloadPaths = listExpectedPayloadPaths(root, packageJson);
    const packageRootPrefix = `Packages/${packageJson.name}`;
    const unsafe = payloadPaths.filter((path) => isUnsafePayloadPath(path));
    if (unsafe.length > 0) {
        throw new Error(
            `The payload carries ${unsafe.length} unsafe path(s) that cannot be validated as ` +
                'staged .unitypackage entries:\n' + unsafe.join('\n'),
        );
    }
    const payload = new Set(payloadPaths);
    const entries = [];
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
        entries.push({ stagedPath: `${packageRootPrefix}/${path}`, assetPath: path, metaPath });
    }
    if (missingMeta.length > 0) {
        throw new Error(
            `${missingMeta.length} payload file(s) have no committed '.meta' companion, so the ` +
                'archive cannot carry them with their importer settings:\n' +
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
    entries.push(...folders);
    const guidOwners = new Map();
    for (const entry of entries) {
        const metaContent = readFileSync(join(root, entry.metaPath));
        const match = metaContent.toString('utf8').match(metaGuidPattern);
        if (match === null) {
            throw new Error(
                `'${entry.metaPath}' carries no well-formed 'guid:' field, so its archive entry ` +
                    'cannot be checked against the committed asset identity.',
            );
        }
        const guid = match[1].toLowerCase();
        const owner = guidOwners.get(guid);
        if (owner !== undefined) {
            throw new Error(
                `'${entry.metaPath}' reuses guid '${guid}', already carried by '${owner}'; ` +
                    'duplicate committed identities cannot produce a valid import.',
            );
        }
        guidOwners.set(guid, entry.metaPath);
        entry.guid = guid;
        entry.metaContent = metaContent;
    }
    return entries;
}

function compareArchiveWithExpected(archiveEntries, expectedEntries, root, extractDirectory) {
    const expected = new Map(expectedEntries.map((entry) => [entry.stagedPath, entry]));
    const claimed = new Map();
    for (const archiveEntry of archiveEntries) {
        const previous = claimed.get(archiveEntry.stagedPath);
        if (previous !== undefined) {
            throw new Error(
                `GUID directories '${previous.guid}' and '${archiveEntry.guid}' both stage ` +
                    `'${archiveEntry.stagedPath}'; duplicate assets would corrupt the import.`,
            );
        }
        claimed.set(archiveEntry.stagedPath, archiveEntry);
    }
    const unexpected = archiveEntries
        .filter((entry) => !expected.has(entry.stagedPath))
        .map((entry) => entry.stagedPath);
    if (unexpected.length > 0) {
        throw new Error(
            `The archive stages ${unexpected.length} path(s) outside the tracked payload; ` +
                'extend the allowlist deliberately or rebuild from a clean tree:\n' +
                unexpected.join('\n'),
        );
    }
    const missing = [...expected.keys()].filter((path) => !claimed.has(path)).sort();
    if (missing.length > 0) {
        throw new Error(
            `The archive is missing ${missing.length} tracked payload path(s); the payload ` +
                'would silently shrink:\n' + missing.join('\n'),
        );
    }
    const mismatches = [];
    for (const archiveEntry of archiveEntries) {
        const expectedEntry = expected.get(archiveEntry.stagedPath);
        if (archiveEntry.guid !== expectedEntry.guid) {
            mismatches.push(
                `'${archiveEntry.stagedPath}' sits in guid directory '${archiveEntry.guid}' but ` +
                    `the committed meta '${expectedEntry.metaPath}' carries '${expectedEntry.guid}'.`,
            );
            continue;
        }
        const archivedMeta = readMember(extractDirectory, `${archiveEntry.guid}/asset.meta`);
        if (!archivedMeta.equals(expectedEntry.metaContent)) {
            mismatches.push(
                `'${archiveEntry.stagedPath}': asset.meta does not byte-match the committed ` +
                    `'${expectedEntry.metaPath}'.`,
            );
        }
        if (archiveEntry.isFolder) {
            continue;
        }
        const archivedAsset = readMember(extractDirectory, `${archiveEntry.guid}/asset`);
        if (!archivedAsset.equals(readFileSync(join(root, expectedEntry.assetPath)))) {
            mismatches.push(
                `'${archiveEntry.stagedPath}': asset does not byte-match the tracked file ` +
                    `'${expectedEntry.assetPath}'.`,
            );
        }
    }
    if (mismatches.length > 0) {
        throw new Error(
            `The archive disagrees with the repository in ${mismatches.length} place(s):\n` +
                mismatches.join('\n'),
        );
    }
}

function main() {
    const parsed = parseArguments(process.argv.slice(2));
    const root = parsed.root !== undefined ? resolve(parsed.root) : repositoryRoot;
    const archivePath = resolve(parsed.archive);
    let archiveStat;
    try {
        archiveStat = statSync(archivePath);
    } catch {
        throw new Error(`Cannot read a non-empty archive file at '${archivePath}'.`);
    }
    if (!archiveStat.isFile() || archiveStat.size === 0) {
        throw new Error(`Cannot read a non-empty archive file at '${archivePath}'.`);
    }

    const gzipBuffer = readFileSync(archivePath);
    const archiveSha256 = createHash('sha256').update(gzipBuffer).digest('hex');
    verifyChecksum(archivePath, `${archivePath}.sha256`, archiveSha256);

    const grouped = groupMembers(listArchiveMembers(archivePath));
    const extractDirectory = mkdtempSync(join(tmpdir(), 'unitypackage-validate-'));
    try {
        runTar(['-xzf', archivePath, '-C', extractDirectory], 'extract the archive');
        const archiveEntries = resolveArchiveEntries(grouped, extractDirectory);

        verifyGitWorkTree(root);
        const packageJson = readPackageJson(root);
        const expectedEntries = resolveExpectedEntries(root, packageJson);
        compareArchiveWithExpected(archiveEntries, expectedEntries, root, extractDirectory);

        console.log(`pkg: ${packageJson.name}`);
        console.log(`version: ${packageJson.version}`);
        console.log(`package_file: ${basename(archivePath)}`);
        console.log(`sha256: ${archiveSha256}`);
        console.log(`entries: ${archiveEntries.length}`);
    } finally {
        rmSync(extractDirectory, { recursive: true, force: true });
    }
}

try {
    main();
} catch (error) {
    console.error(`error: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
}
