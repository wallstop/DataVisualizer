# Data Visualizer Enhancement Roadmap

## 1. Settings Experience & State Portability
- [x] Build a richer **Settings** popover panel (shortcut hints, drag behaviour toggles, processor defaults).
- [x] Surface **user-state export/import** to JSON (read/write through `IUserStateRepository`) with validation + success/failure dialogs.
- [x] Add **Reset to defaults** and per-section apply buttons; ensure all changes route through scheduler-backed save APIs.
- [x] Document the settings schema and update README/AGENTS with the new UX.

## 2. Search & Filtering Improvements
- [ ] Introduce configurable **fuzzy matching** options (threshold, scoring display) leveraging `SearchService`.
- [ ] Highlight match quality in the search popover (confidence badges, keyboard shortcut legend).
- [ ] Add quick filters in the namespace pane (namespace + label chips) that sync with `VisualizerSessionState`.
- [ ] Expand tests to cover fuzzy search behaviour and combined filters.

## 3. Batched Save/Update Pipeline
- [ ] Audit all calls to `AssetDatabase.SaveAssets()` and replace them with scheduler-driven hooks.
- [ ] Extend `ScriptableAssetSaveScheduler` with diagnostics (queued operations, last flush time) and unit tests.
- [ ] Ensure persistence interfaces (`IUserStateRepository` / settings popover) fully own save semantics; no direct editor API calls from controllers/wrapper window.
- [ ] Add integration tests that validate debounce/flush behaviour under rapid edits.

## 4. Undo / Redo Support
- [ ] Wrap object list operations (create, rename, reorder, label changes) with `Undo.RecordObject` + descriptive action names.
- [ ] Provide helper APIs for services/controllers to register undo steps without depending on `DataVisualizer` internals.
- [ ] Verify undo/redo flows for labels, namespace changes, and processor-triggered modifications.
- [ ] Add automated edit-mode tests exercising the undo stack where feasible.

## 5. Test Coverage Expansion
- [ ] Build controller-focused unit tests using `DataVisualizer.OverrideUserStateRepositoryForTesting` and event hub assertions.
- [ ] Add playmode/editmode integration tests covering: settings export/import, fuzzy search, batched saves, undo/redo.
- [ ] Introduce performance guard tests for paged loading & drag modifiers using `DiagnosticsState`.
- [ ] Wire tests into CI (document commands + expected outputs).
