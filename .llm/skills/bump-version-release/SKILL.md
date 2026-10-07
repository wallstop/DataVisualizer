---
name: bump-version-release
description: Bump the package version and publish com.wallstop-studios.data-visualizer to npm - version sync into .llm/context.md, tarball verification, commit message format, and dist-tag rules for the trusted-publishing workflow. Use when cutting any release.
metadata:
  category: Workflow
---

# Skill: Bump Version / Release

Owner setup for the release chain (GitHub PR permissions, npm Trusted Publisher
fields, rerun and recovery) is in
[releasing](../../references/RELEASING.md). Read it before the first release of
a clone or after a partial failure.

## Steps

1. Add user-facing entries under `CHANGELOG.md` `## [Unreleased]`.
2. Dispatch `.github/workflows/release.yml` on `main` with `operation=prepare`.
   Choose `bump` or `explicit_version`; use the default `dry_run=true` first.
3. Set `dry_run=false` to open the release PR. The workflow bumps `package.json`,
   syncs `.llm/context.md`, dates the changelog, and validates the artifacts.
4. Merge the validated `release/vX.Y.Z` PR. The same release workflow tags the
   merged commit, validates both packages and notes, then publishes npm and the
   GitHub Release. The artifacts must pass before publication.
5. Recover a partial release by dispatching `release.yml` with
   `operation=publish`, `tag=vX.Y.Z`, and `dry_run=false`. A dry run validates
   that existing tag and retains artifacts without publishing.

For a local version bump, edit `package.json`, run `npm run lint:llm:fix`, and
verify `npm pack`. Commit `package.json`, `.llm/context.md`, and the dated
changelog together with subject `Bump version from X to Y`. Do not run automated
preparation on a version that has already been bumped locally.

## Changelog

- `CHANGELOG.md` is user-facing only: features, fixes, visible behavior. No
  tests, CI, tooling, or release mechanics. Write plain Simplified Technical
  English. The GitHub Release notes are extracted from the released section,
  so keep that section's copy release-ready.

## Dist-Tag Rules

- Versions matching `-rc`, `-alpha`, `-beta`, `-preview` (case-insensitive) publish
  under the `next` dist-tag; everything else under `latest`.
- Publishing uses npm Trusted Publishing through GitHub OIDC (`id-token: write`);
  no NPM_TOKEN secret is involved.

## Safety

- The publish step is re-runnable: it skips a name@version already on the registry.
- npm publish is otherwise irreversible - never publish with local uncommitted
  changes to shipped assets, and always dry-run first.

## Rollback

Cannot unpublish reliably; cut a patch bump and publish that instead.

## Related Skills

- [ship-changes](./ship-changes/SKILL.md) - the general validation ladder
- [manage-skills](./manage-skills/SKILL.md) - why the version line lives in
  `.llm/context.md`
