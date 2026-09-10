---
name: ship-changes
description: Validate and ship changes in the Data Visualizer package - CSharpier formatting, .llm harness linters, npm pack, Unity EditMode/PlayMode suites, commit subjects, and PR handoff requirements. Use before committing, pushing, or opening a pull request.
metadata:
  category: Core
---

# Skill: Ship Changes

## Validation Ladder

Run the narrowest sufficient layer; escalate on failure:

| Layer | Scope | Command |
| --- | --- | --- |
| Pre-commit hook | staged files | automatic (`pre-commit` + `.pre-commit-config.yaml`) |
| Local gate | whole harness | `npm run lint:llm` |
| Harness self-tests | scripts | `pwsh -NoProfile -File scripts/tests/run-all.ps1` |
| Packaging | tarball integrity | `npm pack` |
| Unity suites | behavior | EditMode then PlayMode batch commands |
| CI | everything, ubuntu + windows | `.github/workflows/llm-lint.yml` |

## Pre-Commit Checklist

1. Changed `.cs` files: `dotnet tool run csharpier -- format <paths>` (the hook runs
   it automatically on staged C# files; verify with `-- check`).
2. If any `.llm/**` file changed: `npm run lint:llm` (regenerates nothing; use
   `npm run lint:llm:fix` to auto-fix index drift and version sync, then review the
   diff and re-stage).
3. `pwsh -NoProfile -File scripts/tests/run-all.ps1` when `scripts/**` changed.
4. `npm pack` when `package.json`, `files`, or shipped assets changed; confirm the
   tarball contains only `Editor`, `Runtime`, `Tests`, `docs` payloads.
5. Unity EditMode tests; PlayMode tests when runtime lifecycle or persistence
   changed.

## Commits

- Short imperative subject with the subsystem up front, matching history style:
  "Fix settings persistence dirty state", "Bump version from 0.0.36 to 0.0.37".
- Reference related issue IDs in the body.
- Never commit generated `.llm/skills/index.md` without its source skills, and never
  commit a stale index.

## Pull Requests

Include: behavior summary, reproduction/validation steps, screenshots or GIFs for UI
tweaks, and a risk callout plus rollback plan. Confirm CSharpier, `npm pack`, and both
Unity test suites before handoff.

Never explicitly request a review from a person, team, bot, or automation. Do not
mention a reviewer in a comment, call a reviewer-request API, or otherwise trigger a
review. Monitor and address reviews that arrive through the repository's existing
configuration or that another participant submits independently.

## Related Skills

- [manage-skills](./manage-skills/SKILL.md) - how to keep the harness itself valid
- [bump-version-release](./bump-version-release/SKILL.md) - release-specific flow
