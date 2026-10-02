# Releasing com.wallstop-studios.data-visualizer

This reference records what an owner must configure before the first release
and how to recover a release that stopped halfway. The agent-facing procedure
lives in [bump-version-release](../skills/bump-version-release/SKILL.md).

The release chain never runs Unity. EditMode and PlayMode suites stay a local
release gate (see `.llm/context.md`).

## Release chain

Three workflows run in order. Each one consumes the previous step's output.

| Step | Workflow | Trigger | Output |
| --- | --- | --- | --- |
| 1. Prepare | `release-prep.yml` | manual dispatch | `release/vX.Y.Z` pull request |
| 2. Tag | `release-tag.yml` | the release pull request merges into `main` | annotated `vX.Y.Z` tag |
| 3. Publish | `npm-publish.yml` | the tag is pushed | npm version + GitHub Release |

`release-prep.yml` takes `bump` (`patch`, `minor`, or `major`), an optional
`explicit_version`, and `dry_run`. It bumps `package.json`, syncs the version
line in `.llm/context.md`, rotates the `Unreleased` changelog section into
`## [X.Y.Z] - YYYY-MM-DD`, validates the tree, and opens the pull request.

`release-tag.yml` also runs by dispatch, where `branch` is required and
`dry_run` verifies without tagging.

`npm-publish.yml` also runs by dispatch, where `tag` is the recovery entry
point (see [Rerun and recovery](#rerun-and-recovery)).

## Required secrets

<!-- release-secrets:begin -->

| Secret | Required | Scope | Read by |
| --- | --- | --- | --- |
| `RELEASE_TOKEN` | yes, for the automatic chain | GitHub App installation token or PAT with `contents: write`; `release-prep.yml` also needs `pull-requests: write` | `release-prep.yml`, `release-tag.yml` |

<!-- release-secrets:end -->

npm publishing uses no token. `npm-publish.yml` authenticates through npm
Trusted Publishing over OIDC, which needs the `id-token: write` permission and
a matching entry on npmjs.com (see below).

`GITHUB_TOKEN` needs no configuration. It is the built-in per-run token, and
`npm-publish.yml` uses it to create the GitHub Release.

### Why `RELEASE_TOKEN` is required

GitHub does not start workflow runs for events created with the default
`GITHUB_TOKEN`. Both workflows read `secrets.RELEASE_TOKEN || github.token`, so
they stay runnable before the secret exists. That fallback has two silent
consequences:

- `release-prep.yml` pushes the release branch and opens the pull request, but
  the pull request gets no CI run.
- `release-tag.yml` creates and pushes the tag, but `npm-publish.yml` never
  starts. The release stops after tagging with no npm publish and no GitHub
  Release, and every workflow reports success.

The tag push is the step that cannot recover on its own, so configure
`RELEASE_TOKEN` before the first release.

## npm Trusted Publisher setup

Configure the package once on npmjs.com, under the package settings for
`com.wallstop-studios.data-visualizer` → Trusted Publisher → GitHub Actions.

| Field | Value |
| --- | --- |
| Organization or user | `wallstop` |
| Repository | `DataVisualizer` |
| Workflow filename | `npm-publish.yml` |
| Environment name | leave empty |
| Allowed actions | allow `npm publish` |

Points that decide whether a release succeeds:

- npm does not verify the configuration when it is saved. A wrong field appears
  only at publish time, as an `ENEEDAUTH` error.
- Allowed actions default matters. Trusted publisher configurations created
  after 2026-09-03 allow `npm stage publish` only. `npm-publish.yml` calls
  `npm publish` directly, so that action must be enabled explicitly.
- The workflow filename is the file that contains the publish command, given as
  a bare filename with its extension. The manual rerun dispatches
  `npm-publish.yml` itself, so the same entry covers both the tag push and the
  rerun. Do not add a second publisher for a dispatch workflow.
- Every field is case-sensitive and must match exactly.
- Trusted publishing needs npm CLI 11.5.1 or later and Node 22.14.0 or later.
  `npm-publish.yml` pins Node 24 and fails closed when npm is older than
  11.5.1.
- Only GitHub-hosted runners are supported. Self-hosted runners cannot
  publish.
- `package.json` `repository.url` must match the GitHub repository, which it
  already does.

After the first successful publish, set the package's publishing access to
require two-factor authentication and disallow tokens, and add a tag
protection rule. Those are recommended hardening, not prerequisites.

## Rerun and recovery

| State | Action |
| --- | --- |
| The release pull request was not opened | Re-dispatch `release-prep.yml` with the same inputs. Use `dry_run` first to check the computed version. |
| `release-tag.yml` failed | Fix the cause, then dispatch it with `branch` set to `release/vX.Y.Z`. Use `dry_run` to verify before tagging. |
| The tag already exists | Never retargeted. Delete the tag deliberately, or cut a new version. |
| npm published, the GitHub Release failed | Dispatch `npm-publish.yml` with the `tag` input. The publish step is skipped and the release is edited and re-uploaded. |
| Nothing published yet | Dispatch `npm-publish.yml` with the `tag` input to run the whole step again. |

Two rules make reruns safe:

- `tag-release.mjs` refuses to move an existing tag, so a rerun cannot repoint
  a published version at different commits.
- `npm-publish.yml` skips `npm publish` when `name@version` is already on the
  registry, and `gh release upload --clobber` replaces artifacts. A rerun
  therefore finishes a partially completed release instead of duplicating it.

npm publication is otherwise irreversible. There is no reliable unpublish.
Recover from a bad release with a new patch version.

## Fail-closed guards

The chain refuses to continue rather than publish something wrong.

| Script | Rejects |
| --- | --- |
| `prepare-release.mjs` | an unsupported version, a dirty tree, changelog rotation that cannot be applied |
| `tag-release.mjs` | a branch that is not `release/v<version>`, a missing, duplicated, or empty changelog section, an existing tag, a dirty tree, an unresolvable commit |
| `verify-release.mjs` | a tag that is not `v<package version>`, packed files outside the allowlist, allowlisted files missing from the tarball |
| `build-unitypackage.mjs` | unsafe paths, missing or malformed `.meta` companions, duplicate or malformed GUIDs |
| `validate-unitypackage.mjs` | a corrupt or empty archive, members outside the GUID-directory layout, duplicate members, unsafe paths, checksum problems, and any disagreement with the tracked tree |

`npm-publish.yml` also checks that npm is new enough for trusted publishing
before it publishes.
