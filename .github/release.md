# Release operations

Releases are prepared from `main` with the **Prepare release** workflow. Select
`patch`, `minor`, or `major`, or choose `none` and provide an explicit SemVer.
Use `dry_run` first. A real run updates `package.json`, `.llm/context.md`, and
`CHANGELOG.md`, then opens one `release/vX.Y.Z` pull request.

## Required repository configuration

- `AUTO_COMMIT_APP_ID`: the numeric ID of a GitHub App installed on this
  repository.
- `AUTO_COMMIT_APP_PRIVATE_KEY`: the PEM private key for that App.
- The App installation must have only `Contents: write` and `Pull requests:
  write` for this repository. The tag workflow uses only `Contents: write`.
- npm Trusted Publishing must be configured for
  `com.wallstop-studios.data-visualizer` with this repository, the
  `Publish release artifacts` workflow, and the default branch/tag workflow
  identity. No npm token secret is used.

## Recovery and reruns

The tag workflow validates the merged release branch and creates an annotated
`vX.Y.Z` tag. An existing tag is never retargeted automatically: a tag at the
same commit is left alone, while a tag at another commit fails closed.

The publication workflow checks whether the exact npm `name@version` already
exists and skips republishing it. It always validates the checked-out tag and
artifacts first. If npm succeeds but GitHub Release creation fails, rerun the
tag workflow from the existing tag event or use the workflow run again; release
creation updates an existing release and replaces attachments by checksum-safe
name. A failed release-preparation run may be retried only after resolving an
existing branch or PR with the same version.

All `.unitypackage` assembly and validation uses Python, tar, gzip, and checksum
tools in cloud CI. Unity is not installed, authenticated, or invoked by any
release workflow; EditMode and PlayMode validation remain local gates.
