---
name: ship-changes
description: Validate and ship changes in the DxVisualizer package - CSharpier formatting, .llm harness linters, npm pack, Unity EditMode/PlayMode suites, commit subjects, and PR handoff requirements. Use before committing, pushing, or opening a pull request.
metadata:
  category: Core
---

# Skill: Ship Changes

## Validation Ladder

Run the narrowest sufficient layer; escalate on failure:

| Layer | Scope | Command |
| --- | --- | --- |
| Fast check | changed files | `npm run check:fast` |
| Pre-commit hook | staged files | automatic (`pre-commit` + `.pre-commit-config.yaml`) |
| C# member layout | all C# types | `npm run lint:csharp-member-order` |
| Markdown formatting | tracked Markdown | `npm run format:md:check` |
| Local gate | whole harness | `npm run lint:llm` |
| Harness self-tests | scripts | `pwsh -NoProfile -File scripts/tests/run-all.ps1` |
| Packaging | tarball integrity | `npm pack` |
| Unity suites | behavior | EditMode then PlayMode batch commands |
| CI | everything, ubuntu + windows | `.github/workflows/llm-lint.yml` |

## Pre-Commit Checklist

1. Changed `.cs` files: `dotnet tool run csharpier -- format <paths>` (the hook runs
   it automatically on staged C# files; verify with `-- check`). Then run
   `npm run lint:csharp-member-order`; the `:fix` command safely permutes complete
   member slices, but conditional/static-initialization barriers require manual review.
1b. Changed `.md` files (except `.prettierignore`d skills): `npm run format:md`,
   verify with `npm run format:md:check`.
2. If any `.llm/**` file changed: `npm run lint:llm` (regenerates nothing; use
   `npm run lint:llm:fix` to auto-fix index drift and version sync, then review the
   diff and re-stage).
3. `pwsh -NoProfile -File scripts/tests/run-all.ps1` when `scripts/**` changed.
4. `npm pack` when `package.json`, `files`, or shipped assets changed; confirm the
   tarball contains only `Editor`, `Runtime`, and package metadata payloads. Docs
   imagery ships via GitHub, not npm; `docs/` and dev files must not appear.
5. Unity EditMode tests; PlayMode tests when runtime lifecycle or persistence
   changed.

## Commits

- Write commit messages in Simplified Technical English (see `.llm/context.md`):
  short imperative subject with the subsystem up front, plain body, no filler.
  Match history style: "Fix settings persistence dirty state", "Bump version from
  0.0.36 to 0.0.37".
- Reference related issue IDs in the body.
- Keep subjects at most 72 characters and make the body explain why first; each
  commit compiles and carries one logical change.
- Never commit generated `.llm/skills/index.md` without its source skills, and never
  commit a stale index.
- CHANGELOG entries are user-facing only (features, fixes, visible behavior).
  Tests, CI, tooling, and release mechanics go in commits and PR bodies, never
  in `CHANGELOG.md`. Entries are one or two sentences, at most 300 rendered
  characters (issue references and link targets do not count), starting with
  the verb its section names (`Add`, `Fix`), user-visible effect first;
  `npm run lint:changelog-length` enforces the cap on `[Unreleased]`. No
  root-cause narration, mechanism, file paths, or "verified on" notes; the
  long version lives in the commit body or a docs guide. Never modify released
  sections; edit `[Unreleased]` entries in place. A fix for a defect that was
  never in a release is not a `Fixed` entry: fold what the user gets into that
  feature's entry.

## Pull Requests

GitHub pre-fills the body from `.github/pull_request_template.md` (Why/What +
Type of Change + Checklist); keep that shape. Confirm CSharpier, `npm pack`,
and both Unity test suites before handoff.

Start agent-written bodies with the disclosure line; the template then reads:

```markdown
DISCLOSURE: LLM-GENERATED TEXT

**Why:** <the problem, in one sentence>

**What:**

- <one change, one line>
- <two to five bullets>

Fixes #<issue>
```

| Limit            | Value         |
| ---------------- | ------------- |
| Title            | 50 characters |
| `**Why:**`       | 1 sentence    |
| `**What:**`      | 2 to 5 lines  |
| Words per bullet | 12            |

Count the title (`printf '%s' "$TITLE" | wc -c`) instead of judging. Name the
effect the user sees, not the mechanism, and do not join two changes with
"and"; let the body carry the rest.

- The disclosure line is not part of any limit.
- No validation transcripts, measurements, run IDs, session numbers, CI
  results, risk or rollback essays, history, diff narration, or file lists;
  they live in the commit body, tests, and linked issues.
- The template's Type of Change and Checklist sections are the only allowed
  headers beyond Why/What.
- UI tweaks add one screenshot or GIF after the bullets.

Never explicitly request a review from a person, team, bot, or automation. Do not
mention a reviewer in a comment, call a reviewer-request API, or otherwise trigger a
review. Monitor and address reviews that arrive through the repository's existing
configuration or that another participant submits independently.

## Related Skills

- [manage-skills](./manage-skills/SKILL.md) - how to keep the harness itself valid
- [bump-version-release](./bump-version-release/SKILL.md) - release-specific flow
