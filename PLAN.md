# Data Visualizer Architectural Refactor Plan

## Current Observations
- `Editor/DataVisualizer/DataVisualizer.cs`: ~9k lines combining window lifecycle, data loading, search, label management, drag and drop, persistence, and popover UI; violates the Single Responsibility and Interface Segregation principles and is difficult to reason about.
- `Editor/DataVisualizer/NamespaceController.cs`: mixes visual tree construction, ordering logic, deletion confirmation, and persistence writes, relying on internal fields exposed by `DataVisualizer`, creating tight coupling.
- Many `internal` mutable fields across modules (for example `_scriptableObjectTypes`, `_selectedObjects`, `_activePopover`) are shared instead of encapsulated, making invariants unclear and discouraging unit testing.
- Asset, processor, and label workflows are tightly interwoven with UI code, so performance optimizations (caching, batching) require editing UI handlers—risking regressions.
- Persistence concerns (`DataVisualizerSettings`, `DataVisualizerUserState`, EditorPrefs, JSON files) leak throughout the window logic, making it hard to reason about data flow and error handling.
- Search, filtering, and drag operations allocate heavily and rebuild UI trees wholesale due to lack of separation between models and views.

## Refactoring Objectives
- Reduce `DataVisualizer` to a thin composition root that delegates to focused controllers adhering to SOLID principles.
- Decouple domain state (types, assets, labels, processors) from UI representation so that logic can be unit-tested and reused.
- Preserve and improve performance by keeping existing caches fast, enabling incremental updates, and avoiding redundant UI rebuilds.
- Clarify persistence and configuration flows with dedicated services, making it easier to reason about state serialization and migration.
- Establish clear extension seams for new processors, filters, and UI panels without touching core window code.

## Target Architecture Overview
- **State Layer (`Editor/DataVisualizer/State/`)**: Immutable or narrowly mutable records (`VisualizerSessionState`, `NamespaceState`, `ObjectListState`, `LabelFilterState`, `ProcessorPanelState`) describing current selections, pagination, filters, and cached metadata.
- **Service Layer (`Editor/DataVisualizer/Services/`)**: Interfaces such as `IDataAssetService`, `IUserStateRepository`, `IProcessorRegistry`, `ISearchService`, `ILabelService`, with concrete implementations wrapping `AssetIndex`, `AssetDatabase`, and persistence assets. These enforce Dependency Inversion and allow mocking in tests.
- **Controller Layer (`Editor/DataVisualizer/Controllers/`)**: UI Toolkit-focused classes (for example `NamespacePanelController`, `ObjectListController`, `LabelPanelController`, `ProcessorPanelController`, `SearchPopoverController`, `PopoverManager`, `DragAndDropController`) that translate state into visual elements and raise events.
- **Event Hub (`Editor/DataVisualizer/Events/`)**: Lightweight mediator publishing high-level events (`TypeSelected`, `ObjectsRequested`, `LabelsChanged`, `SearchRequested`) so controllers depend on abstractions instead of one another.
- **DataVisualizer Window**: Retains lifecycle (`OnEnable`, `OnDisable`), creates services/controllers, wires them through the event hub, forwards Unity callbacks, and persists layout metrics.
- **Runtime Contracts (`Runtime/DataVisualizer/`)**: Preserve existing interfaces (`IDataProcessor`, `IDisplayable`, etc.) but add small DTOs if needed for controller-service communication.

## Execution Roadmap

### Priority 0 – Safeguards and Baseline
1. Document feature inventory and critical user journeys (type selection, object creation, label editing, processor execution, search, drag reorder) with before screenshots or recordings.
2. Expand editor test scaffolding to open the window in isolation and simulate interactions. Stub `AssetDatabase` with test assets to capture current behavior for regression detection.
3. Profile representative workflows to capture GC allocations and latency; store metrics to verify improvements (drag reorder, search, namespace collapse toggles).
4. Freeze public API by exporting current assembly public surface (via `dotnet` reflection or Unity API analyzer) to track changes and update `package.json` intentionally when necessary.

### Priority 1 – Core Abstractions and Infrastructure
1. Introduce `VisualizerSessionState` (selection, pagination, highlighted indices, popover status) as a serializable record under `Editor/DataVisualizer/State/`. Replace scattered fields in `DataVisualizer` with this state container.
2. Wrap persistence logic in `IUserStateRepository` with two implementations: `SettingsAssetStateRepository` and `JsonUserStateRepository`. `DataVisualizer` asks the repository to load/save; persistence code no longer leaks into controllers.
3. Extract `AssetIndex` responsibilities behind `IDataAssetService`, which exposes typed queries (`GetTypeMetadata`, `Rebuild`, `RefreshGuid`, `EnumerateLabels`). Ensure service batches index rebuilds and exposes change events.
4. Build `DataVisualizerDependencies` (factory or simple struct) to gather services and share them via dependency injection when constructing controllers.
5. Update `DataVisualizer` to use the new services/state container while still driving existing UI to guarantee behavior parity.

### Priority 2 – Namespace and Type Pane
1. Create `NamespacePanelController` with ownership of namespace list visual elements, selection handling, reordering, and collapse state. Inject `IDataAssetService`, `IUserStateRepository`, and `VisualizerSessionState`.
2. Move namespace-specific persistence into the controller, raising `TypeSelectionChanged` events via the event hub instead of calling `DataVisualizer` methods directly.
3. Simplify `NamespaceController` by either rewriting it atop the new architecture or replacing it with `NamespacePanelController`. Remove direct access to `DataVisualizer` internals (`_scriptableObjectTypes`, `_namespaceOrder`).
4. Ensure namespace rebuilds diff against cached state to minimize UI churn, keeping performance characteristics.

### Priority 3 – Object List and Selection
1. Introduce `ObjectListController` that owns the `ListView`, pagination header, and drag reorder behavior. It consumes `IDataAssetService` metadata and emits commands (`CloneRequested`, `RenameRequested`, etc.) through the event hub.
2. Create `ObjectSelectionService` to manage `_selectedObjects`, `_selectedObject`, and associated metadata (GUID tracking, highlight index). This reduces shared mutable collections.
3. Refactor object command handlers (clone, rename, move, delete) into dedicated command classes or strategies (`IObjectCommand`) scoped to services for file operations, keeping UI logic clean.
4. Preserve `ListView` virtualization and pooling while ensuring row binding pulls metadata lazily and reuses row view models to maintain performance.

### Priority 4 – Inspector, Labels, and Filters
1. Extract label management into `LabelPanelController` (UI) and `ILabelService` (data). Consolidate label caches, suggestion popovers, and AND/OR filter logic within these components.
2. Move label filter state into `LabelFilterState`, stored within `VisualizerSessionState`. Provide change notifications so object list refreshes only when filters change.
3. Introduce `LabelSuggestionProvider` that builds suggestions from `IDataAssetService` and supports incremental refresh to keep UI responsive.
4. Update inspector integration to construct serialized objects via a helper (`InspectorBindingService`), disposing them responsibly and handling Odin integration with feature flags.

### Priority 5 – Processor Pipeline
1. Create `ProcessorPanelController` for building the processor list UI, toggles, and execution actions. It observes current selection and filter state via the event hub.
2. Add `IProcessorRegistry` to surface available `IDataProcessor` implementations, caching instances and capabilities. `ProcessorPanelController` uses the registry instead of re-creating instances in `DataVisualizer.OnEnable`.
3. Introduce `ProcessorExecutionService` to run processors asynchronously or with progress reporting, ensuring results update the state and trigger asset refreshes efficiently.
4. Incorporate performance telemetry (duration, allocations) via opt-in diagnostics stored in `VisualizerSessionState` for future debugging.

### Priority 6 – Search and Popovers
1. Split search responsibilities into `SearchService` (indexing, fuzzy match, highlight data) and `SearchPopoverController` (UI). Use pooled builders and limit allocations during typing.
2. Generalize popover handling with a `PopoverManager` managing active popovers, focus restoration, and drag interactions. Replace direct `_activePopover`, `_popoverContext`, `_isDraggingPopover` fields with managed state objects.
3. Ensure search results and popovers rely on immutable view models for clarity and testability.

### Priority 7 – Drag and Drop, Input, and Layout
1. Centralize drag state in `DragAndDropController`, coordinating namespace/type/object drags via the event hub. This controller can reuse pooled visuals and handle keyboard modifiers consistently.
2. Introduce `InputShortcutController` to register global key bindings and dispatch high-level commands (NextType, PreviousType, ExecuteSearchConfirm) without referencing UI internals.
3. Extract layout persistence (split view widths, window size) into `LayoutPersistenceService`, debouncing writes and exposing load/save methods invoked by the window lifecycle.

### Priority 8 – Cleanup and API Hardening
1. Remove obsolete fields and methods from `DataVisualizer`, exposing only minimal internal APIs needed by controllers. Update `package.json` if public surface changes.
2. Delete legacy helper methods that migrated into services, ensuring old code paths are gone and tests cover new flows.
3. Review accessibility of new types (`internal sealed` within editor assembly) and ensure runtime contracts remain stable.
4. Update documentation (`README.md`) and AGENTS guidelines to reflect new architecture and extension points.

## Testing and Verification Strategy
- Expand edit mode tests to cover each controller via mocked services, asserting UI state changes without touching `AssetDatabase` when possible.
- Add integration tests that instantiate the window, inject fake services, and simulate user flows (type switch, label changes, processor run) to guard against regression.
- Add performance guard tests using Unity profiling APIs to confirm drag reorder, search updates, and namespace rebuilds remain allocation-free or within budget.
- Run targeted manual validation (large projects, thousands of assets) after each major phase to ensure responsiveness.

## Risks and Mitigations
- **Risk: Behavior changes during incremental extraction.** Mitigate by pairing each refactor with regression tests and shipping behind feature toggles where feasible.
- **Risk: Service abstractions adding overhead.** Keep interfaces lean, pass pooled data structures, and ensure controllers operate on cached metadata to avoid unnecessary allocations.
- **Risk: Editor-only dependencies leaking into runtime.** Constrain new types to editor assemblies via `.asmdef` updates and enforce editor namespace usage in CI.
- **Risk: Public API drift.** Monitor API reports and coordinate `package.json` version updates with documentation to alert downstream consumers.

## Follow-Up Opportunities
- Add a diagnostics overlay powered by the new event hub to display cache status, processor runtimes, and recent asset changes for developers.
- Evaluate moving reusable services (asset indexing, label suggestions) into separate packages for reuse across editor tooling.
- Consider asynchronous asset scanning using background tasks once services encapsulate data access, further improving editor responsiveness.
