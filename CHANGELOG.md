# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Add the Dx theme and make it the default: a fresh window opens in Dx and Reset Theme returns to it. Explicit selections survive, and a missing Dx asset falls back to the package
  style (#148).

### Changed

- Renamed to DxVisualizer: display name, window title, and menu paths (**Tools → Wallstop Studios → DxVisualizer**). Namespaces, types, package name, saved state, and theme tokens
  are unchanged, so projects upgrade without edits (#147).

### Fixed

- `BaseDataObject` asset ids no longer change on validation: an asset keeps its authored id, and only an empty id fills from the asset GUID. Created and cloned assets still get a
  fresh id (#137).

## [0.1.0] - 2026-10-07

### Added

- Compact and Minimal built-in style variants: density-only theme presets that keep the Classic palette and shrink window typography, action buttons, and control chrome (Compact
  13px type, 20px action buttons, 4px control radii; Minimal 12px type, 16px action buttons, square control corners).
- Theme hot reload: an open Data Visualizer window re-applies the selected theme immediately when its theme asset or referenced stylesheet is reimported, reference-swapped,
  renamed, moved, or deleted; deletions fall back to the package style with the existing saved-GUID semantics.
- A `.unitypackage` is now built and verified on each release and attached to the GitHub Release alongside the npm package.

### Changed

- Control typography and radii in the base stylesheet now resolve from the `--dataviz-font-size`, `--dataviz-circle-size`, and `--dataviz-control-radius` tokens instead of
  hardcoded values. The Classic preset keeps its type and glyph sizes; the three large round action buttons are 1px smaller to match the action-button token, and themes that
  override `--dataviz-font-size` now also restyle the standard prose controls.
