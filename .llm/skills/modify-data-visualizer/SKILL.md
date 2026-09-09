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
