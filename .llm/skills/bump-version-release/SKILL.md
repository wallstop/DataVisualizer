---
name: bump-version-release
description: Bump the package version and publish com.wallstop-studios.data-visualizer to npm - version sync into .llm/context.md, tarball verification, commit message format, and dist-tag rules for the trusted-publishing workflow. Use when cutting any release.
metadata:
  category: Workflow
---

# Skill: Bump Version / Release

## Steps

1. Edit `package.json` `version` (semver; append `-rc`/`-alpha`/`-beta`/`-preview`
   for pre-releases).
2. Sync the harness: `npm run lint:llm:fix` updates the `**Version**:` line in
   `.llm/context.md` to match; rerun `npm run lint:llm` to confirm green.
3. Verify the tarball: `npm pack` and inspect contents (must stay limited to the
   `Editor` and `Runtime` payloads and their `.meta` companions per the `files`
   whitelist).
4. Commit with the historical subject format: `Bump version from X to Y`, including
   `package.json` and `.llm/context.md` in the same commit. Keep the message in
   Simplified Technical English (see `.llm/context.md`).
5. Publish through the release chain: merge the release/vX.Y.Z pull request the
   release-prep workflow opened; the release-tag workflow creates the annotated
   tag, whose push triggers the tag-driven publish workflow
   (`.github/workflows/npm-publish.yml`). It verifies the tag against the package
   version and the packed allowlist before publishing. To rerun a partially
   completed release, dispatch `npm-publish.yml` with the `tag` input.

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
