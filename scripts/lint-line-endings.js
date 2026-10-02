#!/usr/bin/env node
/**
 * Line-ending contract: the ending CSharpier writes must be the ending git
 * checks out, on every platform.
 *
 * `.editorconfig` and `.gitattributes` each describe the working tree, and they
 * disagreed. `.editorconfig` required CRLF for every file, `.gitattributes` set
 * `* text=auto` with no `eol`, so git stored LF and checked out the platform
 * default. CSharpier follows `.editorconfig`, so `csharpier -- check` passed
 * only on Windows and reported every tracked C# file on Linux and macOS, and
 * the pre-commit hook rewrote staged files to an ending git normalized straight
 * back so the change never landed. No CI job ran CSharpier, so the drift was
 * silent. This check fails closed on that class of drift.
 *
 * Every path either configuration names is resolved on both sides and the two
 * endings must be equal, so a narrowed `.editorconfig` section cannot disagree
 * with a broad `.gitattributes` rule any more than a broad section can disagree
 * with a narrow rule. Resolving both directions needs one representative path
 * per `.editorconfig` section that declares an ending and one per
 * `.gitattributes` rule.
 *
 * Resolution follows git's own attribute model: each attribute takes the value
 * of the last matching rule that sets it, so a later `text` and an earlier
 * `eol` combine. A path git treats as binary (`-text`) needs no ending. A rule
 * that sets `eol` without `text` is rejected because git ignores it.
 *
 * Only the glob subset the two files use is supported: `*`, `?`, and `{a,b}`
 * alternation, where a pattern without a slash matches a basename at any depth.
 *
 * `--verbose` prints the resolved canonical ending and the compared paths. Exit
 * codes: 0 = in sync, 1 = drift, a malformed rule, or an unreadable input.
 *
 * `LINE_ENDINGS_EDITORCONFIG` and `LINE_ENDINGS_GITATTRIBUTES` override the
 * inputs so the self-test can point at a fixture tree. Nothing in CI sets them.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");

const EDITORCONFIG = process.env.LINE_ENDINGS_EDITORCONFIG
    ? path.resolve(REPO_ROOT, process.env.LINE_ENDINGS_EDITORCONFIG)
    : path.join(REPO_ROOT, ".editorconfig");
const GITATTRIBUTES = process.env.LINE_ENDINGS_GITATTRIBUTES
    ? path.resolve(REPO_ROOT, process.env.LINE_ENDINGS_GITATTRIBUTES)
    : path.join(REPO_ROOT, ".gitattributes");

const ENDING_NAMES = new Set(["lf", "crlf"]);
const DEFAULT_GLOB = "*";
const SAMPLE_NAME = "sample";

function readFileOrThrow(filePath, purpose) {
    try {
        return fs.readFileSync(filePath, "utf8");
    } catch {
        throw new Error(`Cannot ${purpose}: '${filePath}' is missing or unreadable.`);
    }
}

/*
    A repo-relative path when the file lives inside the repository, otherwise the
    absolute path, so a message never shows a '../../..' chain.
*/
function displayPath(filePath) {
    const relative = path.relative(REPO_ROOT, filePath).split(path.sep).join("/");
    return relative.startsWith("../") ? filePath : relative;
}

/*
    Splits one `{a,b,c}` glob into its alternatives. Nesting is not used by
    either file, so an inner brace stays literal instead of being expanded.
*/
function expandBraces(glob) {
    const open = glob.indexOf("{");
    const close = glob.indexOf("}", open + 1);
    if (open < 0 || close < 0) {
        return [glob];
    }
    const prefix = glob.slice(0, open);
    const suffix = glob.slice(close + 1);
    return glob
        .slice(open + 1, close)
        .split(",")
        .flatMap((alternative) => expandBraces(prefix + alternative + suffix));
}

function compileGlob(glob) {
    let source = "^";
    for (const character of glob) {
        if (character === "*") {
            source += "[^/]*";
        } else if (character === "?") {
            source += "[^/]";
        } else {
            source += character.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
        }
    }
    return new RegExp(`${source}$`);
}

function globMatches(glob, filePath) {
    for (const alternative of expandBraces(glob)) {
        const expression = compileGlob(alternative);
        if (expression.test(filePath)) {
            return true;
        }
        if (!alternative.includes("/") && expression.test(path.posix.basename(filePath))) {
            return true;
        }
    }
    return false;
}

/*
    A representative path for a glob, so each rule contributes the kind of file
    it governs to the comparison: `*.cs` becomes `sample.cs`, a literal path
    stays itself, and a bare `*` becomes an extensionless sample.
*/
function samplePath(glob) {
    const widest = expandBraces(glob)[0];
    return widest.includes("*") ? widest.replace(/\*/g, SAMPLE_NAME) : widest;
}

function normalizeEnding(value, origin) {
    const ending = value.trim().toLowerCase();
    if (!ENDING_NAMES.has(ending)) {
        throw new Error(`${origin} names the unknown line ending '${value}'; use lf or crlf.`);
    }
    return ending;
}

function parseEditorConfig(filePath) {
    const sections = [];
    let current = null;
    const lines = readFileOrThrow(filePath, "read the editor configuration").split("\n");
    for (const [index, raw] of lines.entries()) {
        const line = raw.trim();
        if (line.length === 0 || line.startsWith("#") || line.startsWith(";")) {
            continue;
        }
        const lineNumber = index + 1;
        if (line.startsWith("[") && line.endsWith("]")) {
            current = { glob: line.slice(1, -1).trim(), lineNumber, properties: new Map() };
            sections.push(current);
            continue;
        }
        const separator = line.indexOf("=");
        if (separator < 0) {
            throw new Error(`${displayPath(filePath)}:${lineNumber} is not a 'key = value' line.`);
        }
        // A property before the first section header is a preamble such as
        // `root = true`; it configures the file rather than any path.
        if (current !== null) {
            current.properties.set(
                line.slice(0, separator).trim().toLowerCase(),
                line.slice(separator + 1),
            );
        }
    }
    if (!sections.some((section) => section.glob === DEFAULT_GLOB)) {
        throw new Error(
            `${displayPath(filePath)} must carry a '[${DEFAULT_GLOB}]' section so every file has a ` +
                "declared line ending.",
        );
    }
    return sections;
}

/*
    The last matching section that sets `end_of_line` wins, which is how
    `.editorconfig` resolves precedence.
*/
function resolveEditorEnding(sections, filePath, origin) {
    let resolved = null;
    for (const section of sections) {
        const value = section.properties.get("end_of_line");
        if (value === undefined || !globMatches(section.glob, filePath)) {
            continue;
        }
        resolved = {
            ending: normalizeEnding(value, `${displayPath(origin)}:${section.lineNumber}`),
            lineNumber: section.lineNumber,
        };
    }
    return resolved;
}

function parseGitAttributes(filePath) {
    const rules = [];
    const lines = readFileOrThrow(filePath, "read the git attributes").split("\n");
    for (const [index, raw] of lines.entries()) {
        const line = raw.trim();
        if (line.length === 0 || line.startsWith("#")) {
            continue;
        }
        const fields = line.split(/\s+/);
        const attributes = new Map();
        for (const field of fields.slice(1)) {
            const separator = field.indexOf("=");
            if (separator < 0) {
                // `-text` unsets an attribute and `!text` resets it to git's
                // default; neither is a boolean `false` written after '='.
                attributes.set(field.replace(/^[-!]/, ""), field.startsWith("-") ? false : field.startsWith("!") ? "unset" : true);
            } else {
                attributes.set(field.slice(0, separator), field.slice(separator + 1));
            }
        }
        rules.push({ pattern: fields[0], lineNumber: index + 1, attributes });
    }
    return rules;
}

/*
    Git resolves each attribute from the last matching rule that sets it, so the
    `text` and `eol` answers can come from different rules. A binary path needs
    no ending, which is reported as no ending at all.
*/
function resolveGitEnding(rules, filePath, origin) {
    let text = { value: undefined, lineNumber: 0 };
    let ending = { value: undefined, lineNumber: 0 };
    let matched = { lineNumber: 0 };
    for (const rule of rules) {
        if (!globMatches(rule.pattern, filePath)) {
            continue;
        }
        matched = rule;
        if (rule.attributes.has("text")) {
            text = { value: rule.attributes.get("text"), lineNumber: rule.lineNumber };
        }
        if (rule.attributes.has("eol")) {
            ending = { value: rule.attributes.get("eol"), lineNumber: rule.lineNumber };
        }
    }
    if (text.value === false || text.value === "unset") {
        return { binary: true, ending: null, lineNumber: matched.lineNumber };
    }
    if (ending.value === undefined) {
        return { binary: false, ending: null, lineNumber: matched.lineNumber };
    }
    return {
        binary: false,
        ending: normalizeEnding(ending.value, `${displayPath(origin)}:${matched.lineNumber}`),
        lineNumber: matched.lineNumber,
    };
}

function main() {
    const verbose = process.argv.includes("--verbose");
    const sections = parseEditorConfig(EDITORCONFIG);
    const rules = parseGitAttributes(GITATTRIBUTES);
    const editorName = displayPath(EDITORCONFIG);
    const gitName = displayPath(GITATTRIBUTES);
    const failures = [];

    const baseRule = rules.find((rule) => rule.pattern === DEFAULT_GLOB);
    if (baseRule === undefined) {
        failures.push(
            `'${gitName}' has no '${DEFAULT_GLOB}' rule, so no path has a declared checkout ending.`,
        );
    } else if (baseRule.attributes.get("text") === false || baseRule.attributes.get("text") === "unset") {
        failures.push(`'${gitName}:${baseRule.lineNumber}' marks every path binary.`);
    }

    for (const rule of rules) {
        const origin = `${gitName}:${rule.lineNumber}`;
        const text = rule.attributes.get("text");
        if (text === false || text === "unset") {
            if (rule.attributes.has("eol")) {
                failures.push(
                    `${origin} marks '${rule.pattern}' binary and also sets an ending; drop the ending.`,
                );
            }
            continue;
        }
        if (rule.attributes.has("eol") && !rule.attributes.has("text")) {
            failures.push(
                `${origin} sets an ending for '${rule.pattern}' without 'text'; git ignores it.`,
            );
        }
    }

    const canonical = resolveEditorEnding(sections, SAMPLE_NAME, EDITORCONFIG);
    if (canonical === null) {
        throw new Error(
            `${editorName} sets no 'end_of_line' for '[${DEFAULT_GLOB}]'; CSharpier and the git ` +
                "checkout would each pick their own default.",
        );
    }

    const candidates = [];
    for (const section of sections) {
        if (section.properties.has("end_of_line")) {
            candidates.push(samplePath(section.glob));
        }
    }
    for (const rule of rules) {
        candidates.push(samplePath(rule.pattern));
    }

    for (const candidate of [...new Set(candidates)]) {
        const editor = resolveEditorEnding(sections, candidate, EDITORCONFIG);
        const git = resolveGitEnding(rules, candidate, GITATTRIBUTES);
        if (git.binary || editor === null) {
            continue;
        }
        if (git.ending === null) {
            failures.push(
                `'${candidate}': git leaves the checkout ending to the platform ` +
                    `('${gitName}:${git.lineNumber}' sets no ending) but ` +
                    `'${editorName}:${editor.lineNumber}' requires ${editor.ending}.`,
            );
            continue;
        }
        if (git.ending !== editor.ending) {
            failures.push(
                `'${candidate}': git checks out ${git.ending} ('${gitName}:${git.lineNumber}') but ` +
                    `'${editorName}:${editor.lineNumber}' requires ${editor.ending}.`,
            );
        }
    }

    if (failures.length > 0) {
        console.error(
            `The line-ending contract is broken. CSharpier follows '${editorName}'; git follows ` +
                `'${gitName}'.`,
        );
        for (const failure of failures) {
            console.error(`  ${failure}`);
        }
        process.exit(1);
    }

    if (verbose) {
        const textRules = rules.filter((rule) => rule.attributes.has("text") || rule.attributes.has("eol"))
            .length;
        console.log(
            `Line-ending contract in sync: ${canonical.ending} across ` +
                `${new Set(candidates).size} path(s) and ${textRules} rule(s) in '${gitName}'.`,
        );
    }
    process.exit(0);
}

try {
    main();
} catch (error) {
    console.error(`error: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
}