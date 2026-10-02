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
 * `.gitattributes` rule, and one per `{a,b}` alternative of each, because a
 * pattern whose later alternatives are never compared hides the drift behind
 * them.
 *
 * Resolution follows git's own attribute model: each attribute takes the value
 * of the last matching rule that sets it, so a later `text` and an earlier
 * `eol` combine. A path git treats as binary (`-text`) needs no ending. A rule
 * that sets `eol` without `text` is rejected because git ignores it.
 *
 * Only the glob subset the two files use is supported: `*`, `?`, and `{a,b}`
 * alternation, where a pattern without a slash matches a basename at any depth.
 * A recursive `**`, an anchored or directory-only `/`, a character class, and a
 * backslash escape are refused instead of guessed, because a matcher that reads
 * one as literal text silently stops matching and would hide the drift behind
 * the rule.
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
        // Every line is trimmed before it is parsed, and trimming already drops
        // the byte-order mark some editors put on line one.
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
    Splits one `{a,b,c}` glob into every alternative, including nested groups.
    Splitting on the first `}` would leave a nested group as literal text and
    then match nothing, so the closing brace is found by depth.
*/
function expandBraces(glob) {
    const open = glob.indexOf("{");
    if (open < 0) {
        return [glob];
    }
    let depth = 0;
    let close = -1;
    for (let index = open; index < glob.length; index++) {
        if (glob[index] === "{") {
            depth++;
        } else if (glob[index] === "}") {
            depth--;
            if (depth === 0) {
                close = index;
                break;
            }
        }
    }
    if (close < 0) {
        return [glob];
    }
    const prefix = glob.slice(0, open);
    const suffix = glob.slice(close + 1);
    const alternatives = [];
    let start = open + 1;
    depth = 0;
    for (let index = open + 1; index <= close; index++) {
        const character = glob[index];
        if (character === "{") {
            depth++;
        } else if (character === "}") {
            depth--;
        } else if (character === "," && depth === 0) {
            alternatives.push(glob.slice(start, index));
            start = index + 1;
        }
    }
    alternatives.push(glob.slice(start, close));
    return alternatives.flatMap((alternative) =>
        expandBraces(`${prefix}${alternative}${suffix}`),
    );
}

/*
    Glob constructs this matcher cannot resolve exactly. A pattern that uses one
    would be read as literal text and would silently stop matching the files git
    or `.editorconfig` will actually apply it to, so it is refused instead.
*/
const UNSUPPORTED_GLOBS = [
    { label: "a recursive '**' segment", matches: (glob) => glob.includes("**") },
    { label: "an anchored or directory-only '/'", matches: (glob) => glob.startsWith("/") || glob.endsWith("/") },
    { label: "a character class", matches: (glob) => glob.includes("[") },
    { label: "a backslash escape", matches: (glob) => glob.includes("\\") },
];

function refuseUnsupportedGlob(glob, origin) {
    const unsupported = UNSUPPORTED_GLOBS.find((candidate) => candidate.matches(glob));
    if (unsupported !== undefined) {
        throw new Error(
            `${origin} uses ${unsupported.label} in '${glob}', which this check cannot match.`,
        );
    }
}

function compileGlob(glob, origin) {
    refuseUnsupportedGlob(glob, origin);
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

function globMatches(glob, filePath, origin) {
    for (const alternative of expandBraces(glob)) {
        const expression = compileGlob(alternative, origin);
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
    One representative path per brace alternative, so every file a glob governs
    reaches the comparison: `*.cs` becomes `sample.cs`, `{*.md,*.ps1}` becomes
    both `sample.md` and `sample.ps1`, a literal path stays itself, and a bare `*`
    becomes an extensionless sample. Taking only the first alternative would leave
    the rest uncompared and hide the drift behind them.
*/
function samplePaths(glob, origin) {
    return expandBraces(glob).map((alternative) => {
        refuseUnsupportedGlob(alternative, origin);
        return alternative.includes("*") ? alternative.replace(/\*/g, SAMPLE_NAME) : alternative;
    });
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
function resolveEditorEnding(sections, filePath, source) {
    let resolved = null;
    for (const section of sections) {
        const value = section.properties.get("end_of_line");
        const origin = `${displayPath(source)}:${section.lineNumber}`;
        if (value === undefined || !globMatches(section.glob, filePath, origin)) {
            continue;
        }
        resolved = {
            ending: normalizeEnding(value, origin),
            lineNumber: section.lineNumber,
        };
    }
    return resolved;
}

/*
    One `.gitattributes` attribute token. `-name` unsets an attribute, `!name`
    resets it to git's default, and a bare `name` sets it; neither leading mark is
    a boolean `false` written after '='.
*/
function parseAttribute(field) {
    const separator = field.indexOf("=");
    if (separator >= 0) {
        return [field.slice(0, separator), field.slice(separator + 1)];
    }
    if (field.startsWith("-")) {
        return [field.slice(1), false];
    }
    if (field.startsWith("!")) {
        return [field.slice(1), "unset"];
    }
    return [field, true];
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
            const [name, value] = parseAttribute(field);
            attributes.set(name, value);
        }
        // `binary` is git's macro for `-diff -merge -text`, so it decides the
        // ending the same way an explicit `-text` does.
        if (attributes.has("binary")) {
            attributes.set("text", false);
        }
        rules.push({ pattern: fields[0], lineNumber: index + 1, attributes });
    }
    return rules;
}

/*
    Git resolves each attribute from the last matching rule that sets it, so the
    `text` and `eol` answers can come from different rules. A binary path needs
    no ending at all and is reported as such.
*/
function resolveGitEnding(rules, filePath, source) {
    let isBinary = false;
    let ending;
    let matched = 0;
    for (const rule of rules) {
        if (!globMatches(rule.pattern, filePath, `${displayPath(source)}:${rule.lineNumber}`)) {
            continue;
        }
        matched = rule.lineNumber;
        if (rule.attributes.has("text")) {
            const value = rule.attributes.get("text");
            isBinary = value === false || value === "unset";
        }
        if (rule.attributes.has("eol")) {
            ending = { value: rule.attributes.get("eol"), lineNumber: rule.lineNumber };
        }
    }
    if (isBinary) {
        return { binary: true, ending: null, lineNumber: matched };
    }
    if (ending === undefined) {
        return { binary: false, ending: null, lineNumber: matched };
    }
    return {
        binary: false,
        ending: normalizeEnding(ending.value, `${displayPath(source)}:${ending.lineNumber}`),
        lineNumber: matched,
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
            candidates.push(...samplePaths(section.glob, editorName));
        }
    }
    for (const rule of rules) {
        candidates.push(...samplePaths(rule.pattern, `${gitName}:${rule.lineNumber}`));
    }
    const compared = [...new Set(candidates)];

    for (const candidate of compared) {
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
        const endingRules = rules.filter(
            (rule) => rule.attributes.has("text") || rule.attributes.has("eol"),
        ).length;
        console.log(
            `Line-ending contract in sync: ${canonical.ending} across ${compared.length} path(s) ` +
                `and ${endingRules} rule(s) in '${gitName}'.`,
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