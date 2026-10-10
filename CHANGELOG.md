# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- A Dx theme for the window, and it is now the default: a fresh window with no
  saved theme opens in Dx, and **Reset Theme** clears the saved selection and
  lands on Dx. Explicit theme selections, including Classic, survive the
  upgrade. If the Dx asset is missing, the window falls back to the package
  style and keeps the saved GUID (#148).

### Changed

- Renamed to DxVisualizer. The display name, window title, and menu item
  (**Tools → Wallstop Studios → DxVisualizer**) and the settings and theme
  asset menus (**Assets → Create → Wallstop Studios → DxVisualizer**) use the
  new name. The package name, C# namespaces, public types, saved state, and
  `--dataviz-*` theme tokens are unchanged, so projects upgrade without edits
  (#147).

### Fixed

- `BaseDataObject` asset ids no longer change on validation. An asset keeps
  its authored `Id` when it is non-empty; only an empty id fills from the
  asset's `.meta` GUID, as in 0.0.37. Created and cloned assets still get a
  fresh id. This restores persisted identity that consumers key saves and
  databases on (#137).

## [0.1.0] - 2026-10-07

### Added

- Compact and Minimal built-in style variants: density-only theme presets that
  keep the Classic palette and shrink window typography, action buttons, and
  control chrome (Compact 13px type, 20px action buttons, 4px control radii;
  Minimal 12px type, 16px action buttons, square control corners).
- Theme hot reload: an open Data Visualizer window re-applies the selected
  theme immediately when its theme asset or referenced stylesheet is
  reimported, reference-swapped, renamed, moved, or deleted; deletions fall
  back to the package style with the existing saved-GUID semantics.
- A `.unitypackage` is now built and verified on each release and attached to
  the GitHub Release alongside the npm package.

### Changed

- Control typography and radii in the base stylesheet now resolve from the
  `--dataviz-font-size`, `--dataviz-circle-size`, and `--dataviz-control-radius`
  tokens instead of hardcoded values. The Classic preset keeps its type and
  glyph sizes; the three large round action buttons are 1px smaller to match
  the action-button token, and themes that override `--dataviz-font-size` now
  also restyle the standard prose controls.
