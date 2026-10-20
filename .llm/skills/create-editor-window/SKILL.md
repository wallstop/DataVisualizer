---
name: create-editor-window
description: Build or extend UI Toolkit editor windows (EditorWindow + UIElements) in the DxVisualizer package following repo patterns - menu wiring with nameof, USS class constants, pane layout, EditorPrefs persistence, and Odin-compatible SerializedObject handling. Use when adding editor windows, panes, popovers, or controls.
metadata:
  category: Feature
---

# Skill: Create Editor Window

## Conventions (grounded in `Editor/DataVisualizer/DataVisualizer.cs`)

- Editor-only code lives under `Editor/` (asmdef `includePlatforms: ["Editor"]`);
  no `#if UNITY_EDITOR` wrapper is needed there.
- Menu wiring uses `nameof`-built labels:
  `[MenuItem("Tools/Wallstop Studios/DxVisualizer")]` - keep the company segment
  literal, derive the feature segment from a const/`nameof` where practical.
- USS class names and `EditorPrefs` keys are `private const string` fields; prefs
  keys share a prefix (`PrefsPrefix = "WallstopStudios.Editor.DataVisualizer."`).
- Build UI in `OnEnable` (or `CreateGUI` for newer Unity) from `rootVisualElement`;
  load stylesheets from `Editor/DataVisualizer/Styles/` via `AssetDatabase` or
  relative package paths; reuse `StyleConstants` instead of literals.
- Window state: transient layout in `EditorPrefs` (splitter widths, applied flags);
  shared/project state in `Data/DataVisualizerSettings.asset`; per-user state in
  `Data/DataVisualizerUserState` (JSON) to avoid merge conflicts.
- Persist sparingly and idempotently: write on real changes, restore on enable,
  never clear saved keys during refactor without a migration path.
- Inspector integration: bind with `SerializedObject`/`SerializedProperty`; for
  ScriptableObjects that may use Odin, branch on `#if ODIN_INSPECTOR` and prefer
  `SerializedScriptableObject`-compatible flows.

## Integrated Asset Tools

- Add package workflows inside the existing DxVisualizer window. Use `AssetToolPanel`
  for saved-search and validation workspaces rather than opening a second unstyled
  `EditorWindow`. Inherit the root's active theme and use `StyleConstants` classes
  with USS token colors. Group related actions and show disabled states.
- Keep browser state while a workspace is open; provide Back and Escape exits.
  Dispose its work when switching tools, rebuilding the tree, detaching, or closing
  the owner. Hosted tests must check that tool navigation adds no native window.
- Search snapshots capture notified user text, not placeholder text or a popover's
  transient query cache. Exercise popover closure before capture in a regression.
- Capture and inspect the real hosted UI with representative content at normal and
  compact sizes. Refresh affected documentation screenshots before handoff.

## Minimal Skeleton

See the full template:
[EditorWindowTemplate.cs](../../code-samples/EditorWindowTemplate.cs).

## Layout Rules

- Three-pane layout (namespace tree, object list, inspector) uses fixed-pane
  splitters with minimum widths enforced as constants; replicate that pattern for
  new panes.
- Popovers and search result lists are built from reusable UI Toolkit elements in
  `Editor/DataVisualizer/UI/` (`HorizontalToggle`, `ActionButtonToggle`) - extend
  those before inventing new controls.
- Every user-visible string class gets a USS class constant; avoid inline style
  assignments except for computed geometry.

## Testing

UI layout logic that can be extracted (measurements, ordering, filtering) belongs in
plain C# under `Editor/DataVisualizer/Data/` or `Utilities/` with EditMode tests;
see [create-editmode-test](./create-editmode-test/SKILL.md).

## Related Skills

- [modify-data-visualizer](./modify-data-visualizer/SKILL.md) - map of the existing
  window code
- [ship-changes](./ship-changes/SKILL.md) - validation before committing UI changes
