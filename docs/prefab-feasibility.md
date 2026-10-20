# Read-only prefab-component feasibility

This experiment answers [#321](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/321). Production still browses exact ScriptableObject main assets only. The prototype is
confined to the test assembly; all prefab writes generate or change owned fixtures. The adapter and contents scope never save assets.

## Decision and boundary

Proceed only with a separately reviewed, opt-in read-only source after #309/#315. Regular, variant and nested prefab components can be discovered and inspected with explicit
identities and lifetimes. Do not enable prefab editing or expose these records through existing GUID-only SO queries. Model prefabs and missing scripts have no editable target. The
default SO browser, saved selections, authored IDs and runtime API remain unchanged by this experiment.

| Fixture            | Copied records | Verified behavior                                                |
| ------------------ | -------------- | ---------------------------------------------------------------- |
| Regular            | 3              | Transform and two BoxColliders retain separate identities        |
| Variant            | 3              | Effective collider size override is visible without applying it  |
| Nested             | 7              | Two instances of one source retain distinct component identities |
| Missing script     | 4              | One diagnostic row has no resolvable component target            |
| Imported OBJ model | 0              | Model source is explicitly excluded                              |

Group a future catalog by source kind and exact component type, then show the containing prefab and hierarchy location. Names and hierarchy strings are descriptive, not keys.
Preserve the complete [GlobalObjectId](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/GlobalObjectId.html), including its prefab-instance portion, alongside the
containing asset GUID. Resolve only a selected component; reject missing parents, mismatched parent GUIDs/types and unavailable IDs. Never select another component by position.
Tests cover duplicate names/components, reimport stability, exact resolution and the variant's effective override value. A future source must also handle parent moves/deletion and
invalidate descendants when a nested source changes; this experiment does not ship an incremental index.

## Measurements

[Warm raw samples](prefab-feasibility-warm-2026-10-20.json) use source `495dd7fc4be7174636c15f09d7c298fa58743a8c`. [Cold raw samples](prefab-feasibility-cold-2026-10-20.json) use
`1cbb22be0b0ab11ab2874a2646b07f65836e328b`. Both read the same five fixture cases. Warm discovery has five warmups and 30 samples; changed-parent reads have 30 actual fixture
content changes and imports. All five cold samples use separate editor processes and complete record counts. The two failed project-setup attempts produced no samples.

| Observation                                               | Median     | p95 / spread                   |
| --------------------------------------------------------- | ---------- | ------------------------------ |
| Warm discovery, 17 records                                | 1.102 ms   | p95 2.036 ms; max 2.144 ms     |
| Selected-component resolution and SerializedObject update | 0.539 ms   | p95 1.065 ms; max 1.400 ms     |
| Changed-parent metadata read, 3 records                   | 0.408 ms   | p95 0.768 ms; max 0.900 ms     |
| Unity import for that fixture change                      | 168.786 ms | p95 209.060 ms; max 229.954 ms |
| Isolated nested contents load/read, 5 samples             | 4.241 ms   | range 2.596–11.084 ms          |
| Fresh-process discovery, 5 samples                        | 13.304 ms  | range 12.260–15.198 ms         |

The first discovery in the warm session took 4.771 ms after fixture staging. Warm samples use Windows 11, Unity 6000.5.2f1, Snapdragon X Elite/Adreno X1-85, 96 DPI and 1 pixel per
point. Cold runs use the same installed editor in batch mode with a Null graphics device. The editor process is x64 on the ARM64 host. Storage device and OS cache state were not
measured. Fresh process means a new managed/native editor process; Library and OS caches remain warm. Startup, compilation, imports, rendering and first paint are outside the
discovery timer. These are small real-asset fixtures, not large-project latency or full browsing-frame guarantees.

Managed heap occupancy deltas across warm discovery were 0–24,576 bytes; they are not allocation or retained-memory totals. Returned metadata has zero Unity object fields. Five
isolated contents cycles left preview scene counts at 1 before/after and GameObject counts at 38 before/after. The existing preview scene was preserved. Tests also check that a
disposed contents root is destroyed and repeated disposal is safe. Unity can still cache persistent prefab assets; no total native-memory bound is claimed.

## Read-only inspection and future editing

The hosted default property inspector is disabled. Tests verify disabled property fields, unchanged asset bytes and clean component state. Fixture construction preserves the user
scene's root count and dirty flag. A named contents scope calls LoadPrefabContents and UnloadPrefabContents, never SaveAsPrefabAsset. Its lifetime is independent of persistent
component selection. Custom editors/drawers, importer-generated components and editor callbacks need separate validation; disabling controls is not a security boundary.

Future editing needs an explicit target and save contract: regular prefab writes target that asset; variants must distinguish a variant override from a base-source edit; nested
instances must distinguish a containing-prefab override from a nested-source edit. Preview must not save or dirty assets, while Apply must report per-item outcomes, Undo/recovery
limits, cancellation and Play Mode refusal through the shared operation service. Model/importer sources and package assets stay immutable. Missing scripts remain diagnostics, not
repair targets. No source adapter should silently save loaded contents, apply overrides to a base prefab, or migrate existing SO selections to prefab components.

## Reproduction and API floor

Run `PrefabFeasibilityTests` and `PrefabFeasibilityTests.RunExperiment(outputPath, sourceRevision)` with the package's test assembly enabled. Both own unique fixture folders; the
measurement driver deletes its fixtures and checks scene/object cleanup. The test-only helpers live in `WallstopStudios.DataVisualizer.Tests.Editor`.

For cold runs, use an independently owned disposable project with this package testable, Test Framework 1.7.0 and the built-in UIElements, Physics, IMGUI, ImageConversion,
Animation and JSONSerialize modules. In that project, create an empty `Assets/Probe` folder and call `PrefabFeasibilityTests.CreateFixtures("Assets/Probe")` once. Close the staging
editor, then launch five sequential processes, retaining their logs and separate output files:

```text
Unity -batchmode -nographics -quit -projectPath <disposable-project>
  -executeMethod WallstopStudios.DataVisualizer.Tests.Editor.PrefabFeasibilityTests.RunColdFromCommandLine
  -probeFolder Assets/Probe -probeOutput <sample.json> -probeRevision <source-sha> -logFile <sample.log>
```

Join the command lines for the shell you use. Count only processes with 17 records, one missing-script diagnostic and exit code zero. Delete only the owned disposable project after
retaining reports/logs and confirming its processes stopped. This run did so; the one-off project-preparation helper was removed from the final change.

Unity's 2021.3 documentation defines GlobalObjectId and the default serialized-property/UI Toolkit APIs used by the adapter. The prefab contents
[load](https://docs.unity3d.com/cn/2018.4/ScriptReference/PrefabUtility.LoadPrefabContents.html) and
[unload](https://docs.unity3d.com/cn/2021.2/ScriptReference/PrefabUtility.UnloadPrefabContents.html) APIs also predate 2021.3. Runtime execution was verified only on the host
above. Unity 2021.3, macOS/Linux, large catalogs, script-backed custom components and importer identity changes remain unverified. Future adoption belongs to #305 after #309/#315;
this is a go/no-go decision and editing proposal, not a support guarantee or a package-floor change.
