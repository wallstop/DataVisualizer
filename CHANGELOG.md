# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- npm publishing is tag-driven: pushing an annotated `vX.Y.Z` release tag runs
  the publish workflow, which verifies the tag against the package version and
  the packed payload against the `files` allowlist before publishing. The
  manual dispatch-and-publish workflow is replaced; rerun a release by
  dispatching the workflow with its `tag` input.

### Added

- Initial changelog. Release history before this entry is available in the
  [repository history](https://github.com/wallstop/DataVisualizer/commits/main)
  and on [npm](https://www.npmjs.com/package/com.wallstop-studios.data-visualizer).
