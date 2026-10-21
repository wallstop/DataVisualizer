# Modernization Roadmap: Areas T01-T12

Detailed future scope linked from `PLAN.md`. Areas are priorities and outcome definitions, not a queue: select one bounded slice per run (`GOAL.md`). Mark an item delivered only
when the work is on the default branch; keep PR and issue references as the evidence.

Status legend: `done` (all known scope on main), `partial`, `open` (no known scope delivered). Re-verify against GitHub issues before selecting work; issues override this file when
they disagree.

## Current improvement initiative

[Issue #305](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/305) owns the 2026-10-19 audit and its prioritized tasks. Keep Unity 2021.3, the three-pane workflow,
runtime contracts, authored IDs, and saved arrangements. Prioritize ScriptableObject workflows and measured 10k–100k operation. Prefabs and million-asset support require
feasibility evidence before a support commitment. Priorities order investigation; they do not authorize implementing the whole backlog in one run.

| Area                                            | Owning tasks                                                                                                                                                                                                                 |
| ----------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Measurement and scale                           | [#306](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/306), [million-record feasibility #323](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/323)                                                |
| Incremental refresh and metadata                | [#308](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/308), [#309](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/309)                                                                           |
| Search and catalog rendering                    | [#310](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/310), [#311](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/311), [#324](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/324) |
| Controls, panes, and filters                    | [#312](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/312), [#313](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/313), [#314](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/314) |
| Editor API and JSON                             | [#315](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/315), [#316](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/316)                                                                           |
| Selected processing, saved searches, validation | [#317](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/317), [#318](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/318), [#319](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/319) |
| Subasset and prefab feasibility                 | [#320](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/320), [#321](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/321)                                                                           |
| Documentation correctness and task guides       | [#307](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/307), [#322](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/322)                                                                           |

Each issue owns its evidence, acceptance, dependencies, and exclusions. Reproduce or measure first, change one bounded behavior, then repeat the same scenario. Separate static
findings, historical measurements, and exploratory UX hypotheses. Do not infer performance from virtualization alone or million-asset support from synthetic metadata.

## T01 - Local validation harness and baseline (partial)

Delivered: `Tests/Editor/PerfBaseline.cs` provides a tick-driven local driver, monotonic timing, raw JSON and environment reporting. The 100/1,000/10,000 report is published in
`Tests/Artifacts/performance.md` (#244); background counting was corrected in #246. The original raw report remains unchanged with its limitation documented.

- [ ] Extend the existing driver with explicit scenario/fixture controls and stage timings; reuse its host/MCP entrypoints rather than create another harness (#306).
- [ ] Disposable fixture project referencing this package; deterministic datasets of 100 / 1,000 / 10,000 / 50,000 / 100,000 assets (one large type, many-type distribution,
      `BaseDataObject` subclasses, labels, nested fields, duplicate short names, saved selections); verified seeds.
- [ ] Record environment (package/fixture revision, Unity version, OS, CPU, memory, storage, GPU, DPI, theme, window size) and run timing inside Unity with monotonic timers and
      profiler markers; MCP transport excluded.
- [ ] Extend existing machine-readable JSON with matched before/after comparisons and missing timing/memory metrics.
- [ ] Measure separately: clean vs incremental compiles; shell construction, first visible rows, inspector readiness, full indexing; search, selection, filtering, scroll, type
      switches; cold index, warm disk, warm session; allocations and realized elements; Play entry with package absent, closed, idle, indexing. Five warm-ups and 30+ repetitions
      for warm scenarios; 5+ samples with spread for expensive scenarios. Package-absent control uses an ordinary-ScriptableObject fixture.

Done when: the baseline is reproducible locally, distinguishes cold from warm work, and identifies costs without changing normal package behavior.

## T02 - Compatibility and persistence characterization (partial)

Delivered: runtime assembly surface pins (#88), persisted-state contract pins (#89), runtime signature pins with per-accessor access/virtualness (#90, #92), enum contract tests,
malformed user-JSON fail-closed parser (#24), identity regressions, README accuracy corrections (#106).

- [x] `Tests/Runtime` contains `BaseDataObjectLifecycleTests` and `BaseDataObjectSerializationTests`; keep filesystem/editor-callback tests in EditMode.
- [ ] Behavior inventory still to capture as executable coverage: duplicate type short names, missing scripts/assets, removed managed types, missing saved selections, empty
      projects, clone/create/rename paths, both persistence modes (dirty state, idempotent writes, mode switching).
- [ ] Runtime compatibility fixtures deriving from `BaseDataObject`, implementing extension interfaces, and overriding virtuals; preserve fields and `.meta` GUIDs when moving
      files.

Done when: behavior that must survive refactoring has executable coverage and saved-state examples suitable for migration tests.

## T03 - Compilation and player boundary (partial)

Delivered: unused reflection-emit accessors removed (#84), editor-only helpers moved to the editor assembly (#87), npm/UPM payload slimmed (#102, closes #32), SonarWay enforcement
with pinned installer (#94, #97, #98, #100, #102), usings-inside-namespace lint (#124, #125).

- [ ] Player smoke projects with plain assets and with `BaseDataObject` assets; verify editor/test assemblies and resources are absent and retained runtime assets load.
- [ ] Before/after clean and incremental compile measurements with assembly dependency evidence; report only improvements beyond baseline variation.
- [ ] Keep the runtime/editor split by default; add an assembly only when a T01 experiment proves a compile benefit without API or serialization break.

Done when: player compatibility is preserved, unnecessary code is removed, and compilation results are documented.

## T04 - Service extraction and work ownership (partial)

Delivered: index classification defers while the AssetDatabase is busy (#103), GUID normalization defers while busy with idle re-normalization (#105), Play Mode mutation gate and
suspension (#101), event-driven debounced splitter persistence (#93).

- [ ] Extract focused editor services (type catalog, indexing/search, persistence, asset operations) in small steps with the T02 suite green; no framework, service locator, or DI
      dependency.
- [ ] One owner for queued work, subscriptions, cancellation; generation identifiers so stale callbacks never update current state; idempotent disposal across close, disable,
      destroy, compilation, domain reload.
- [ ] Separate logical state updates from view refresh and persistence writes.
- [ ] Dormant startup: closed-window import callbacks may mark invalidation but must not load assets or build indexes.

Done when: services are exercisable without opening a window, work ownership is explicit, and no duplicate old/new implementation paths remain.

## T05 - Incremental metadata index and lazy loading (partial)

Foundation delivered: `AssetGuidTypeIndex` classifies paths incrementally with a cooperative budget, and #228 removes object deserialization from import relevance checks. The
shared browsing/search metadata cache and change-proportional window refresh remain open (#308/#309).

- [ ] Metadata records keyed by asset GUID (path, exact type identity, display name, description, labels, ordering inputs, search payload, status); assembly-qualified identity
      internally; never resolve duplicate short names by first match.
- [ ] Cheap identity/path catalog before loading objects; prioritize saved selection and visible rows.
- [ ] Versioned disposable cache under `Library/DataVisualizer/` with schema marker and fingerprints; warm cache reads without synchronous full reconciliation before first paint.
- [ ] Handle import, delete, move, label edits, serialized edits, Undo/Redo, recompile; coalesce changes; reject irrelevant changes without loading every `.asset`.
- [ ] Cooperative slices targeting 4 ms with cancellation; main-thread Unity object access; release package-owned references; never use loaded-object count as list length.
- [ ] Preserve custom order and selection across partial loads.

Tests: corrupt/incompatible caches, changes while closed, interrupted indexing, type switching during loads, import bursts, duplicate GUIDs, selected-asset deletion.

Done when: the 10k fixture is interactive before full indexing, warm metadata is reused correctly, and retained live-object references stay bounded.

## T06 - Search and rendering performance (partial)

Delivered: search match-source plumbing and highlight fixes (#77, #79, and related); current search behavior documented on the docs site (#134).

- [ ] Share the T05 index between browsing and global search; extract searchable strings once per invalidation; cache reflection traversal metadata by type with cycle and null
      handling.
- [ ] Show indexed matches during incomplete indexing with explicit status; rerun the live query as updates arrive; cancel superseded queries.
- [ ] Metadata-backed `itemsSource` on the existing fixed-height `ListView` with reusable rows and targeted refresh; `fixedItemHeight` from density metrics.
- [ ] Virtualized flattened namespace/type navigation compatible with Unity 2021.3; preserve collapse state and drag/drop ordering.
- [ ] No inspectors, `SerializedObject` bindings, or custom GUI for offscreen rows; batch GUID-to-row index updates.

Done when: indexed search meets the T12 timing target and realized visual elements stay bounded by the viewport as dataset size grows.

## T07 - Persistent, responsive, deterministic layout (partial)

Delivered: debounced geometry persistence with close/reload flush and teardown fixes (#93), density-token foundation and theme variants (#116, #119, #120).

- [ ] Versioned per-user layout state (pane widths, floating geometry, scale mode and manual scale) with one-time migration from EditorPrefs; distinguish preferred from clamped
      dimensions.
- [ ] Preserve valid-geometry restoration and Unity-owned docking while adding responsive panes. Prefer public geometry APIs; retain necessary tested platform interop unless an
      equivalent replacement is demonstrated. Placement fixes are delivered; remaining hardware evidence belongs to #270.
- [ ] Auto scale plus manual 80-150% in 5% steps and Reset; derive package fonts, spacing, control heights, and row heights from one metrics model.
- [ ] Responsive panes: collapse navigation behind a toggle, list/detail navigation when panes cannot fit, restore preferred arrangement later.
- [ ] Normalize control padding, borders, alignment, and icons through shared USS classes and metrics; clamp popovers to window bounds.

Done when: preferred geometry survives restoration without drift and package-owned alignment is within one physical pixel under equivalent conditions.

## T08 - Play Mode overhead and suspension (partial)

Delivered: Play Mode suspension, paused indicator, mutation and serialized inspector gating, resume path (#101).

Issue #20 records the four reload configurations, repeated in-flight transitions, asset invalidation, compilation, reopening, and destroy/disable cleanup on Unity 6000.4.6f1. This
is historical lifecycle evidence, not proof of the timing target or other editor versions. Re-exercise affected scenarios when indexing ownership changes.

- [ ] Matched local benchmark showing added p95 Play-entry latency of at most 20 ms (report window-open and window-closed separately).
- [x] Exercise all four domain-reload/scene-reload combinations with in-flight work on the recorded 6000.4 host (#20).
- [x] Exercise close/destroy/disable cleanup and reopening on that host (#20).
- [ ] Preserve those lifecycle guarantees under incremental refresh and metadata service changes (#308/#309), including retained-reference measurements (#306).

Done when: the benchmark and lifecycle regression tests demonstrate correct cleanup with reload disabled.

## T09 - Public editor API and JSON automation (open)

- [ ] Documented facade in the editor namespace: configuration read/apply, type discovery, paged metadata queries, index progress, selection, asset operations, processor execution;
      GUID and assembly-qualified identifiers; cancellation and progress; index completeness in results.
- [ ] UI mutations route through these services; mutation preview without side effects; no modal dialogs; per-item outcomes; reject mutations while playing.
- [ ] JSON schema version 1 with published schemas and examples; explicit dispatch; file-based `-executeMethod` entrypoint plus an in-process entrypoint via the existing MCP
      tooling; no daemons or HTTP listeners.
- [ ] Validate schema versions, targets, identities, paths, values; idempotent configuration application.

Tests: UI-service vs JSON equivalence, preview side effects, malformed input, partial failures, Play Mode rejection.

Done when: an agent can configure, query, and operate the package through documented C# or JSON without opening the window or using private reflection.

## T10 - Automated local editor captures (partial)

Delivered: Tests-assembly offscreen capture with fail-closed guards (#123), docs-image capture driver with shot manifest and `-executeMethod` CLI (#125).

Delivered: the reviewed Windows Unity 6000.5 six-shot capture inventory and WebP/GIF exporters are recorded in closed #114/#152. The remaining macOS 6000.4 render gap is
platform-specific and belongs to #270; it does not block all capture work.

- [x] Capture inventory and image validation are implemented; inspect content in addition to dimension checks.
- [ ] Regenerate affected production UI captures as workflow changes land (#322); preserve the previous complete set on failed capture/export.
- [ ] Geometry assertions cross-platform; screenshot baselines pinned to OS/Unity/theme/DPI; changed baselines require inspection.

Done when: one local command regenerates the documented image inventory with repeatable evidence and no consumer dependency or CI Unity execution.

## T11 - Documentation and GitHub Pages (partial)

Delivered: MkDocs Material foundation with strict build and Pages publishing workflow (#130), guide pages for search/filtering, asset management, organizing, and extending (#134).

- [x] README hero imagery from the capture pipeline (#140, #143); README stays concise and links into the site.
- [x] Troubleshooting page (incomplete indexing, stale metadata, missing scripts, small windows, capture limits, host/container paths) (#145).
- [x] Measured performance section with the complete 100/1,000/10,000 baseline (Tests/Artifacts/performance.md and its raw report; #244). Background-count correction merged in
      #246.
- [x] Current six-shot captures embedded with captions and alt text; earlier overhaul closed in #114.
- [x] Odin behavior documented separately from the default inspector contract (docs/extending.md, #134).
- [ ] Correct newly verified filter and persisted-ID claims and compile complete examples (#307).
- [ ] Separate contributor capture procedures from user guides; refresh affected screenshots and label or replace the pre-rename video (#322).

Done when: the published site is accurate with working examples and complete screenshots; unavailable publication access is reported explicitly.

## T12 - Acceptance matrix and release evidence (partial)

Delivered: Keep a Changelog file and package metadata (#107), release preparation (#108), tag-driven publish (#111), deterministic `.unitypackage` builder and fail-closed validator
(#112, #113), publish-time package build, validation, and GitHub Release with npm + `.unitypackage` artifacts (#126), credential documentation pinned to workflows (#128), unified
release preparation and publication (#135), v0.1.0 shipped (#136).

- [ ] Local compatibility matrix: pinned Unity 2021.3, 2022.3, representative Unity 6 LTS, and the confirmed 6000.5.2f1 host; Windows/macOS/Linux; light/dark themes, 100/150/200%
      DPI, manual scale endpoints, docking, mixed-monitor movement; EditMode + PlayMode + player smoke; optional Odin where licensed; EntityId branch when Unity 6000.5+ is
      available. Treat unavailable combinations as unverified and publish the gaps.
- [ ] Performance acceptance: warm usable open p95 <= 100 ms at 10k; indexed search p95 <= 100 ms including debounce; Play-entry p95 <= 20 ms added; 4 ms cooperative slices; cold
      indexing measured; compile improvement beyond noise; realized rows bounded by viewport; no package-owned memory accumulation. Missed targets are recorded, not silently
      relaxed.
- [ ] Record first paint, usable rows, inspector readiness and full index readiness separately. Preserve historical open-to-index-ready results rather than compare unlike metrics.
- [ ] Establish numeric 100k latency budgets from #306 before optimizing; require bounded live-object retention, responsive cancellation, and change-proportional refresh.
- [ ] Final milestone acceptance and handoff summary (what changed, what was tested, measured improvements, unverified platforms).

Done when: the support and performance matrix is published with unverified gaps explicit; Unity validation remains a required local release gate.

## Sources and implementation references

Inspect upstream source and licenses at the pinned revision before adapting code. Check API capabilities against the minimum supported Unity version.

- [Unity 2021.3 ListView](https://docs.unity3d.com/2021.3/Documentation/Manual/UIE-uxml-element-ListView.html): recycling, fixed-height virtualization, targeted refresh.
- [Unity assembly definitions](https://docs.unity3d.com/2021.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html): assembly boundaries and compilation
  dependencies.
- [Unity domain reloading](https://docs.unity3d.com/2021.3/Documentation/Manual/DomainReloading.html): lifecycle when reload is disabled.
- [Unity Helpers capture](https://github.com/Ambiguous-Interactive/unity-helpers/tree/main/Tests/Editor/Capture): host windows, sentinels, documentation image tests.
- [DxMessaging](https://github.com/ambiguous-interactive/DxMessaging): capture inventory and staged publication patterns; release-prepare and release-tag workflows for the tag
  handoff shape.
- [Unity Helpers release workflow](https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/.github/workflows/release.yml): artifact identity, checksums, rerun behavior;
  replace its Unity export with the no-Unity archive builder.
- [No-Unity `.unitypackage` builder example](https://github.com/premium-ads/max-adapter-unity/blob/main/scripts/pack-unitypackage.sh): GUID-directory gzip/tar packages from
  committed `.meta` files.
