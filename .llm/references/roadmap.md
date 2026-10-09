# Modernization Roadmap: Areas T01-T12

Detail behind the roadmap table in `PLAN.md`. Areas are priorities and outcome
definitions, not a queue: select one bounded slice per run (`GOAL.md`). Mark an
item delivered only when the work is on the default branch; keep PR and issue
references as the evidence.

Status legend: `done` (all known scope on main), `partial`, `open` (no known
scope delivered). Re-verify against GitHub issues before selecting work; issues
override this file when they disagree.

## T01 - Local validation harness and baseline (partial)

Ad hoc measurements exist per task (probes recorded on issues and PRs); no
standing harness.

- [ ] Local driver under `scripts/` with host-project, editor-version,
      fixture-size, suite, and output-directory arguments; callable through the
      Unity MCP bridge and direct host execution with explicit path resolution.
- [ ] Disposable fixture project referencing this package; deterministic
      datasets of 100 / 1,000 / 10,000 / 50,000 assets (one large type,
      many-type distribution, `BaseDataObject` subclasses, labels, nested
      fields, duplicate short names, saved selections); verified seeds.
- [ ] Record environment (package/fixture revision, Unity version, OS, CPU,
      memory, storage, GPU, DPI, theme, window size) and run timing inside
      Unity with monotonic timers and profiler markers; MCP transport excluded.
- [ ] Machine-readable JSON results plus a readable comparison report.
- [ ] Measure separately: clean vs incremental compiles; shell construction,
      first visible rows, inspector readiness, full indexing; search, selection,
      filtering, scroll, type switches; cold index, warm disk, warm session;
      allocations and realized elements; Play entry with package absent,
      closed, idle, indexing. Five warm-ups and 30+ repetitions for warm
      scenarios; 5+ samples with spread for expensive scenarios. Package-absent
      control uses an ordinary-ScriptableObject fixture.

Done when: the baseline is reproducible locally, distinguishes cold from warm
work, and identifies costs without changing normal package behavior.

## T02 - Compatibility and persistence characterization (partial)

Delivered: runtime assembly surface pins (#88), persisted-state contract pins
(#89), runtime signature pins with per-accessor access/virtualness (#90, #92),
enum contract tests, malformed user-JSON fail-closed parser (#24), identity
regressions, README accuracy corrections (#106).

- [ ] `Tests/Runtime` PlayMode coverage: `BaseDataObject` lifecycle callbacks
      and serialized asset-state persistence, in a correctly constrained
      assembly; keep filesystem/editor-callback tests in EditMode.
- [ ] Behavior inventory still to capture as executable coverage: duplicate
      type short names, missing scripts/assets, removed managed types, missing
      saved selections, empty projects, clone/create/rename paths, both
      persistence modes (dirty state, idempotent writes, mode switching).
- [ ] Runtime compatibility fixtures deriving from `BaseDataObject`,
      implementing extension interfaces, and overriding virtuals; preserve
      fields and `.meta` GUIDs when moving files.

Done when: behavior that must survive refactoring has executable coverage and
saved-state examples suitable for migration tests.

## T03 - Compilation and player boundary (partial)

Delivered: unused reflection-emit accessors removed (#84), editor-only helpers
moved to the editor assembly (#87), npm/UPM payload slimmed (#102, closes #32),
SonarWay enforcement with pinned installer (#94, #97, #98, #100, #102),
usings-inside-namespace lint (#124, #125).

- [ ] Player smoke projects with plain assets and with `BaseDataObject` assets;
      verify editor/test assemblies and resources are absent and retained
      runtime assets load.
- [ ] Before/after clean and incremental compile measurements with assembly
      dependency evidence; report only improvements beyond baseline variation.
- [ ] Keep the runtime/editor split by default; add an assembly only when a T01
      experiment proves a compile benefit without API or serialization break.

Done when: player compatibility is preserved, unnecessary code is removed, and
compilation results are documented.

## T04 - Service extraction and work ownership (partial)

Delivered: index classification defers while the AssetDatabase is busy (#103),
GUID normalization defers while busy with idle re-normalization (#105),
Play Mode mutation gate and suspension (#101), event-driven debounced splitter
persistence (#93).

- [ ] Extract focused editor services (type catalog, indexing/search,
      persistence, asset operations) in small steps with the T02 suite green;
      no framework, service locator, or DI dependency.
- [ ] One owner for queued work, subscriptions, cancellation; generation
      identifiers so stale callbacks never update current state; idempotent
      disposal across close, disable, destroy, compilation, domain reload.
- [ ] Separate logical state updates from view refresh and persistence writes.
- [ ] Dormant startup: closed-window import callbacks may mark invalidation but
      must not load assets or build indexes.

Done when: services are exercisable without opening a window, work ownership is
explicit, and no duplicate old/new implementation paths remain.

## T05 - Incremental metadata index and lazy loading (open)

- [ ] Metadata records keyed by asset GUID (path, exact type identity, display
      name, description, labels, ordering inputs, search payload, status);
      assembly-qualified identity internally; never resolve duplicate short
      names by first match.
- [ ] Cheap identity/path catalog before loading objects; prioritize saved
      selection and visible rows.
- [ ] Versioned disposable cache under `Library/DataVisualizer/` with schema
      marker and fingerprints; warm cache reads without synchronous full
      reconciliation before first paint.
- [ ] Handle import, delete, move, label edits, serialized edits, Undo/Redo,
      recompile; coalesce changes; reject irrelevant changes without loading
      every `.asset`.
- [ ] Cooperative slices targeting 4 ms with cancellation; main-thread Unity
      object access; release package-owned references; never use loaded-object
      count as list length.
- [ ] Preserve custom order and selection across partial loads.

Tests: corrupt/incompatible caches, changes while closed, interrupted indexing,
type switching during loads, import bursts, duplicate GUIDs, selected-asset
deletion.

Done when: the 10k fixture is interactive before full indexing, warm metadata
is reused correctly, and retained live-object references stay bounded.

## T06 - Search and rendering performance (partial)

Delivered: search match-source plumbing and highlight fixes (#77, #79, and
related); current search behavior documented on the docs site (#134).

- [ ] Share the T05 index between browsing and global search; extract
      searchable strings once per invalidation; cache reflection traversal
      metadata by type with cycle and null handling.
- [ ] Show indexed matches during incomplete indexing with explicit status;
      rerun the live query as updates arrive; cancel superseded queries.
- [ ] Metadata-backed `itemsSource` on the existing fixed-height `ListView`
      with reusable rows and targeted refresh; `fixedItemHeight` from density
      metrics.
- [ ] Virtualized flattened namespace/type navigation compatible with Unity
      2021.3; preserve collapse state and drag/drop ordering.
- [ ] No inspectors, `SerializedObject` bindings, or custom GUI for offscreen
      rows; batch GUID-to-row index updates.

Done when: indexed search meets the T12 timing target and realized visual
elements stay bounded by the viewport as dataset size grows.

## T07 - Persistent, responsive, deterministic layout (partial)

Delivered: debounced geometry persistence with close/reload flush and teardown
fixes (#93), density-token foundation and theme variants (#116, #119, #120).

- [ ] Versioned per-user layout state (pane widths, floating geometry, scale
      mode and manual scale) with one-time migration from EditorPrefs;
      distinguish preferred from clamped dimensions.
- [ ] Restore after valid geometry; suppress writes during restoration; let
      Unity own docked placement; remove platform-specific monitor P/Invoke
      paths.
- [ ] Auto scale plus manual 80-150% in 5% steps and Reset; derive package
      fonts, spacing, control heights, and row heights from one metrics model.
- [ ] Responsive panes: collapse navigation behind a toggle, list/detail
      navigation when panes cannot fit, restore preferred arrangement later.
- [ ] Normalize control padding, borders, alignment, and icons through shared
      USS classes and metrics; clamp popovers to window bounds.

Done when: preferred geometry survives restoration without drift and
package-owned alignment is within one physical pixel under equivalent
conditions.

## T08 - Play Mode overhead and suspension (partial)

Delivered: Play Mode suspension, paused indicator, mutation and serialized
inspector gating, resume path (#101).

- [ ] Matched local benchmark showing added p95 Play-entry latency of at most
      20 ms (report window-open and window-closed separately).
- [ ] Exercise all four domain-reload/scene-reload combinations, including
      transitions during indexing/search; queue and coalesce invalidation while
      paused; resume incrementally.
- [ ] Verify closing the window leaves no scheduler, polling loop, or live
      asset cache.

Done when: the benchmark and lifecycle regression tests demonstrate correct
cleanup with reload disabled.

## T09 - Public editor API and JSON automation (open)

- [ ] Documented facade in the editor namespace: configuration read/apply,
      type discovery, paged metadata queries, index progress, selection, asset
      operations, processor execution; GUID and assembly-qualified identifiers;
      cancellation and progress; index completeness in results.
- [ ] UI mutations route through these services; mutation preview without side
      effects; no modal dialogs; per-item outcomes; reject mutations while
      playing.
- [ ] JSON schema version 1 with published schemas and examples; explicit
      dispatch; file-based `-executeMethod` entrypoint plus an in-process
      entrypoint via the existing MCP tooling; no daemons or HTTP listeners.
- [ ] Validate schema versions, targets, identities, paths, values; idempotent
      configuration application.

Tests: UI-service vs JSON equivalence, preview side effects, malformed input,
partial failures, Play Mode rejection.

Done when: an agent can configure, query, and operate the package through
documented C# or JSON without opening the window or using private reflection.

## T10 - Automated local editor captures (partial)

Delivered: Tests-assembly offscreen capture with fail-closed guards (#123),
docs-image capture driver with shot manifest and `-executeMethod` CLI (#125).

Blocked: the offscreen capture deterministically omits specific subtrees
(unselected rows, asset-name/labels rows, InspectorElement internals) while the
on-screen window paints them; twelve falsified workarounds recorded on #114.

- [ ] Fix the render gap; then regenerate the documented image inventory from
      production UI.
- [ ] Scenario inventory and manifest validation (nonempty, distinct, expected
      dimensions); staged publication preserving the previous complete set;
      cleanup of textures, windows, and settings.
- [ ] Geometry assertions cross-platform; screenshot baselines pinned to
      OS/Unity/theme/DPI; changed baselines require inspection.

Done when: one local command regenerates the documented image inventory with
repeatable evidence and no consumer dependency or CI Unity execution.

## T11 - Documentation and GitHub Pages (partial)

Delivered: MkDocs Material foundation with strict build and Pages publishing
workflow (#130), guide pages for search/filtering, asset management,
organizing, and extending (#134).

- [ ] README hero imagery from the capture pipeline (blocked on T10 render
      gap); README stays concise and links into the site.
- [ ] Troubleshooting page (incomplete indexing, stale metadata, missing
      scripts, small windows, capture limits, host/container paths).
- [ ] Measured performance section once T01 produces numbers; captures embedded
      with captions and alt text, validated against the manifest.
- [ ] Odin behavior documented separately from the default inspector contract.

Done when: the published site is accurate with working examples and complete
screenshots; unavailable publication access is reported explicitly.

## T12 - Acceptance matrix and release evidence (partial)

Delivered: Keep a Changelog file and package metadata (#107), release
preparation (#108), tag-driven publish (#111), deterministic `.unitypackage`
builder and fail-closed validator (#112, #113), publish-time package build,
validation, and GitHub Release with npm + `.unitypackage` artifacts (#126),
credential documentation pinned to workflows (#128), unified release
preparation and publication (#135), v0.1.0 shipped (#136).

- [ ] Local compatibility matrix: pinned Unity 2021.3, 2022.3, representative
      Unity 6 LTS, and the confirmed 6000.4.6f1 host; Windows/macOS/Linux;
      light/dark themes, 100/150/200% DPI, manual scale endpoints, docking,
      mixed-monitor movement; EditMode + PlayMode + player smoke; optional Odin
      where licensed; EntityId branch when Unity 6000.5+ is available. Treat
      unavailable combinations as unverified and publish the gaps.
- [ ] Performance acceptance: warm open p95 <= 100 ms at 10k; indexed search
      p95 <= 100 ms including debounce; Play-entry p95 <= 20 ms added; 4 ms
      cooperative slices; cold indexing measured; compile improvement beyond
      noise; realized rows bounded by viewport; no package-owned memory
      accumulation. Missed targets are recorded, not silently relaxed.
- [ ] Final milestone acceptance and handoff summary (what changed, what was
      tested, measured improvements, unverified platforms).

Done when: the support and performance matrix is published with unverified gaps
explicit; Unity validation remains a required local release gate.

## Sources and implementation references

Inspect upstream source and licenses at the pinned revision before adapting
code. Check API capabilities against the minimum supported Unity version.

- [Unity 2021.3 ListView](https://docs.unity3d.com/2021.3/Documentation/Manual/UIE-uxml-element-ListView.html):
  recycling, fixed-height virtualization, targeted refresh.
- [Unity assembly definitions](https://docs.unity3d.com/2021.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html):
  assembly boundaries and compilation dependencies.
- [Unity domain reloading](https://docs.unity3d.com/2021.3/Documentation/Manual/DomainReloading.html):
  lifecycle when reload is disabled.
- [Unity Helpers capture](https://github.com/Ambiguous-Interactive/unity-helpers/tree/main/Tests/Editor/Capture):
  host windows, sentinels, documentation image tests.
- [DxMessaging](https://github.com/ambiguous-interactive/DxMessaging):
  capture inventory and staged publication patterns; release-prepare and
  release-tag workflows for the tag handoff shape.
- [Unity Helpers release workflow](https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/.github/workflows/release.yml):
  artifact identity, checksums, rerun behavior; replace its Unity export with
  the no-Unity archive builder.
- [No-Unity `.unitypackage` builder example](https://github.com/premium-ads/max-adapter-unity/blob/main/scripts/pack-unitypackage.sh):
  GUID-directory gzip/tar packages from committed `.meta` files.
