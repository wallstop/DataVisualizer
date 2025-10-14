# Data Visualizer Optimization Plan

## Implementation Plan

### [x] Introduce Allocation Utilities
- Fork the minimal pooling APIs that are needed (`Buffers<T>`, `SetBuffers<T>`, `WallstopArrayPool<T>`, `PooledResource<T>`) into `Runtime/Helper/Pooling/` to keep the package dependency-free.
- Trim features to project needs (List/HashSet/Array/Stopwatch pools) and wrap them with `internal` visibility.
- Add light editor-only instrumentation helpers (pooled `Stopwatch`) for measuring hot paths during optimization.

### [x] Build Persistent Asset and Label Cache
- Create `Editor/DataVisualizer/Data/AssetIndex.cs` that tracks GUID to lightweight metadata (type name, asset path, labels, last write) and keeps strong references to assets only on demand.
- On window load, scan existing managed types once, store metadata, and keep the GUID list in `_allManagedObjectsCache` instead of live `ScriptableObject` instances.
- Hook `AssetPostprocessor` and `EditorApplication.projectChanged` to enqueue incremental updates, using background delay callbacks to avoid duplicate scans.
- Maintain per-type sorted GUID lists with dirty flags so `LoadObjectTypes` pulls data from the cache without round-tripping through the `AssetDatabase`.

### [x] Virtualize Object List Rendering
- Replace manual row creation in `BuildObjectsView` with a `ListView` that binds to the cached GUID list, fetching metadata and (lazily) the `ScriptableObject` only for visible rows.
- Preserve scroll offset and selection state between refreshes, and update individual rows in response to reorder events instead of rebuilding the entire list.
- Add pooled row view-models to minimize per-frame allocations during drag operations.

### [x] Optimize Filtering and Search
- Store labels in the asset metadata cache, updating only on label mutations or asset imports/deletes.
- Rework label filtering to operate on cached label sets using pooled `HashSet<string>` instances; only fetch live objects for the final result list.
- Convert the recursive reflection search to use compiled accessors from `ReflectionHelpers` and an iterative pool-backed traversal queue to eliminate repeated allocations and recursion depth risks.
- Expand search results past the first 25 items with pagination or a "show all" toggle, and expose counts so users know if results are truncated.

### Streamline Reordering and Processors
- Update drag-and-drop reordering to mutate the cached GUID order and refresh just the affected range (using `ListView.RefreshItem`).
- When invoking processors, supply pooled arrays from the new pooling helpers and only call `AssetDatabase.SaveAssets()` if any processor reports modifications.
- Capture processor runtimes with pooled stopwatches and log slow runs (optional profiling toggle).

### Robust Persistence and State Handling
- Introduce a debounced persistence service that batches changes (namespace/type order, filters, label edits) and writes JSON/settings after a short idle period or on window close.
- Harden `DirectoryHelper.AbsoluteToUnityRelativePath` logic and validate sanitized paths early to prevent incorrect asset moves/creations.
- Ensure asset deletion detection uses GUID/type metadata so refreshes happen even when the asset no longer exists on disk.

### UX Enhancements
- Keep search popover stateful (keyboard navigation, focus) without reallocating UI elements every keystroke; reuse pooled `VisualElement` instances.
- Surface attribute requirements (for example, `CustomDataVisualizationAttribute`) in the type-add popover, possibly with quick actions to add attribute snippets.
- Provide user feedback when long operations (bulk label updates, processors) run, using pooled overlays or progress bars to avoid GC pressure.
- Fix move action button styling to match the circular appearance used for clone/rename/delete controls.

## Performance and Correctness Opportunities
- Measure key operations (initial scan, search, filter, reorder) with the new instrumentation to baseline improvements; target zero GC allocations in steady-state interactions.
- Explore background GUID refresh via `EditorApplication.delayCall` to keep the UI responsive during large project scans.
- Cache `SerializedObject` instances per GUID with version checks to reuse inspector state without rescan when practical.

## Automated Testing Plan

### Runtime Tests (`Tests/Runtime/`)
- **BufferPoolsTests**: verify pool reuse, capacity guarantees, and disposal for `List`, `HashSet`, `Array`, and `Stopwatch` helpers.
- **DirectoryHelperTests**: confirm path sanitization and Unity-relative conversion logic with edge cases (project root, nested folders, invalid input).

### Editor Tests (`Tests/Editor/`)
- **AssetIndexTests**: simulate asset import/delete/label changes using `AssetDatabase` APIs in a temporary folder; assert caches update without loading live objects unnecessarily.
- **LabelFilteringTests**: feed synthetic cached metadata and ensure AND/OR combinations handle empty, large, and duplicate label sets while keeping allocations stable (use Unity GC allocation tracking).
- **SearchServiceTests**: mock metadata with nested structures to cover reflection search depth, circular references, and pagination boundaries.
- **ListViewIntegrationTests**: instantiate the editor window in edit-mode tests, populate fake assets, and verify scroll position, selection persistence, and drag reorder state using UI Toolkit test helpers.
- **ProcessorInvocationTests**: register a stub processor that records inputs; run it against filtered selections to confirm logic respects filter mode and that saves trigger only when required.
- **PersistenceDebounceTests**: invoke multiple state mutations within short intervals and assert only one disk write occurs, plus a final flush on window close.

### Performance Guardrails
- Add targeted allocation tests with `UnityEngine.Profiling.Recorder` or `GC.AllocRecorder` around search/filter/rebuild flows to enforce zero-allocation regressions.
- Use large synthetic datasets (thousands of GUID entries) to confirm responsiveness and ensure caching keeps load times within acceptable bounds.

## Additional Suggestions
- Track before-and-after profiling snapshots in documentation to demonstrate improvements and guard against regressions.
- Consider exposing a developer "Diagnostics" mode in the window that displays cache status, recent asset events, and per-operation timings.
- Schedule regular CSharpier runs and include the new pooling files in style enforcement to keep formatting consistent.
