---
name: create-editor-window
description: Build or extend UI Toolkit editor windows (EditorWindow + UIElements) in the Data Visualizer package following repo patterns - menu wiring with nameof, USS class constants, pane layout, EditorPrefs persistence, and Odin-compatible SerializedObject handling. Use when adding editor windows, panes, popovers, or controls.
metadata:
  category: Feature
---

# Skill: Create Editor Window

## Conventions (grounded in `Editor/DataVisualizer/DataVisualizer.cs`)

- Editor-only code lives under `Editor/` (asmdef `includePlatforms: ["Editor"]`);
  no `#if UNITY_EDITOR` wrapper is needed there.
- Menu wiring uses `nameof`-built labels:
  `[MenuItem("Tools/Wallstop Studios/Data Visualizer")]` - keep the company segment
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
