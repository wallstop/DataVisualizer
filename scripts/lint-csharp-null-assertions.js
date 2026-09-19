#!/usr/bin/env node
/**
 * C# source policy enforcement: null comparisons on UnityEngine.Object must use the
 * explicit `== null` / `!= null` operators (issue #117).
 *
 * NUnit's Assert.IsNull / Assert.IsNotNull and the equivalent `Is.Null` /
 * `Is.Not.Null` constraints compare with object reference semantics, so a destroyed
 * UnityEngine.Object wrapper (Unity's "fake null") is reported as not-null. The same
 * applies to `?.`, `??`, and implicit bool tests on UnityEngine.Object. This lint bans
 * the mechanically detectable forms - the Assert.Is(Not)Null calls and the Is.Null /
 * Is.Not.Null constraints - so every null assertion in the repository is written as
 * `Assert.That(value == null)` or `Assert.That(value != null)`, which binds Unity's
 * overloaded operators and matches the lifetime the test means.
 *
 * Comments and string literals are masked before scanning, so prose or fixture text that
 * mentions the banned forms cannot fail the scan. Interpolated strings are masked whole,
 * which can only hide a violation inside a hole, never fabricate one.
 *
 * `--verbose` also prints the scanned file count when clean. Exit codes: 0 = clean,
 * 1 = at least one banned assertion remains.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");

// Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.CSHARP_NULL_ASSERTION_ROOTS
  ? process.env.CSHARP_NULL_ASSERTION_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor", "Tests", "Generator~"];

// Vendored upstream verbatim; the other C# lint excludes it for the same reason.
const EXCLUDED_PREFIXES = ["Runtime/Utils/SevenZip"];

const BANNED_ASSERTION = /\bAssert\.Is(?:Not)?Null\s*\(/g;
const BANNED_CONSTRAINT = /\bIs\.Not\.Null\b|\bIs\.Null\b/g;

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

function main() {
  const verbose = process.argv.includes("--verbose");
  const violations = [];
  let scanned = 0;
  for (const root of SCAN_ROOTS) {
    for (const file of listCSharpFiles(root)) {
      if (EXCLUDED_PREFIXES.some((prefix) => file.display.startsWith(prefix))) {
        continue;
      }
      scanned++;
      const text = fs.readFileSync(file.fullPath, "utf8");
      const masked = maskNoise(text);
      const lines = masked.split("\n");
      for (let lineIndex = 0; lineIndex < lines.length; lineIndex++) {
        BANNED_ASSERTION.lastIndex = 0;
        BANNED_CONSTRAINT.lastIndex = 0;
        if (BANNED_ASSERTION.test(lines[lineIndex]) || BANNED_CONSTRAINT.test(lines[lineIndex])) {
          violations.push(`${file.display}:${lineIndex + 1}`);
        }
      }
    }
  }
  if (violations.length > 0) {
    console.error(
      "Banned null assertions found. Use Assert.That(value == null) or " +
        "Assert.That(value != null) so destroyed UnityEngine.Object wrappers compare " +
        "with Unity's overloaded operators; Assert.IsNull/IsNotNull and the Is.Null/" +
        "Is.Not.Null constraints compare with object reference semantics and miss that " +
        "lifetime (issue #117):"
    );
    for (const violation of violations) {
      console.error(`  ${violation}`);
    }
    console.error(`\n${violations.length} banned assertion(s) in ${SCAN_ROOTS.join(", ")}.`);
    process.exit(1);
  }
  if (verbose) {
    console.log(`Null-assertion policy clean: ${scanned} file(s) scanned.`);
  }
  process.exit(0);
}

main();
