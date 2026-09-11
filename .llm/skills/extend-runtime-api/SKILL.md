---
name: extend-runtime-api
description: Add or modify runtime APIs in Runtime/ - BaseDataObject subclasses, lifecycle interfaces (clone, create, rename, delete), display attributes, and UI Toolkit customization hooks. Use when extending the package's public runtime surface or integrating third-party ScriptableObjects.
metadata:
  category: Feature
---

# Skill: Extend Runtime API

## Surface Map

- `Runtime/DataVisualizer/BaseDataObject.cs` - abstract `ScriptableObject` (or Odin
  `SerializedScriptableObject` under `#if ODIN_INSPECTOR`) implementing
  `IComparable<BaseDataObject>`, `IDuplicable`, `ICreatable`, `IRenamable`,
  `IGUIProvider`, `IDisplayable`.
- Lifecycle interfaces: `IDuplicable` (`BeforeClone`/`AfterClone`),
  `ICreatable` (`BeforeCreate`/`AfterCreate`), `IRenamable`
  (`BeforeRename`/`AfterRename`), invoked by the editor window's asset operations.
- `CustomDataVisualizationAttribute` - overrides display namespace/friendly name on
  ScriptableObject classes; `DataVisualizerGUIContext` - context passed to
  `BuildGUI` for UI Toolkit customization.
- Runtime grants `InternalsVisibleTo("WallstopStudios.DataVisualizer.Editor")` (top
  of `BaseDataObject.cs`); editor-side helpers that tests need are `public` instead.

## Lifecycle Contract

- `Id` defaults to the asset GUID (`_assetGuid`), captured lazily in `OnValidate`
  via `TrySetAssetPath` - guarded by `#if UNITY_EDITOR` and skipped while
  `Application.isPlaying`.
- Clones must reset `_assetGuid` in `BeforeClone` so the duplicate re-captures its
  own GUID; `AfterClone` re-runs `TrySetAssetPath` and increments the
  `(Clone N)` title suffix.
- Derived types override only what they need (GUID generation, cache resets,
  companion asset syncing) without duplicating boilerplate; keep overrides
  side-effect-free enough to run inside batch operations.
- Any persistence write from lifecycle hooks must call `EditorUtility.SetDirty`
  (already done for `Title`/`Description` setters).

## Adding New Runtime Types

1. Keep `Runtime/` free of editor references except inside `#if UNITY_EDITOR`
   blocks; the runtime asmdef has no editor references.
2. Serialize new state with `[SerializeField]` camelCase fields; add
   `[FormerlySerializedAs]` when names change after users have saved assets.
3. Prefer virtual hooks over events so the window's batch operations stay
   deterministic.
4. Register display metadata through attributes (`CustomDataVisualizationAttribute`)
   rather than window-side name lookups.
5. Use indented block comments for multi-line explanations; keep `//` for standalone
   one-line notes or tool directives and `///` for XML documentation.

## Testing Requirements

Lifecycle callbacks and asset-state persistence need PlayMode coverage in a sibling
`Tests/Runtime` folder; pure logic (compare ordering, clone-suffix parsing) gets
EditMode tests - see [create-editmode-test](./create-editmode-test/SKILL.md).

## Related Skills

- [modify-data-visualizer](./modify-data-visualizer/SKILL.md) - how the window drives
  these hooks
- [create-editor-window](./create-editor-window/SKILL.md) - inspector integration
  patterns
