---
name: modify-data-visualizer
description: Navigate and modify the Data Visualizer window codebase safely - file responsibility map, persistence models, async batch loading, search, and styling. Use when changing window behavior, fixing bugs, or refactoring editor code.
metadata:
  category: Feature
---

# Skill: Modify Data Visualizer

## Navigation Map

| Path | Responsibility |
| --- | --- |
| `Editor/DataVisualizer/DataVisualizer.cs` | Main `EditorWindow`; ~9.3k lines - always search (`rg`) before editing |
| `Editor/DataVisualizer/NamespaceController.cs` | Namespace/type catalog, drag-drop ordering |
| `Editor/DataVisualizer/Data/` | Persisted models: `DataVisualizerUserState`, `DataVisualizerSettings`, `TypeObjectOrder`, `NamespaceTypeOrder`, `NamespaceCollapseState`, `LastObjectSelectionEntry`, `TypeLabelFilterConfig`, `ProcessorState`, plus pure logic (`LabelFilterEvaluator`) |
| `Editor/DataVisualizer/Search/` | Global search matching: `MatchSource`, `MatchDetail`, `SearchResultMatchInfo` |
| `Editor/DataVisualizer/UI/` | Reusable controls: `HorizontalToggle`, `ActionButtonToggle` |
| `Editor/DataVisualizer/Styles/` | `StyleConstants.cs` + `DataVisualizerStyles.uss` |
| `Editor/DataVisualizer/Unity/` | `DataVisualizerAssetPostprocessor` |
| `Editor/DataVisualizer/Utilities/` | `ReadOnlyAttribute` + drawer, `MonitorUtility` |

## Working in the Main Window File

- `rg` for the feature first (constants, method names); the file predates the split,
  so new subsystems should go into `Data/`, `Search/`, `UI/`, or `Utilities/` files
  instead of growing `DataVisualizer.cs` further.
- Async asset loading runs in batches (`AsyncLoadBatchSize`,
  `AsyncLoadPriorityBatchSize` constants) - preserve batch invariants when touching
  load paths and keep debug logging behind `EnableAsyncLoadDebugLog`.
- Selection persistence is regression-prone; changes to selection or ordering need a
  `SelectionPersistenceTests`-style EditMode test.

## Persistence Safety

- User state is JSON (`DataVisualizerUserState.json`) or a settings asset depending
  on the "persist state in user settings" toggle; both paths must stay consistent
  (see the dirty-state fix history).
- Renaming serialized fields or JSON keys requires `[FormerlySerializedAs]` or an
  explicit migration; never orphan saved keys silently.
- EditorPrefs keys share the `PrefsPrefix` const; reuse the prefix for new keys.

## Asset Discovery

- Treat `AssetDatabase.FindAssets` as an I/O boundary. Call it once per logical
  type refresh unless a measured requirement justifies another query, and reuse its
  returned array when no merge is needed.
- Search folders constrain a query; they cannot express "this type anywhere OR any
  asset in this folder." Do not simulate that union with repeated searches in a
  per-type loop.
- Unity type filters can omit editor-only `ScriptableObject` assets whose class has
  no matching `MonoScript`. Persist exact asset GUIDs at create, clone, reorder, and
  move boundaries, remove them on delete, then validate and merge those direct GUIDs
  with the single type query.
- Validate direct GUIDs with `GetMainAssetTypeAtPath` and exact type equality. Do not
  instantiate arbitrary user types to probe script metadata or broaden the fallback
  into a project-wide asset scan.
- Discovery is intentionally limited to exact main assets. Derived instances and
  subassets do not satisfy a selected base/main type; keep this boundary covered by
  tests instead of broadening equality to assignability.

## Async Ordering

- `_asyncDisplayOrderByGuid` is the full canonical order, including unloaded GUIDs;
  `_selectedObjects` is only the currently loaded subset. Never serialize the subset
  directly or append pending GUIDs after it.
- During a partial load, reorder loaded GUIDs within their canonical slots so
  unloaded entries retain position. For explicit create, clone, top, bottom, or
  delete operations, mutate the full canonical GUID order first, then merge and
  persist the loaded view.
- Keep `_asyncDisplayOrderByGuid` and `_selectedObjectOrderIndex` synchronized after
  every canonical-order mutation; pending batches use those indexes for insertion.

## Filtering and Search

- Label filtering is config-driven (`TypeLabelFilterConfig` with AND/OR combination
  via `LabelCombinationType`) and evaluated by the pure `LabelFilterEvaluator` -
  extend the evaluator, not the UI, when adding filter semantics.
- Search results are aggregated into `SearchResultMatchInfo` with per-source
  `MatchSource`/`MatchDetail` entries; cap visible results (`MaxSearchResults`).

## Styling

- Add USS classes to `DataVisualizerStyles.uss` and expose names through
  `StyleConstants`; the window applies stylesheets from `Styles/`.

## Related Skills

- [create-editor-window](./create-editor-window/SKILL.md) - window conventions
- [extend-runtime-api](./extend-runtime-api/SKILL.md) - runtime hooks the window calls
- [create-editmode-test](./create-editmode-test/SKILL.md) - test patterns for the
  pure-logic files
