#!/usr/bin/env node
/**
 * C# source policy enforcement: `using` directives live inside the namespace (issue #124).
 *
 * Guidance rule 1 mandates block-scoped namespaces with `using` directives inside them,
 * but nothing failed CI when a file drifted. This lint reports every `using` directive
 * that appears before the `namespace` declaration. Two file-level shapes stay sanctioned
 * because the language requires them:
 *
 * - Files without any namespace (assembly-attribute files such as
 *   Runtime/AnalyzerSuppressions.cs) keep their file-level usings.
 * - Files whose pre-namespace region carries an `[assembly: ...]` attribute (the
 *   `InternalsVisibleTo` preamble in Runtime/DataVisualizer/BaseDataObject.cs) need
 *   those usings at file level; the region is sanctioned as a whole. `global using`
 *   directives are likewise legal only at file level and are never reported.
 *
 * Comments and string literals are masked before scanning, so prose or fixture text that
 * mentions a misplaced `using` cannot fail the scan.
 *
 * `--fix` moves reportable usings inside the namespace's opening brace, preserving order.
 * It declines files whose pre-namespace region holds anything besides comments, usings,
 * and blank lines (for example `#if` blocks), leaving those as manual barriers.
 * `--verbose` also prints the scanned file count when clean. Exit codes: 0 = clean (or
 * fixed clean), 1 = at least one misplaced using remains.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");

// Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.CSHARP_USINGS_ROOTS
  ? process.env.CSHARP_USINGS_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor", "Tests", "Generator~"];

// Vendored upstream verbatim; the other C# lint excludes it for the same reason.
const EXCLUDED_PREFIXES = ["Runtime/Utils/SevenZip"];

const NAMESPACE_LINE = /^\s*namespace\s/;
const USING_LINE = /^\s*using\s(?!.*\bglobal\b)/;
const ASSEMBLY_ATTRIBUTE = /\[\s*assembly\s*:/;
const OPEN_BRACE_LINE = /^\s*\{/;

function maskNoise(text) {
  const out = text.split("");
  const blank = (start, end) => {
    for (let index = start; index < end; index++) {
      if (out[index] !== "\n") {
        out[index] = " ";
      }
    }
  };
  let index = 0;
  while (index < text.length) {
    const char = text[index];
    const next = text[index + 1];
    if (char === "/" && next === "/") {
      const end = text.indexOf("\n", index);
      blank(index, end < 0 ? text.length : end);
      index = end < 0 ? text.length : end + 1;
      continue;
    }
    if (char === "/" && next === "*") {
      const end = text.indexOf("*/", index + 2);
      const close = end < 0 ? text.length : end + 2;
      blank(index, close);
      index = close;
      continue;
    }
    if (char === '"' || (char === "@" && next === '"') || (char === "$" && next === '"')) {
      const verbatim = char === "@";
      const quote = verbatim || char === "$" ? index + 1 : index;
      let cursor = quote + 1;
      while (cursor < text.length) {
        if (verbatim) {
          if (text[cursor] === '"' && text[cursor + 1] === '"') {
            cursor += 2;
            continue;
          }
          if (text[cursor] === '"') {
            break;
          }
          cursor++;
          continue;
        }
        if (text[cursor] === "\\") {
          cursor += 2;
          continue;
        }
        if (text[cursor] === '"') {
          break;
        }
        cursor++;
      }
      blank(index, Math.min(cursor + 1, text.length));
      index = Math.min(cursor + 1, text.length);
      continue;
    }
    index++;
  }
  return out.join("");
}

function listCSharpFiles(root) {
  const files = [];
  const absoluteRoot = path.isAbsolute(root) ? root : path.join(REPO_ROOT, root);
  if (!fs.existsSync(absoluteRoot)) {
    return files;
  }
  const stack = [absoluteRoot];
  while (stack.length > 0) {
    const current = stack.pop();
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const fullPath = path.join(current, entry.name);
      if (entry.isDirectory()) {
        stack.push(fullPath);
        continue;
      }
      if (entry.isFile() && entry.name.endsWith(".cs")) {
        const relative = path.relative(REPO_ROOT, fullPath).split(path.sep).join("/");
        const display = relative.startsWith("..")
          ? fullPath.split(path.sep).join("/")
          : relative;
        files.push({ fullPath, display });
      }
    }
  }
  return files.sort((left, right) => (left.display < right.display ? -1 : 1));
}

/*
    Returns the indexes of misplaced using directives, or null when the file's shape is
    sanctioned: no namespace at all, or an assembly attribute in the pre-namespace region.
*/
function findMisplacedUsingLines(maskedLines) {
  const namespaceIndex = maskedLines.findIndex((line) => NAMESPACE_LINE.test(line));
  if (namespaceIndex < 0) {
    return null;
  }

  const region = maskedLines.slice(0, namespaceIndex);
  const misplaced = [];
  let hasAssemblyAttribute = false;
  for (let index = 0; index < region.length; index++) {
    if (ASSEMBLY_ATTRIBUTE.test(region[index])) {
      hasAssemblyAttribute = true;
      break;
    }
    if (USING_LINE.test(region[index])) {
      misplaced.push(index);
    }
  }
  return hasAssemblyAttribute ? null : misplaced;
}

/*
    Moves the misplaced usings after the namespace's opening brace. Returns the fixed
    text, or null when the region holds a manual barrier (anything besides comments,
    usings, and blank lines) or the namespace brace cannot be located.
*/
function fixFile(text, maskedLines) {
  const namespaceIndex = maskedLines.findIndex((line) => NAMESPACE_LINE.test(line));
  if (namespaceIndex < 0) {
    return null;
  }

  const lines = text.split("\n");
  const usingIndexes = [];
  for (let index = 0; index < namespaceIndex; index++) {
    const masked = maskedLines[index];
    if (USING_LINE.test(masked)) {
      usingIndexes.push(index);
      continue;
    }
    // Masked-blank lines are comments, blank lines, or strings: safe to leave in place.
    if (masked.trim() === "") {
      continue;
    }
    return null;
  }
  if (usingIndexes.length === 0) {
    return null;
  }

  let braceIndex = -1;
  for (let index = namespaceIndex; index < lines.length; index++) {
    if (OPEN_BRACE_LINE.test(maskedLines[index])) {
      braceIndex = index;
      break;
    }
  }
  if (braceIndex < 0) {
    return null;
  }

  const usings = usingIndexes.map((index) => "    " + lines[index].trim());
  const kept = lines.filter((_, index) => !usingIndexes.includes(index));
  const braceShift = braceIndex - usingIndexes.filter((index) => index < braceIndex).length;
  kept.splice(braceShift + 1, 0, ...usings);
  return kept.join("\n");
}

function main() {
  const fix = process.argv.includes("--fix");
  const verbose = process.argv.includes("--verbose");
  const violations = [];
  const fixes = [];
  let scanned = 0;
  for (const root of SCAN_ROOTS) {
    for (const file of listCSharpFiles(root)) {
      if (EXCLUDED_PREFIXES.some((prefix) => file.display.startsWith(prefix))) {
        continue;
      }
      scanned++;
      const text = fs.readFileSync(file.fullPath, "utf8");
      const maskedLines = maskNoise(text).split("\n");
      const misplaced = findMisplacedUsingLines(maskedLines);
      if (misplaced === null || misplaced.length === 0) {
        continue;
      }
      if (fix) {
        const fixed = fixFile(text, maskedLines);
        if (fixed !== null) {
          fs.writeFileSync(file.fullPath, fixed, "utf8");
          fixes.push(file.display);
          continue;
        }
        violations.push(`${file.display}:${misplaced[0] + 1} (manual barrier; fix declined)`);
        continue;
      }
      for (const index of misplaced) {
        violations.push(`${file.display}:${index + 1}`);
      }
    }
  }
  if (fix && fixes.length > 0) {
    for (const fixed of fixes) {
      console.log(`Moved usings inside the namespace: ${fixed}`);
    }
  }
  if (violations.length > 0) {
    console.error(
      "Usings found outside the namespace. Move every `using` directive inside the " +
        "namespace block (guidance rule 1). File-level usings stay sanctioned only for " +
        "namespaceless assembly-attribute files and for `[assembly: ...]` preambles such " +
        "as InternalsVisibleTo (issue #124):"
    );
    for (const violation of violations) {
      console.error(`  ${violation}`);
    }
    console.error(`\n${violations.length} misplaced using(s) in ${SCAN_ROOTS.join(", ")}.`);
    process.exit(1);
  }
  if (verbose) {
    const suffix = fixes.length > 0 ? `; ${fixes.length} file(s) fixed` : "";
    console.log(`Using-placement policy clean: ${scanned} file(s) scanned${suffix}.`);
  }
  process.exit(0);
}

main();
