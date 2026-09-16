#!/usr/bin/env node
/**
 * Release preparation for com.wallstop-studios.data-visualizer.
 *
 * Bumps `package.json`, syncs the `**Version**:` line in `.llm/context.md`, and rotates
 * the `## [Unreleased]` changelog section into `## [X.Y.Z] - YYYY-MM-DD` (Keep a Changelog
 * 1.1.0). The dispatchable `.github/workflows/release-prep.yml` workflow runs this script
 * and opens the `release/vX.Y.Z` pull request.
 *
 * Usage:
 *   node scripts/release/prepare-release.mjs --bump patch|minor|major [--root <path>] [--dry-run]
 *   node scripts/release/prepare-release.mjs --version <semver>   [--root <path>] [--dry-run]
 *
 * Explicit versions accept bare semver with an optional prerelease suffix restricted to
 * `-rc`, `-alpha`, `-beta`, or `-preview` (optionally `.N`), matching the dist-tag rules
 * in `.github/workflows/npm-publish.yml`. A version bump applies to the numeric core and
 * drops an existing prerelease suffix; prerelease flows use `--version` instead.
 */

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '..', '..');

const versionPattern =
    '^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-(rc|alpha|beta|preview)(?:\\.(0|[1-9]\\d*))?)?$';
const versionRegex = new RegExp(versionPattern);
const unreleasedHeadingPattern = /^## \[Unreleased\]\s*$/;
const sectionHeadingPattern = /^## /;
const contextVersionPattern = /(\*\*Version\*\*:\s*)([^\r\n]*)/;

const usage = [
    'Prepare a release: bump the version and rotate the changelog.',
    '',
    'Options:',
    '  --bump <patch|minor|major>  Apply a semantic version bump.',
    '  --version <semver>          Set an explicit version (mutually exclusive with --bump).',
    '  --root <path>               Repository root to operate on (default: this repository).',
    '  --dry-run                   Report the plan without writing any file.',
    '  --help                      Show this help.',
].join('\n');

function parseArguments(argv) {
    const parsed = { bump: undefined, version: undefined, root: undefined, dryRun: false };
    for (let index = 0; index < argv.length; index++) {
        const argument = argv[index];
        switch (argument) {
            case '--bump':
                parsed.bump = readValue(argv, ++index, argument);
                break;
            case '--version':
                parsed.version = readValue(argv, ++index, argument);
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
    return parsed;
}

function readValue(argv, index, flag) {
    if (index >= argv.length) {
        throw new Error(`Missing value for ${flag}.`);
    }
    return argv[index];
}

function validateSemver(value, label) {
    if (!versionRegex.test(value)) {
        throw new Error(
            `${label} '${value}' is not supported semver. Use bare x.y.z with an optional ` +
                '-rc/-alpha/-beta/-preview (optionally .N) prerelease suffix.',
        );
    }
    return value;
}

function bumpVersion(current, bump) {
    const [major, minor, patch] = current.split('-')[0].split('.').map(Number);
    if (bump === 'patch') {
        return `${major}.${minor}.${patch + 1}`;
    }
    if (bump === 'minor') {
        return `${major}.${minor + 1}.0`;
    }
    return `${Number(major) + 1}.0.0`;
}

function resolveTargetVersion(parsed, current) {
    if (parsed.version !== undefined) {
        if (parsed.bump !== undefined) {
            throw new Error('--version and --bump are mutually exclusive; provide only one.');
        }
        return validateSemver(parsed.version, 'Explicit version');
    }
    if (parsed.bump === undefined) {
        throw new Error('Provide --bump <patch|minor|major> or --version <semver>.\n\n' + usage);
    }
    if (!['patch', 'minor', 'major'].includes(parsed.bump)) {
        throw new Error(`--bump must be patch, minor, or major; got '${parsed.bump}'.`);
    }
    const target = bumpVersion(validateSemver(current, 'Current version'), parsed.bump);
    if (target === current) {
        throw new Error(`A ${parsed.bump} bump of '${current}' produces no version change.`);
    }
    return target;
}

function readRepositoryFiles(root) {
    const packagePath = join(root, 'package.json');
    const changelogPath = join(root, 'CHANGELOG.md');
    const contextPath = join(root, '.llm', 'context.md');
    for (const filePath of [packagePath, changelogPath, contextPath]) {
        try {
            readFileSync(filePath, 'utf8');
        } catch {
            throw new Error(`Cannot read required release file '${filePath}'.`);
        }
    }
    return {
        packagePath,
        changelogPath,
        contextPath,
        packageJson: JSON.parse(readFileSync(packagePath, 'utf8')),
        changelog: readFileSync(changelogPath, 'utf8'),
        context: readFileSync(contextPath, 'utf8'),
    };
}

function serializePackageJson(packageJson) {
    return `${JSON.stringify(packageJson, null, 2)}\n`;
}

function syncContextVersion(context, version) {
    const updated = context.replace(contextVersionPattern, `$1${version}`);
    if (updated === context) {
        throw new Error("'.llm/context.md' does not carry a '**Version**: x.y.z' line to sync.");
    }
    return updated;
}

function rotateChangelog(changelog, version, dateStamp) {
    const lines = changelog.split(/\r?\n/);
    const unreleasedIndex = lines.findIndex((line) => unreleasedHeadingPattern.test(line));
    if (unreleasedIndex === -1) {
        throw new Error("CHANGELOG.md has no '## [Unreleased]' section to rotate.");
    }
    if (duplicateSectionIndex(lines, version) !== -1) {
        throw new Error(`CHANGELOG.md already carries a '## [${version}]' section.`);
    }

    let sectionEnd = unreleasedIndex + 1;
    while (sectionEnd < lines.length && !sectionHeadingPattern.test(lines[sectionEnd])) {
        sectionEnd++;
    }
    const bodyLines = lines.slice(unreleasedIndex + 1, sectionEnd);
    const contentLines = bodyLines.filter(
        (line) => line.trim() !== '' && !/^###\s/.test(line.trim()),
    );
    if (contentLines.length === 0) {
        throw new Error(
            "The '## [Unreleased]' section has no release notes; add entries before preparing a release.",
        );
    }
    while (bodyLines.length > 0 && bodyLines[bodyLines.length - 1].trim() === '') {
        bodyLines.pop();
    }

    const replacement = [
        '## [Unreleased]',
        '',
        `## [${version}] - ${dateStamp}`,
        ...bodyLines,
        '',
    ];
    return [...lines.slice(0, unreleasedIndex), ...replacement, ...lines.slice(sectionEnd)].join(
        '\n',
    );
}

function duplicateSectionIndex(lines, version) {
    const heading = `## [${version}]`;
    return lines.findIndex((line) => line.startsWith(heading));
}

function utcDateStamp() {
    return new Date().toISOString().slice(0, 10);
}

function main() {
    const parsed = parseArguments(process.argv.slice(2));
    const root = parsed.root !== undefined ? resolve(parsed.root) : repositoryRoot;
    const files = readRepositoryFiles(root);

    const currentVersion = files.packageJson.version;
    if (typeof currentVersion !== 'string' || currentVersion.trim() === '') {
        throw new Error("'package.json' is missing a version field.");
    }
    const targetVersion = resolveTargetVersion(parsed, currentVersion);
    if (targetVersion === currentVersion) {
        throw new Error(
            `Explicit version '${targetVersion}' equals the current version; a release must change the version.`,
        );
    }

    const updatedPackageJson = { ...files.packageJson, version: targetVersion };
    const updatedContext = syncContextVersion(files.context, targetVersion);
    const updatedChangelog = rotateChangelog(files.changelog, targetVersion, utcDateStamp());

    const changedFiles = ['package.json', '.llm/context.md', 'CHANGELOG.md'];
    if (parsed.dryRun) {
        console.log(`dry-run: would bump ${currentVersion} -> ${targetVersion}`);
        console.log(`dry-run: would update ${changedFiles.join(', ')}`);
    } else {
        writeFileSync(files.packagePath, serializePackageJson(updatedPackageJson));
        writeFileSync(files.contextPath, updatedContext);
        writeFileSync(files.changelogPath, updatedChangelog);
        console.log(`Bumped ${currentVersion} -> ${targetVersion}.`);
        console.log(`Updated ${changedFiles.join(', ')}.`);
    }
    console.log(`previous-version: ${currentVersion}`);
    console.log(`next-version: ${targetVersion}`);
}

try {
    main();
} catch (error) {
    console.error(`error: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
}
