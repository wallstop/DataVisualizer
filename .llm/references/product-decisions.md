# Standing Product Decisions

Long-lived constraints for every Data Visualizer task. Recorded decisions stay
here instead of `PLAN.md`; revisit only by explicit owner decision.

## Compatibility

- Preserve Unity 2021.3+ compatibility and existing assets, settings, saved
  selections, ordering, and public extension contracts.
- Retain a minimal player-compatible runtime assembly. Exclude editor tooling
  and development dependencies from players; do not break games whose assets
  inherit from `BaseDataObject`.
- Preserve exact-type matching and current main-asset discovery. Subasset
  browsing is out of scope; document the boundary rather than changing identity
  semantics.

## Product behavior

- Instant loading means immediate interaction and visible rows, with metadata
  indexing and asset loading performed incrementally. Never require 10,000 live
  ScriptableObjects before displaying results.
- Offer automatic density adjustment, a persisted manual scale override, and
  responsive pane arrangements.
- Cross-platform determinism means package-owned layout and alignment within
  one physical pixel at equivalent dimensions, DPI, and theme. OS font
  rasterization and third-party inspector internals may differ.
- Pause background indexing, automatic refresh, and package-driven asset
  editing during Play Mode. Keep the last view visible and resume afterwards.

## Automation and extensibility

- Provide typed C# editor APIs and versioned JSON commands. Reuse the existing
  Unity MCP bridge; do not build a dedicated MCP server, daemon, HTTP listener,
  or background file watcher.

## Build, test, and release

- Run all Unity tests, performance experiments, builds, and screenshots
  locally. Do not add Unity CI runners, Unity-in-CI workflows, or license or
  seat requirements.
- Cloud release automation may assemble a `.unitypackage` with ordinary archive
  tooling, but must never install, invoke, or authenticate Unity. A validated
  `.unitypackage` ships alongside the npm tarball.
- Build and validate documentation locally. A documentation-only GitHub Pages
  publishing workflow may publish prepared artifacts without executing Unity.
- Preserve unrelated workspace changes. Do not perform broad cleanup of the
  existing agent and devcontainer tooling.
