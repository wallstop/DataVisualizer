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
3. Verify the tarball: `npm pack` and inspect contents (must stay limited to
   `Editor`, `Runtime`, and `docs` payloads per the `files` whitelist).
4. Commit with the historical subject format: `Bump version from X to Y`, including
   `package.json` and `.llm/context.md` in the same commit.
5. Dispatch the manual publish workflow
   (`.github/workflows/npm-publish.yml`) via `workflow_dispatch`; use `dry_run: true`
   first to check the resolved version/dist-tag.

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
