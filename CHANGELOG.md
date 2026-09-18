# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Theme hot reload: an open Data Visualizer window re-applies the selected
  theme immediately when its theme asset or referenced stylesheet is
  reimported, reference-swapped, renamed, moved, or deleted; deletions fall
  back to the package style with the existing saved-GUID semantics.
- Deterministic `.unitypackage` builder (`scripts/release/build-unitypackage.mjs`):
  builds the release archive and a `.sha256` checksum from the tracked,
  allowlisted package payload with no Unity invocation; two builds of the same
  tree are byte-identical. Not yet attached to the release workflow.
- Standalone `.unitypackage` validator (`scripts/release/validate-unitypackage.mjs`):
  verifies a built archive against the tracked, allowlisted payload with no
  Unity invocation, using standard `tar` for listing and extraction; checks
  the `.sha256` sidecar, every GUID directory, staged path, asset and `.meta`
  byte, and archive member, and fails closed on corruption or drift.
- Initial changelog. Release history before this entry is available in the
  [repository history](https://github.com/wallstop/DataVisualizer/commits/main)
  and on [npm](https://www.npmjs.com/package/com.wallstop-studios.data-visualizer).

### Changed

- npm publishing is tag-driven: pushing an annotated `vX.Y.Z` release tag runs
  the publish workflow, which verifies the tag against the package version and
  the packed payload against the `files` allowlist before publishing. The
  manual dispatch-and-publish workflow is replaced; rerun a release by
  dispatching the workflow with its `tag` input.
