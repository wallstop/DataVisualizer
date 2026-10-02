#!/usr/bin/env node
/**
 * Release-notes extraction for com.wallstop-studios.data-visualizer.
 *
 * The tag-driven publish workflow generates GitHub Release notes from the
 * exact rotated CHANGELOG section for the released version. This script is
 * the tested mechanism behind that step: it prints the body of the
 * `## [X.Y.Z] - YYYY-MM-DD` section (everything between its heading line and
 * the next `## ` heading) and fails closed when the section is missing or
 * duplicated, so a release can never go out with empty or ambiguous notes.
 *
 * Usage:
 *   node scripts/release/extract-release-notes.mjs --version X.Y.Z [--root <path>] [--output <path>]
 *
 * Options:
 *   --version <X.Y.Z>  Version section to extract (required, no leading 'v').
 *   --root <path>      Repository root to operate on (default: this repository).
 *   --output <path>    Write the notes to this file instead of stdout.
 *   --help             Show this help.
 */

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '..', '..');

const usage = [
    'Extract the CHANGELOG release notes for a released version.',
    '',
    'Options:',
    '  --version <X.Y.Z>  Version section to extract (required, no leading \'v\').',
    '  --root <path>      Repository root to operate on (default: this repository).',
    '  --output <path>    Write the notes to this file instead of stdout.',
    '  --help             Show this help.',
].join('\n');

function parseArguments(argv) {
    const parsed = { version: undefined, root: undefined, output: undefined };
    for (let index = 0; index < argv.length; index++) {
        const argument = argv[index];
        switch (argument) {
            case '--version':
                parsed.version = readValue(argv, ++index, argument);
                break;
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
    if (parsed.version === undefined) {
        throw new Error('--version <X.Y.Z> is required.\n\n' + usage);
    }
    return parsed;
}

function readValue(argv, index, flag) {
    if (index >= argv.length) {
        throw new Error(`Missing value for ${flag}.`);
    }
    return argv[index];
}

function escapeRegExp(text) {
    return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function extractSection(changelog, version) {
    const lines = changelog.split(/\r?\n/);
    const headingPattern = new RegExp(`^## \\[${escapeRegExp(version)}\\](?:\\s|$)`);
    const headingIndexes = [];
    for (let index = 0; index < lines.length; index++) {
        if (headingPattern.test(lines[index])) {
            headingIndexes.push(index);
        }
    }
    if (headingIndexes.length === 0) {
        throw new Error(`CHANGELOG.md has no '## [${version}]' section.`);
    }
    if (headingIndexes.length > 1) {
        throw new Error(`CHANGELOG.md carries ${headingIndexes.length} '## [${version}]' sections.`);
    }
    const start = headingIndexes[0] + 1;
    let end = lines.length;
    for (let index = start; index < lines.length; index++) {
        if (/^## /.test(lines[index])) {
            end = index;
            break;
        }
    }
    const body = lines
        .slice(start, end)
        .join('\n')
        .replace(/^\s*\n+|\s+$/g, '');
    if (body === '') {
        throw new Error(`CHANGELOG.md section '## [${version}]' is empty.`);
    }
    return `${body}\n`;
}

function main() {
    const parsed = parseArguments(process.argv.slice(2));
    const root = parsed.root !== undefined ? resolve(parsed.root) : repositoryRoot;
    if (/^v/i.test(parsed.version)) {
        throw new Error(`--version expects the bare package version '${parsed.version.replace(/^v/i, '')}', not the tag '${parsed.version}'.`);
    }
    const changelogPath = join(root, 'CHANGELOG.md');
    let changelog;
    try {
        changelog = readFileSync(changelogPath, 'utf8');
    } catch {
        throw new Error(`Cannot read '${changelogPath}'.`);
    }
    const notes = extractSection(changelog, parsed.version);
    if (parsed.output !== undefined) {
        writeFileSync(resolve(root, parsed.output), notes);
        console.log(`notes: ${parsed.output}`);
    } else {
        process.stdout.write(notes);
    }
}

try {
    main();
} catch (error) {
    console.error(`error: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
}
