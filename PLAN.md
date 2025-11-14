## Data Visualizer Issues & Mitigation Plan

### 1. Custom object ordering silently ignored (show‑stopper)
- **Problem**: Drag/drop or “move to top/bottom” actions store GUID order (`UpdateAndSaveObjectOrderList` → `TypeObjectOrder.ObjectGuids`), but the async path (`LoadObjectBatch`) re-sorts every loaded asset alphabetically before inserting, and the insert loop never references the saved order. On any refresh or type switch the UI reverts to name-sorted order, so the feature can’t persist user intent.
- **Impact**: Users cannot maintain curated sequences; paging and selection history desync from the visual order; all stored metadata is misleading noise in settings/user state files.
- **Mitigation sketch**:
  1. When building the priority GUID list, treat the entire saved order as the canonical ordering instead of re-sorting alphabetically. Keep a dictionary mapping GUID → desired index.
  2. While loading batches, append objects to `_selectedObjects` in the order their GUIDs appear in `_pendingObjectGuids`, avoiding any alphabetical comparer when a custom order exists.
  3. For types without saved order fall back to deterministic name/path sort.
  4. Add regression coverage: load, reorder, refresh, assert order persists. Consider a lightweight edit-mode test that fakes AssetDatabase via `AssetDatabaseTesting` or a carved-out service.

### 2. Search cache pins every ScriptableObject instance in memory
- **Problem**: `PopulateSearchCacheAsync` loads every managed asset (`AssetDatabase.LoadMainAssetAtPath`) and stores live `ScriptableObject` references in `_allManagedObjectsCache`. Search, label suggestions, and filters enumerate that list. Nothing ever releases those objects until the window closes.
- **Impact**: Large teams with thousands of ScriptableObjects pay the cost of instantiating and retaining all assets, spiking Editor memory/GC, defeating the purpose of async batching, and risking OOMs.
- **Mitigation sketch**:
  1. Change `_allManagedObjectsCache` to hold lightweight metadata (GUID, name, type, labels) instead of the asset instance.
  2. Populate labels lazily: cache GUIDs, and fetch labels via `AssetDatabase.GetLabels` on demand or snapshot them once, releasing the object immediately.
  3. Add a cap / LRU eviction so only the most recent search hits materialize objects, or expose an opt-in toggle for “preload assets for search.”
  4. Verify by stress-testing with thousands of assets while profiling allocations before/after.

### 3. Async batches redo global scans & use quadratic inserts
- **Problem**: Each call to `UpdateLoadingIndicator` re-runs `AssetDatabase.FindAssets` for the current type, so the entire project is rescanned for every 100-item batch. `_selectedObjects` insertion uses `Contains` + linear search, yielding O(n²) behavior as the list grows.
- **Impact**: Loading thousands of objects still hangs the UI for long stretches. Gains from batching vanish; progress indicator becomes the slowest part.
- **Mitigation sketch**:
  1. Cache `allGuids.Length` from the initial discovery and pass it through, so progress updates run in O(1) with no extra asset queries.
  2. Replace repeated insertion loops with either:
     - maintaining `_selectedObjects` in the exact `_pendingObjectGuids` order (append-only), or
     - building a temporary list per batch, concatenating, and resorting once if needed.
  3. Guard `LoadObjectBatch` with a profiler marker and confirm the new algorithm stays linear.

### 4. Duplicate async loads on startup
- **Problem**: `RestorePreviousSelection` invokes `LoadObjectTypesAsync` directly, then selects the namespace/type, which triggers `NamespaceController.SelectType` → `LoadObjectTypesAsync` a second time.
- **Impact**: Every window open double-loads the same GUID sets, causing redundant work, flickering indicators, and extra allocations.
- **Mitigation sketch**:
  1. Teach `SelectType` to no-op if the target type is already the selected `_asyncLoadTargetType` and a load is in progress.
  2. Alternatively, have `RestorePreviousSelection` request a selection through the controller and rely on its single `LoadObjectTypesAsync` call.
  3. Add a logging assertion when duplicate loads are requested for the same type during the same frame, to catch future regressions.

### Validation & Follow-up
- After applying mitigations, run `dotnet tool run csharpier -- format Editor Runtime`, then execute both Unity test suites once they exist.
- Add profiling notes (before/after) for async load and search cache memory.
- Document the “large project” performance expectations in `README.md` so integrators know the window won’t instantiate every asset anymore.
