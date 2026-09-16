/**
 * Shared release payload helpers for com.wallstop-studios.data-visualizer.
 *
 * Both `verify-release.mjs` and `build-unitypackage.mjs` resolve the shipped
 * package payload the same way: `package.json` must carry a name, a supported
 * semver version, and a non-empty `files` allowlist, and the expected payload
 * is derived from tracked files only (npm auto-included root files plus
 * `files` entries) so both tools always see committed content.
 */

import { existsSync, readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { join } from 'node:path';

export const versionPattern =
    /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(rc|alpha|beta|preview)(?:\.(0|[1-9]\d*))?)?$/;
export const autoIncludeRootPatterns = [/^readme/i, /^licen[cs]e/i, /^changelog/i];

export function runGit(root, args) {
    const result = spawnSync('git', args, { cwd: root, encoding: 'utf8' });
    if (result.error) {
        throw new Error(`Cannot run git: ${result.error.message}`);
    }
    return result;
}

export function gitOutput(root, args, purpose) {
    const result = runGit(root, args);
    if (result.status !== 0) {
        const detail = (result.stderr || result.stdout || '').trim();
        throw new Error(`Cannot ${purpose}: ${detail || `git exited with ${result.status}`}`);
    }
    return result.stdout;
}

export function readPackageJson(root) {
    const packagePath = join(root, 'package.json');
    if (!existsSync(packagePath)) {
        throw new Error(`Cannot read required release file '${packagePath}'.`);
    }
    let packageJson;
    try {
        packageJson = JSON.parse(readFileSync(packagePath, 'utf8'));
    } catch (error) {
        throw new Error(`'package.json' is not valid JSON: ${error.message}`);
    }
    if (typeof packageJson.name !== 'string' || packageJson.name.trim() === '') {
        throw new Error("'package.json' is missing a name field.");
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
    if (!Array.isArray(packageJson.files) || packageJson.files.length === 0) {
        throw new Error("'package.json' must carry a non-empty 'files' publish allowlist.");
    }
    return packageJson;
}

export function isAutoIncludedRootFile(relativePath) {
    if (relativePath.includes('/')) {
        return false;
    }
    if (relativePath === 'package.json') {
        return true;
    }
    return autoIncludeRootPatterns.some((pattern) => pattern.test(relativePath));
}

export function isAllowlisted(relativePath, filesEntries) {
    if (isAutoIncludedRootFile(relativePath)) {
        return true;
    }
    return filesEntries.some((rawEntry) => {
        const entry = rawEntry.replace(/\/+$/, '');
        return relativePath === entry || relativePath.startsWith(`${entry}/`);
    });
}

export function verifyGitWorkTree(root) {
    const inside = runGit(root, ['rev-parse', '--is-inside-work-tree']);
    if (inside.status !== 0 || inside.stdout.trim() !== 'true') {
        throw new Error(`'${root}' is not a git work tree.`);
    }
}

export function listExpectedPayloadPaths(root, packageJson) {
    const tracked = gitOutput(root, ['ls-files'], 'list tracked files')
        .split(/\r?\n/)
        .filter((line) => line.trim() !== '');
    const expected = tracked.filter((path) => isAllowlisted(path, packageJson.files));
    if (expected.length === 0) {
        throw new Error("The publish allowlist in 'package.json' selects no tracked files.");
    }
    return expected.sort();
}
