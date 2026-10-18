# Releasing com.wallstop-studios.data-visualizer

[release.yml](../../.github/workflows/release.yml) owns preparation, CI, merge, tagging, and publication. The agent procedure is
[bump-version-release](../skills/bump-version-release/SKILL.md).

The workflow builds and validates Unity archives without an editor. EditMode and PlayMode remain local release gates; see `.llm/context.md`.

## Release flow

1. Add user-facing notes under `CHANGELOG.md` `## [Unreleased]`.
2. Run Release on `main` with `operation=prepare`, select a bump or `explicit_version`, and set `dry_run=false`.
3. Preparation checks the npm repository identity, bumps the version, syncs `.llm/context.md`, rotates the changelog, and validates candidate artifacts.
4. The workflow opens or reuses `release/vX.Y.Z`, dispatches Linux and Windows CI, and waits for that exact request and commit. Failed or missing CI stops the run.
5. It merges the validated PR head with the repository's normal squash-merge API. Changed heads and repository merge restrictions stop the run. No policy bypass is used.
6. The same workflow consumes the returned merge SHA, creates an annotated tag, validates both packages and notes, publishes npm, and creates the GitHub Release.

No manual PR merge or second dispatch is needed. GitHub does not start workflows for `GITHUB_TOKEN` merges or pushes, so explicit CI dispatch and the merge output connect the
chain. An externally merged release PR or externally pushed `v*` tag also publishes. Fork PRs cannot trigger publication through a release branch name.

The default `dry_run=true` validates the bumped candidate without pushing, opening or merging a PR, tagging, or publishing. Publication dry runs retain validated artifacts for 14
days.

## Authentication and repository policy

<!-- release-secrets:begin -->

No custom secrets are required.

<!-- release-secrets:end -->

GitHub writes use the built-in `GITHUB_TOKEN`; npm uses OIDC. No npm token, PAT, or GitHub App is needed. Preparation needs Contents, Pull requests, and Actions write permissions.
Publication needs Contents and ID token write permissions.

Enable Settings → Actions → General → Workflow permissions → Allow GitHub Actions to create and approve pull requests. Allow squash merges. Repository policies must permit the bot
to merge after CI passes. A required human review or deployment approval remains a human gate; remove that requirement for release PRs if zero-touch releases are required. The
workflow does not approve its own PR or bypass policy.

## npm Trusted Publisher setup

Configure package `com.wallstop-studios.data-visualizer` on npmjs.com → package settings → Trusted Publisher → GitHub Actions.

| Field                | Value                   |
| -------------------- | ----------------------- |
| Organization or user | `Ambiguous-Interactive` |
| Repository           | `DxVisualizer`          |
| Workflow filename    | `release.yml`           |
| Environment name     | leave empty             |
| Allowed actions      | allow `npm publish`     |

The manifest must use `git+https://github.com/Ambiguous-Interactive/DxVisualizer.git`. Identity is case-sensitive. Preparation checks it before remote writes; publication checks it
before packing. The workflow uses Node 24 and verifies npm is at least 11.5.1. Prereleases use `next`; stable releases use `latest`. The publisher registration is private npm
configuration; public metadata cannot verify it. After the repository transfer, update this registration before publishing from the new owner.

## Rerun and recovery

Run Release on `main` with `operation=publish` and `dry_run=false` to resume the current package version's existing tag. The tag input can stay blank; set it to resume another
version. Set `dry_run=true` to build and retain artifacts without publication. A missing tag fails at checkout rather than silently releasing current main.

Recovery uses tooling from the workflow revision and checks out the exact source tag. This lets fixes to the build process recover old releases. Tags are never moved. A merged-PR
job rerun accepts an existing tag only if it points at the same merge commit.

If preparation failed after branch creation, rerun it with the same inputs. An identical existing branch and open PR are reused. Different content fails without force-pushing. If
preparation already merged, rerun the failed publication job or use Publish. Preparing a new release after a completed preparation would bump the version again.

npm versions are permanent. If npm succeeded before GitHub publication failed, Publish skips that existing version and finishes the release assets and notes. Both archives, their
SHA-256 checksums, and changelog notes must validate before publication. The two registries do not provide an atomic transaction.

## 0.1.1 incident and recovery

[Preparation run 39084091695](https://github.com/wallstop/DataVisualizer/actions/runs/39084091695) opened [PR #275](https://github.com/wallstop/DataVisualizer/pull/275). The old
design stopped there and needed a manual merge.

[Publication run 39084443871](https://github.com/wallstop/DataVisualizer/actions/runs/39084443871) created `v0.1.1` and validated all artifacts. npm authenticated, generated
provenance, then rejected the upload with E422: `package.json` named `Ambiguous-Interactive/DxVisualizer` while provenance named `wallstop/DataVisualizer`. The registry did not
publish `0.1.1`, and GitHub publication was skipped. The prior guide incorrectly claimed that the manifest identity already matched.

[Recovery run 39086226932](https://github.com/wallstop/DataVisualizer/actions/runs/39086226932) failed earlier: Publish received an empty tag, and the old resolver required one.
Rerunning the old tag's tooling would also keep the incorrect manifest identity.

The fixed Publish path repairs only `repository.url` in the build tree for package `com.wallstop-studios.data-visualizer` version `0.1.1`, only when the URL is the exact known
legacy value and the publishing repository is `wallstop/DataVisualizer`. Code, version, release notes, and the existing tag stay intact. Both archives contain the same repaired
metadata. Unrelated repositories, versions, and metadata mismatches fail. The repair is idempotent and is recorded in the workflow log.

Regression tests cover identity rejection before preparation, the narrow metadata repair, automatic CI/merge failure boundaries, and the complete package build chain.

## References

- [npm provenance requirements](https://docs.npmjs.com/generating-provenance-statements/)
- [GitHub workflow triggers](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow)
- [npm trusted publishers](https://docs.npmjs.com/trusted-publishers/)
