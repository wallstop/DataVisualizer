# Releasing com.wallstop-studios.data-visualizer

One workflow owns release preparation, tagging, and publication: [release.yml](../../.github/workflows/release.yml). The agent procedure is in
[bump-version-release](../skills/bump-version-release/SKILL.md).

The workflow never runs Unity. EditMode and PlayMode suites remain local release gates (see `.llm/context.md`). Unity archives are built and validated without an editor or Unity
license.

## Release flow

1. Add user-facing entries under `CHANGELOG.md` `## [Unreleased]`.
2. Run `release.yml` on `main` with `operation=prepare`. Choose `bump` (`patch`, `minor`, or `major`) or an `explicit_version`. The default `dry_run=true` validates the candidate
   on the runner without remote writes.
3. Set `dry_run=false` to open `release/vX.Y.Z`. Preparation bumps `package.json`, syncs `.llm/context.md`, dates the changelog section, validates the Unity archive and notes, and
   explicitly starts `llm-lint.yml` for the branch.
4. Merge the validated release PR into `main`. The `release` job checks out that exact merge commit, creates and pushes annotated `vX.Y.Z`, builds and validates both packages, and
   extracts the notes from the dated changelog section.
5. After artifact validation passes, the same job publishes npm and creates or updates the GitHub Release. It attaches the npm tarball and Unity package, each with a SHA-256
   checksum. Validated artifacts are retained for 14 days.

An externally pushed `v*` tag also runs the publication job. It checks out that exact tag and requires the package version and changelog notes to match.

The `prepare` job and `release` job run on different events in the same workflow. Tagging and publication run in the same job. No release workflow dispatch chain or custom release
credential is needed.

## Authentication

<!-- release-secrets:begin -->

No custom secrets are required.

<!-- release-secrets:end -->

GitHub writes use the built-in per-run `GITHUB_TOKEN`. npm publication uses OIDC. No npm token, PAT, or GitHub App is needed.

Preparation declares Contents, Pull requests, and Actions write permissions. It explicitly dispatches branch CI because a token-authored push does not start another workflow.
Publication declares Contents and ID token write permissions. Its tag push does not need to trigger another workflow: publication continues in the same job.

For automatic PR creation, enable GitHub repository Settings → Actions → General → Workflow permissions → Allow GitHub Actions to create and approve pull requests. This setting was
enabled during the release audit. The workflow only creates PRs. Manual dispatches must run from `main`.

## npm Trusted Publisher setup

Configure `com.wallstop-studios.data-visualizer` on npmjs.com under package settings → Trusted Publisher → GitHub Actions.

| Field                | Value               |
| -------------------- | ------------------- |
| Organization or user | `wallstop`          |
| Repository           | `DataVisualizer`    |
| Workflow filename    | `release.yml`       |
| Environment name     | leave empty         |
| Allowed actions      | allow `npm publish` |

If a publisher entry already names `npm-publish.yml`, update it to `release.yml` before using the consolidated workflow. npm must trust the file that contains the publish command;
preparation, automatic publication, and manual recovery all use this single file.

- Values are case-sensitive. Enter only the workflow filename and extension.
- The workflow calls `npm publish` directly, so allow that action. A publisher that only allows staged publication will reject it.
- Trusted publishing requires npm CLI 11.5.1 or later and Node 22.14.0 or later. The workflow uses Node 24 and checks the npm minimum before publication.
- Publication uses a GitHub-hosted runner. No local npm login is needed.
- `package.json` `repository.url` already matches this GitHub repository.

npm Trusted Publisher settings cannot be verified through public package metadata. An owner must verify the registration above.

## Rerun and recovery

| State                                | Action                                                                                                                                                                                                    |
| ------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Release PR was not opened            | Re-dispatch `operation=prepare`. An identical existing branch is reused. If its content differs, inspect it before choosing a new version or opening the existing branch's PR. No branch is force-pushed. |
| Release failed before tagging        | Fix the cause and rerun the failed job for the merged PR.                                                                                                                                                 |
| Tag exists, publication stopped      | Dispatch `release.yml` with `operation=publish`, `tag=vX.Y.Z`, and `dry_run=false`.                                                                                                                       |
| npm published, GitHub Release failed | Use the same publish dispatch. npm skips the existing version; release notes and assets are updated.                                                                                                      |
| Verify an existing tag               | Dispatch `operation=publish`, `tag=vX.Y.Z`, and `dry_run=true`.                                                                                                                                           |

A rerun of the merged PR job reuses its tag only when the tag still points at that exact merge commit. It never moves an existing tag. A conflicting tag fails. Direct publication
checks out the requested tag rather than current main.

npm publication is permanent. Recover from a bad release with a new patch version. The pipeline is recoverable across registries, not a transaction: npm may succeed before the
GitHub Release API fails.

## Validation

| Script                      | Rejects                                                                                                 |
| --------------------------- | ------------------------------------------------------------------------------------------------------- |
| `prepare-release.mjs`       | unsupported versions, missing or empty Unreleased notes, invalid changelog rotation                     |
| `tag-release.mjs`           | branch/version disagreement, missing or duplicated dated notes, an existing tag, a dirty tree           |
| `verify-release.mjs`        | tag/version disagreement, packed files outside the allowlist, missing payload files                     |
| `build-unitypackage.mjs`    | unsafe paths, missing metadata, duplicate or malformed GUIDs                                            |
| `validate-unitypackage.mjs` | corrupt archives, unsafe or duplicate members, checksum failures, disagreement with the tracked payload |
| `extract-release-notes.mjs` | missing, empty, or duplicated version notes                                                             |

Both archives, checksums, and release notes must be ready before npm publication. Pinned action revisions are tracked by Dependabot. Workflow contract tests run with the
cross-platform script suite.

## References

The audit compared release workflows in [DxCommandTerminal](https://github.com/wallstop/DxCommandTerminal/tree/master/.github/workflows),
[unity-helpers](https://github.com/Ambiguous-Interactive/unity-helpers/tree/main/.github/workflows), and
[DxMessaging](https://github.com/Ambiguous-Interactive/DxMessaging/tree/master/.github/workflows). They share curated changelog rotation, tags, required Unity archives, and npm
publication after artifact validation.

Preparation [run 37553208060](https://github.com/wallstop/DataVisualizer/actions/runs/37553208060) left `release/v0.1.0` after GitHub rejected automatic PR creation. Inspect that
branch before preparing the same version again.

- [GitHub workflow triggers](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow)
- [npm trusted publishers](https://docs.npmjs.com/trusted-publishers/)
