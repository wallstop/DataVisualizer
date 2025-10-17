# Data Visualizer Enhancement Roadmap

## 0. Build Health & Compilation
- [x] Resolve missing `FontStyle` compile reference by importing `UnityEngine` in namespace controller (completed).

## 1. Settings Experience & State Portability
- [x] Build a richer **Settings** popover panel (shortcut hints, drag behaviour toggles, processor defaults).
- [x] Surface **user-state export/import** to JSON (read/write through `IUserStateRepository`) with validation + success/failure dialogs.
- [x] Add **Reset to defaults** and per-section apply buttons; ensure all changes route through scheduler-backed save APIs.
- [x] Document the settings schema and update README/AGENTS with the new UX.

## 2. Search & Filtering Improvements
- [x] Introduce configurable **fuzzy matching** options (threshold, scoring display) leveraging `SearchService`. _(Completed – search popover exposes fuzzy toggle, threshold slider, and confidence badges synced to settings)_
- [x] Highlight match quality in the search popover (confidence badges, keyboard shortcut legend). _(Completed – badges rendered per result with confidence legend in popover)_
- [x] Add quick filters in the namespace pane (namespace + label chips) that sync with `VisualizerSessionState`. _(Completed – namespace chip, label removal actions, and logic toggle update session state)_
- [x] Expand tests to cover fuzzy search behaviour and combined filters. _(Completed – editor tests validate popover controls and filter chips)_

## 3. Batched Save/Update Pipeline
- [x] Audit all calls to `AssetDatabase.SaveAssets()` and replace them with scheduler-driven hooks. _(Completed – runtime paths now queue saves through `ScriptableAssetSaveScheduler`)_
- [x] Extend `ScriptableAssetSaveScheduler` with diagnostics (queued operations, last flush time) and unit tests. _(Completed – scheduler exposes diagnostics with editor tests)_
- [x] Ensure persistence interfaces (`IUserStateRepository` / settings popover) fully own save semantics; no direct editor API calls from controllers/wrapper window. _(Completed – settings/user state writes now route through repositories)_
- [x] Add integration tests that validate debounce/flush behaviour under rapid edits. _(Completed – repository and scheduler tests cover debounced flush)_

## 4. Undo / Redo Support
- [x] Wrap object list operations (create, rename, reorder, label changes) with `Undo.RecordObject` + descriptive action names. _(Completed – assets and settings route through centralized undo helpers)_
- [x] Provide helper APIs for services/controllers to register undo steps without depending on `DataVisualizer` internals. _(Completed – shared `IUndoService` available via dependencies)_
- [x] Verify undo/redo flows for labels, namespace changes, and processor-triggered modifications. _(Completed – editor undo tests cover label and rename scenarios)_
- [x] Add automated edit-mode tests exercising the undo stack where feasible. _(Completed – Undo integration tests added)_

## 5. Test Coverage Expansion
- [ ] Build controller-focused unit tests using `DataVisualizer.OverrideUserStateRepositoryForTesting` and event hub assertions.
- [ ] Add playmode/editmode integration tests covering: settings export/import, fuzzy search, batched saves, undo/redo.
- [ ] Introduce performance guard tests for paged loading & drag modifiers using `DiagnosticsState`.
- [ ] Wire tests into CI (document commands + expected outputs).
