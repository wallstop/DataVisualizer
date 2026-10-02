#!/usr/bin/env node
/**
 * Release credential contract: every secret the release workflows read must be
 * documented, and every credential the release reference documents must be read
 * by a release workflow.
 *
 * The release chain (prepare -> tag -> publish) is driven by workflow YAML that
 * no test executes, so its credential requirements drift silently. A roadmap
 * item once named `AUTO_COMMIT_APP_ID` and `AUTO_COMMIT_APP_PRIVATE_KEY` as the
 * credentials to configure; neither exists in the repository, the workflows read
 * a single `RELEASE_TOKEN`, and the resulting failure mode is invisible because
 * a `GITHUB_TOKEN`-pushed tag never starts the publish workflow while every job
 * still reports success. This check fails closed on that class of drift.
 *
 * The documented set is read from the `release-secrets` block in
 * `.llm/references/RELEASING.md`, so prose elsewhere in the reference is free to
 * mention credentials that are not required (for example "no NPM_TOKEN is
 * involved") without failing the scan.
 *
 * The release workflow set is the three workflows the reference documents. A
 * new release workflow must be added to RELEASE_WORKFLOWS and to the reference.
 *
 * `--verbose` prints the compared sets when clean. Exit codes: 0 = in sync,
 * 1 = drift, a missing or malformed documented block, or an unreadable input.
 *
 * `RELEASE_SECRETS_WORKFLOW_DIR` and `RELEASE_SECRETS_DOC` override the inputs
 * so the self-test can point at a fixture tree. Nothing in CI sets them.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");

// The workflows `.llm/references/RELEASING.md` documents. Kept explicit so the
// scan does not demand release documentation for unrelated workflows.
const RELEASE_WORKFLOWS = ["release-prep.yml", "release-tag.yml", "npm-publish.yml"];

// Provided by GitHub for every run; configuring it is not an owner task, so it
// is never part of the documented contract.
const BUILT_IN_SECRETS = new Set(["GITHUB_TOKEN"]);

const BLOCK_START = "<!-- release-secrets:begin -->";
const BLOCK_END = "<!-- release-secrets:end -->";

// Both documented GitHub Actions secret forms: `secrets.NAME` and
// `secrets['NAME']`. A computed index such as `secrets[format(...)]` cannot be
// resolved statically and is outside this check's coverage.
const SECRET_REFERENCE =
    /\bsecrets(?:\.([A-Za-z_][A-Za-z0-9_]*)|\[\s*['"]([A-Za-z_][A-Za-z0-9_]*)['"]\s*\])/g;
const DOCUMENTED_SECRET = /`([A-Z][A-Z0-9_]*)`/g;

const WORKFLOW_DIR = process.env.RELEASE_SECRETS_WORKFLOW_DIR
  ? path.resolve(REPO_ROOT, process.env.RELEASE_SECRETS_WORKFLOW_DIR)
  : path.join(REPO_ROOT, ".github", "workflows");
const REFERENCE_DOC = process.env.RELEASE_SECRETS_DOC
  ? path.resolve(REPO_ROOT, process.env.RELEASE_SECRETS_DOC)
  : path.join(REPO_ROOT, ".llm", "references", "RELEASING.md");

function readFileOrThrow(filePath, purpose) {
  try {
    return fs.readFileSync(filePath, "utf8");
  } catch {
    throw new Error(`Cannot ${purpose}: '${filePath}' is missing or unreadable.`);
  }
}

/*
    Blanks YAML comments so a commented-out secret reference cannot create a
    documented-set requirement. Quoted regions are preserved, because a '#'
    inside a string is data rather than a comment.
*/
function maskYamlComments(text) {
  const characters = text.split("");
  const blank = (start, end) => {
    for (let index = start; index < end; index++) {
      if (characters[index] !== "\n") {
        characters[index] = " ";
      }
    }
  };
  let index = 0;
  while (index < text.length) {
    const character = text[index];
    if (character === "'" || character === '"') {
      const quote = character;
      index++;
      while (index < text.length) {
        if (text[index] === "\\" && quote === '"') {
          index += 2;
          continue;
        }
        if (text[index] === quote) {
          if (text[index + 1] === quote) {
            index += 2;
            continue;
          }
          index++;
          break;
        }
        index++;
      }
      continue;
    }
    if (character === "#") {
      const end = text.indexOf("\n", index);
      const stop = end < 0 ? text.length : end;
      blank(index, stop);
      index = stop;
      continue;
    }
    index++;
  }
  return characters.join("");
}

function readWorkflowSecrets(filePath) {
  const masked = maskYamlComments(readFileOrThrow(filePath, "read the release workflow"));
  const secrets = new Set();
  for (const match of masked.matchAll(SECRET_REFERENCE)) {
    // Alternation group 1 is the dot form, group 2 the bracket form.
    const name = match[1] ?? match[2];
    if (!BUILT_IN_SECRETS.has(name)) {
      secrets.add(name);
    }
  }
  return secrets;
}

function readDocumentedSecrets(filePath) {
  const text = readFileOrThrow(filePath, "read the release reference");
  const start = text.indexOf(BLOCK_START);
  const end = text.indexOf(BLOCK_END);
  if (start < 0 || end < 0 || end < start) {
    throw new Error(
      `'${filePath}' must carry a '${BLOCK_START}' / '${BLOCK_END}' block listing the ` +
        "secrets the release workflows read; the documented set is parsed from it.",
    );
  }
  const block = text.slice(start + BLOCK_START.length, end);
  const secrets = new Set();
  for (const match of block.matchAll(DOCUMENTED_SECRET)) {
    secrets.add(match[1]);
  }
  if (secrets.size === 0) {
    throw new Error(
      `'${filePath}' has an empty release-secrets block; name each secret in ` +
        'backticks, for example `RELEASE_TOKEN`.',
    );
  }
  return secrets;
}

function sorted(set) {
  return [...set].sort();
}

/*
    A repo-relative path when the file lives inside the repository, otherwise the
    absolute path, so a message never shows a '../../..' chain.
*/
function displayPath(filePath) {
  const relative = path.relative(REPO_ROOT, filePath).split(path.sep).join("/");
  return relative.startsWith("../") ? filePath : relative;
}

function main() {
  const verbose = process.argv.includes("--verbose");
  const read = new Set();
  for (const workflow of RELEASE_WORKFLOWS) {
    for (const secret of readWorkflowSecrets(path.join(WORKFLOW_DIR, workflow))) {
      read.add(secret);
    }
  }
  const documented = readDocumentedSecrets(REFERENCE_DOC);

  const undocumented = sorted(read).filter((secret) => !documented.has(secret));
  const unused = sorted(documented).filter((secret) => !read.has(secret));

  if (undocumented.length > 0 || unused.length > 0) {
    console.error(
      "The release credential contract is out of sync. The release workflows read " +
        `${sorted(read).join(", ") || "no secret"}; ` +
        `'${displayPath(REFERENCE_DOC)}' documents ${sorted(documented).join(", ")}.`,
    );
    for (const secret of undocumented) {
      console.error(
        `  ${secret}: read by a release workflow but not documented. Add it to the ` +
          "release-secrets block with the scope an owner must grant.",
      );
    }
    for (const secret of unused) {
      console.error(
        `  ${secret}: documented but read by no release workflow. Remove it or point ` +
          "the workflow at the credential the owner actually configures.",
      );
    }
    process.exit(1);
  }

  if (verbose) {
    console.log(
      `Release credential contract in sync: ${sorted(read).length} secret(s) documented ` +
        `across ${RELEASE_WORKFLOWS.length} release workflow(s).`,
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
