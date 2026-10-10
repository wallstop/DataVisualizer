#!/usr/bin/env node
/**
 * Changelog entry length: every entry in the CHANGELOG.md '[Unreleased]'
 * section stays within 300 rendered characters.
 *
 * The entry style (one or two sentences, at most 300 rendered characters,
 * issue references and link targets excluded) lives in the agent guidance,
 * but nothing checked it, so the cap was remembered rather than enforced.
 * This lint makes the cap a gate. Released sections are immutable history
 * and stay unmeasured, so the check reads '[Unreleased]' only.
 *
 * The measured length is the rendered text: soft-wrapped lines join into one
 * line, emphasis and inline-code markers ('**', '*', '`') strip away, and an
 * inline link '[text](target)' keeps only its text. Issue references are
 * excluded entirely, both the bare '#123' and the parenthesized '(#123)' the
 * changelog appends, together with the space that separates them from the
 * sentence. Only these shapes are excluded; any other link syntax, such as a
 * bare URL in prose, counts, so an unknown shape can only over-count, never
 * under-count, and the cap fails closed.
 *
 * The check also fails closed when the '[Unreleased]' section is missing or
 * duplicated: a changelog the check cannot locate has no measured state, and
 * a renamed heading would otherwise silence the cap instead of tripping it.
 * A section without entries passes; that is the normal shape right after a
 * release rotation.
 *
 * '--verbose' prints the rendered length of every entry when clean. Exit
 * codes: 0 = within the cap, 1 = at least one over-cap entry, a missing or
 * duplicated section, or an unreadable input.
 *
 * 'CHANGELOG_LENGTH_CHANGELOG' overrides the input so the self-test can point
 * at a fixture file. Nothing in CI sets it.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");

const CHANGELOG = process.env.CHANGELOG_LENGTH_CHANGELOG
    ? path.resolve(REPO_ROOT, process.env.CHANGELOG_LENGTH_CHANGELOG)
    : path.join(REPO_ROOT, "CHANGELOG.md");

const CAP = 300;

// The heading prepare-release.mjs rotates and Keep a Changelog defines. The
// exact match keeps a renamed or typo'd heading a failure instead of a
// vacuous pass.
const UNRELEASED_HEADING = /^## \[Unreleased\]\s*$/;
const SECTION_HEADING = /^## /;
const SUBSECTION_HEADING = /^### /;
// Every Markdown list marker: an entry the lint did not recognize would sit
// outside the cap instead of failing it, so all three unordered markers and
// both ordered forms count as entries.
const BULLET = /^(?:[-*+]|\d+[.)]) /;

// An inline link renders as its text; the target does not count.
const INLINE_LINK = /\[([^[\]]*)\]\([^()]*\)/g;
// A parenthesized issue reference also drops the space that separates it
// from the sentence, so '... the package style (#148).' counts as rendered.
const PARENTHESIZED_ISSUE_REFERENCE = /\s*\(#\d+\)/g;
// A bare issue reference drops with its separating space; the trailing word
// boundary keeps '#123abc' whole, so unknown shapes over-count, not under.
const BARE_ISSUE_REFERENCE = /\s*#\d+\b/g;
// Emphasis and inline-code markers delimit rendered text and do not count.
const INLINE_MARKER = /[*`]/g;

function readFileOrThrow(filePath, purpose) {
    try {
        return fs.readFileSync(filePath, "utf8");
    } catch {
        throw new Error(`Cannot ${purpose}: '${filePath}' is missing or unreadable.`);
    }
}

/*
    A repo-relative path when the file lives inside the repository, otherwise
    the absolute path, so a message never shows a '../../..' chain.
*/
function displayPath(filePath) {
    const relative = path.relative(REPO_ROOT, filePath).split(path.sep).join("/");
    return relative.startsWith("../") ? filePath : relative;
}

/*
    The '[Unreleased]' body: the lines between its heading and the next
    '## ' heading, failing closed on a missing or duplicated section.
*/
function extractUnreleased(lines, source) {
    const headingIndexes = [];
    for (let index = 0; index < lines.length; index++) {
        if (UNRELEASED_HEADING.test(lines[index])) {
            headingIndexes.push(index);
        }
    }
    if (headingIndexes.length === 0) {
        throw new Error(
            `'${displayPath(source)}' has no '## [Unreleased]' section, so the entry cap has ` +
                "nothing to measure.",
        );
    }
    if (headingIndexes.length > 1) {
        throw new Error(
            `'${displayPath(source)}' carries ${headingIndexes.length} '## [Unreleased]' sections.`,
        );
    }
    const start = headingIndexes[0] + 1;
    let end = lines.length;
    for (let index = start; index < lines.length; index++) {
        if (SECTION_HEADING.test(lines[index])) {
            end = index;
            break;
        }
    }
    return lines.slice(start, end);
}

/*
    One entry per top-level list item. A soft-wrapped continuation line joins
    the item above it; a blank line or a subsection heading ends the entry,
    and the nearest '### ' heading names it in the report.
*/
function extractEntries(sectionLines) {
    const entries = [];
    let current = null;
    let subsection = "";
    for (const rawLine of sectionLines) {
        const line = rawLine.trim();
        if (line === "") {
            current = null;
            continue;
        }
        if (SUBSECTION_HEADING.test(line)) {
            subsection = line;
            current = null;
            continue;
        }
        const bullet = BULLET.exec(line);
        if (bullet !== null) {
            current = { subsection, lines: [line.slice(bullet[0].length)] };
            entries.push(current);
            continue;
        }
        if (current !== null) {
            current.lines.push(line);
        }
    }
    return entries;
}

/*
    The rendered text an entry counts toward the cap. The order matters: the
    link rule runs first so a target can never leak into the issue-reference
    rules, and the whitespace collapse runs last so excluded references leave
    no stray gap behind.
*/
function renderEntry(entry) {
    let text = entry.lines.join(" ").replace(/\s+/g, " ").trim();
    text = text.replace(INLINE_LINK, "$1");
    text = text.replace(PARENTHESIZED_ISSUE_REFERENCE, "");
    text = text.replace(BARE_ISSUE_REFERENCE, "");
    text = text.replace(INLINE_MARKER, "");
    return text.replace(/\s+/g, " ").trim();
}

function main() {
    const verbose = process.argv.includes("--verbose");
    const changelog = readFileOrThrow(CHANGELOG, "read the changelog");
    const section = extractUnreleased(changelog.split(/\r?\n/), CHANGELOG);
    const entries = extractEntries(section).map((entry, index) => {
        const rendered = renderEntry(entry);
        return {
            subsection: entry.subsection,
            number: index + 1,
            rendered,
            length: rendered.length,
        };
    });

    const failures = [];
    for (const entry of entries) {
        if (entry.length > CAP) {
            const origin = entry.subsection ? ` ${entry.subsection}` : "";
            failures.push(
                `${displayPath(CHANGELOG)} '[Unreleased]'${origin} entry ${entry.number} measures ` +
                    `${entry.length} rendered characters, over the ${CAP} cap:\n  ${entry.rendered}`,
            );
        }
    }

    if (failures.length > 0) {
        console.error(
            "The changelog entry cap is broken. Entries measure as rendered text: emphasis and " +
                "code markers stripped, soft-wrapped lines joined, issue references and inline " +
                "link targets excluded.",
        );
        for (const failure of failures) {
            console.error(`  ${failure}`);
        }
        process.exit(1);
    }

    if (verbose) {
        const longest = entries.reduce((best, entry) => (entry.length > best ? entry.length : best), 0);
        console.log(
            `Changelog entry lengths in range: ${entries.length} entry(ies) in '[Unreleased]', ` +
                `longest ${longest} of ${CAP} characters.`,
        );
        for (const entry of entries) {
            const origin = entry.subsection ? ` ${entry.subsection}` : "";
            console.log(`  '[Unreleased]'${origin} entry ${entry.number}: ${entry.length}`);
        }
    }
    process.exit(0);
}

try {
    main();
} catch (error) {
    console.error(`error: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
}
