# Measured performance

This page reports a complete 100-, 1,000-, and 10,000-asset run on one recorded host. It gives measured window, search, and selection costs, with the fixture-creation cost and
other limitations stated separately. These samples do not establish a performance guarantee.

## How the numbers are produced

The driver lives at `Tests/Editor/PerfBaseline.cs`. It stages deterministic `PlayModeDataObject` fixture datasets and measures with monotonic `Stopwatch` timers inside the editor.
Transport latency is not added to each duration.

- **Fixture creation calls** sums `AssetDatabase.CreateAsset` durations. **Fixture import** retains the final `AssetDatabase.Refresh` measurement. **Fixture staging total**
  includes folder creation, fixture construction, creation calls, editor time between staging ticks, and final refresh. The longest creation call and staging tick expose
  indivisible Unity calls that exceed the 4 ms cooperative budget. Each tick creates at most ten assets; Unity calls themselves cannot be interrupted.
- **Window open to index ready** runs from `CreateGUI` until the production search cache reports populated. _Cold_ means the first open after staging in an already-running editor;
  it does not mean a fresh editor or an empty filesystem cache. _Warm_ numbers come from repeated open-close cycles of the same dataset, after five discarded warm-ups (the
  historical run below used two).
- **Search first results** changes the query in the production search field and waits for a result row, including any search deferral. The visible list is capped at 25 rows, so
  match counts saturate at that cap.
- **Selection** measures the production `SelectObject` call for a mid-list fixture asset.

The run writes a schema-1 JSON report with the environment, raw samples, and repetition counts. A configurable watchdog (90 minutes by default) is checked on editor ticks. Each
dataset owns a unique `Assets/PerfBaselineFixtures-<run-id>` folder; cleanup verifies its GUID and ownership marker before deleting it and preserves pre-existing assets, including
the old fixed fixture folder. `PerfBaseline.Status()` reports the current phase and fixture count. `PerfBaseline.Cancel()` writes an error report with the phase, created/requested
counts, elapsed staging time, and completed samples, then cleans up. Assembly reload, editor quit, and entering Play Mode also cancel the run. Start the driver in Edit Mode.
Incomplete single-sample scenarios contain empty arrays instead of zero-duration results.

## Recorded environment

The run started at 22:43:42 UTC and generated its report at 23:24:07 UTC on 2026-10-14. The measured Unity source matches
[main at ec085b1](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/ec085b1596775eac6be5071b23f5da9870097973), including the type-only asset relevance change from
[#228](https://github.com/Ambiguous-Interactive/DxVisualizer/pull/228). [Download the unchanged raw report](performance-baseline-2026-10-14.json).

| Property                                     | Value                                                |
| -------------------------------------------- | ---------------------------------------------------- |
| Unity / package                              | 6000.5.2f1 (Windows Editor) / 0.1.0                  |
| OS                                           | Windows 11 (10.0.26200)                              |
| CPU                                          | Snapdragon X Elite X1E80100, 12 cores                |
| Memory                                       | 65,011 MB                                            |
| GPU                                          | Qualcomm Adreno X1-85                                |
| Display                                      | one, 96 DPI, editor pixels-per-point 1.0, dark theme |
| ScriptableObject assets outside the fixtures | 214, verified after cleanup                          |

The raw report's `preExistingScriptableObjectAssets` field says 10,214 because it was captured before teardown and includes the final 10,000 fixtures. The raw file is preserved as
emitted. New reports exclude the benchmark fixture folder from this count; see [#243](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/243).

## Results

Every value is milliseconds, rounded to one decimal place. Warm, search, and selection rows show min / median / max over five samples; cold and refresh rows are single samples.

| Scenario                                    | 100 assets            | 1,000 assets             | 10,000 assets            |
| ------------------------------------------- | --------------------- | ------------------------ | ------------------------ |
| Fixture import (single sample)              | 31.7                  | 4971.2                   | 561.2                    |
| Window open to index ready, cold            | 1984.6                | 2220.4                   | 6951.4                   |
| Window open to index ready, warm            | 548.0 / 580.0 / 720.9 | 1197.6 / 1390.7 / 2418.8 | 6883.2 / 7771.4 / 8578.4 |
| Search first results (13 / 25 / 25 matches) | 60.6 / 71.3 / 320.2   | 139.7 / 192.2 / 650.9    | 336.7 / 368.2 / 611.6    |
| Selection                                   | 38.1 / 41.1 / 66.5    | 35.2 / 39.5 / 44.3       | 18.1 / 20.7 / 25.1       |

Reading the numbers:

- Warm open medians rise from 580 ms to 1,391 ms to 7,771 ms. At 10,000 assets, the first open takes 6,951 ms and the warm maximum reaches 8,578 ms. The object list fills gradually
  while the index loads; see [The object list fills in gradually](troubleshooting.md#the-object-list-fills-in-gradually).
- Search first-results medians are 71 ms, 192 ms, and 368 ms. The 1,000- and 10,000-asset medians exceed 100 ms, and every size has a maximum above 100 ms. Five samples do not
  establish a p95 or worst-case guarantee.
- Selection medians are 41 ms, 39 ms, and 21 ms. Search displays 13 rows at 100 assets and the capped 25 rows at both larger sizes; these timings do not measure rendering every
  match.
- The final 10,000-asset refresh takes 561 ms, but the complete run takes about 40 minutes. Main-thread status requests were unavailable for roughly 36 minutes between the
  1,000-asset warm phase and the 10,000-asset warm phase. The driver does not time fixture creation separately, so this report cannot identify that interval's exact cause or
  establish fast end-to-end creation/import.

The [earlier 100-/1,000-asset baseline](https://github.com/Ambiguous-Interactive/DxVisualizer/blob/51b37b8/docs/performance.md) remains in repository history. These are separate
sessions, not a controlled before/after comparison. In particular, the 561 ms final refresh does not prove that the import-stall acceptance criterion in
[#226](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/226) is satisfied.

## Current gaps

- The historical report predates staging instrumentation. New runs measure total staging and creation calls separately, but a controlled 10,000-asset import-burst comparison
  remains unmeasured.
- Play Mode entry overhead and compile timings are separate roadmap areas (T03, T08) and are not measured here yet.
- Cold scenarios are single samples; the full acceptance wants 30+ repetitions per warm scenario and cross-host spread (T01, T12).

Reproduce: run `WallstopStudios.DataVisualizer.Tests.Editor.PerfBaseline.RunFromCommandLine` in an interactive editor (directly or through the Unity MCP bridge), then read
`Temp/PerfBaseline/perf-baseline.json`. The output directory can be redirected with `-perfBaselineOutputDir` or the `DATAVISUALIZER_PERF_BASELINE_DIR` environment variable. For a
bounded host run, call `PerfBaseline.RunBaseline("Temp/PerfBaseline", new[] { 100 })`; sizes from 2 to 100,000 are accepted, but the default remains 100/1k/10k. New runs use five
discarded warm opens and 30 warm-open, search, and selection samples. This does not expand the historical report's sample count or establish large-fixture support.

## Staging instrumentation check (2026-10-19)

A complete 1,000-asset run at [cc4225f](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/cc4225f3a4135e0e529d010eb377c55256c43073) on the Windows 11 / Unity 6000.5.2f1
host above confirms that creation calls dominate the final refresh. [Download the unchanged raw report](performance-staging-2026-10-19.json). It records the complete environment
and 30 warm-open, search, and selection samples after five discarded warm opens.

| Staging metric                                     | Milliseconds |
| -------------------------------------------------- | -----------: |
| Creation calls, summed                             |    92317.406 |
| Total staging, including editor time between ticks |   144180.370 |
| Final refresh                                      |      282.375 |
| Longest creation call                              |      120.954 |
| Longest staging tick                               |      194.881 |

This is an instrumentation check, not a controlled speed comparison or large-fixture guarantee. A 4 ms cooperative budget cannot interrupt an individual Unity creation call. The
100/10k and ordinary-SO/many-type matrices, interaction readiness stages, import bursts, and matched Play/compile controls remain open in
[#306](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/306).

## Bounded fixture import batches

Fixture staging now groups creation calls within each editor tick, up to ten assets and a 4 ms creation budget. Every batch resumes imports before returning to the editor,
including when creation fails. Reports separate creation calls, batch import flushes, fixture identity initialization/verification, final Refresh, and total staging wall time. The
longest import flush and complete staging tick remain visible because a cooperative budget cannot interrupt Unity's import work. Fixture initialization saves only the owned assets
through `SaveAssetIfDirty`, and verifies that generated BaseDataObject identities persist after reload.

Benchmark windows clone the current configuration into an owned temporary state file. Closing or cancelling a run removes those files and restores the host's active selection and
window preferences. Project settings and the user's saved-state JSON remain outside the measurement window's write path.

## Incremental refresh integration check (2026-10-19)

A [single 10,000-real-asset probe](performance-refresh-10k-2026-10-19.json) at
[83d7771](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/83d7771674fc94aa99718fb5c74bf3c7a735fdcd) preserved 9,999 unrelated row identities and reloaded zero objects
for rename. Labels, Undo/Redo, move and deletion during an explicit search rebuild passed without restarting row loading. The longest affected-asset refresh slice was 19.649 ms.
The probe staged fixtures with an explicit 32 ms creation budget and ten-item cap: total staging was 290,550.591 ms, including 194,405.672 ms in batch import flushes; the longest
staging tick was 308.276 ms. These are one-sample integration observations, not p95, a controlled speed comparison, or evidence for real 100k/ordinary-SO/many-type support.

## Larger synthetic metadata sets

See [million-record feasibility](million-record-feasibility.md) for the opt-in synthetic experiment, real-asset resource estimates, and support limits.

## Readiness and fixture distributions

The current driver keeps `window-open-to-index-ready` measured from the existing post-show clock. It also records each opening in `readinessSamples`, including five discarded
warm-up openings, five cold-window openings and 30 measured warm openings. Search and selection also discard five warm-up samples before collecting 30 observations. **Cold-window**
means a recreated window in the same editor process; it does not mean a fresh editor, cold filesystem cache or a package-absent control. The report states that distinction
explicitly.

Each record separates the first root paint callback, first observed laid-out object rows, first observed laid-out inspector, and full index readiness from the start of window
creation. The paint callback does not prove GPU presentation. Row and inspector observations occur on editor updates; their times include observation delay. Negative timings mean
unavailable, and incomplete records remain marked incomplete on cancellation. The legacy index clock stops at its first ready observation even if rendering finishes later.

The controlled window uses an 1180×780 rectangle with 320/280-point catalog/object panes, then restores host preferences. Records include realized row count, loaded selected-type
rows, retained search-object count, separate peak managed/Unity-allocated/Unity-reserved bytes, indexing slice overruns, and observation count/cost. These memory quantities overlap
and must not be summed. The longest driver tick measures synchronous driver work. The longest observed editor-update gap also includes scheduling, background throttling and time
spent outside the driver; it is an upper-bound observation, not a package-attributed main-thread stall measurement. Instrumentation adds overhead; the per-record observer time
makes that cost visible.

`fixtureTypes` accepts distinct eligible ScriptableObject types. Fixtures cycle through the supplied array in deterministic order, use the existing deterministic names, and
preserve authored fixture IDs for `BaseDataObject` types. Ordinary ScriptableObjects need no package base class. Each size must contain at least one asset per supplied type. The
driver snapshots the type array before starting and records exact assembly-qualified identities in the report. Only the owned cloned window state adds fixture types and clears
inherited label filters/collapse state; the host state remains unchanged.

```csharp
WallstopStudios.DataVisualizer.Tests.Editor.PerfBaseline.RunBaseline(
    "Temp/PerfReadiness",
    new[] { 100, 1000 },
    timeoutMinutes: 15,
    fixtureTypes: new[] { typeof(MyGame.Items.WeaponData), typeof(MyGame.Settings.WorldSettings) });
```

`stagingBudgetMilliseconds` and `maximumStagingItems` optionally tune creation batches; defaults remain 4 ms and ten items. The report records both values. A larger creation budget
can amortize Unity import calls but does not bound the following indivisible import. Report longest ticks/imports alongside total staging, and preserve cancellation between
batches.

Use a single type for a large-type distribution and multiple types for a distributed catalog. Fixtures still use uniquely owned roots and the existing cooperative staging and
cleanup. These additions do not complete [#306](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/306): fresh-process, matched Play-entry/compile controls, operation
bursts, interactive filtering/scrolling/type-switch distributions, and the complete size/type matrix require separately recorded runs. Do not count a partial fixture as complete.

### Recorded mixed-type readiness run

[The unchanged raw report](performance-readiness-2026-10-21.json) was generated on 2026-10-21 at 06:35:51 UTC from
[7644f3a](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/7644f3a3becc4aa8d0990bc1de2f52df50e70a63). Unity 6000.5.2f1 ran on Windows 11, Snapdragon X Elite, 65,011 MB
RAM, Adreno X1-85, one 96-DPI display, 1.0 pixels-per-point and the dark editor skin. Each real-asset dataset alternated an ordinary SO (`OtherEditorOnlyCreationData`) with a
`BaseDataObject` (`PlayModeDataObject`); this is a two-type distribution, not hundreds of types. Creation used an explicit 32 ms budget and ten-item limit.

Values below are median / nearest-rank p95 milliseconds for 30 measured warm windows after five discarded openings. Each size also has five cold-window observations, five
search/selection warm-ups and 30 search/selection samples in the raw file. These cold windows share a warm editor process. Both datasets and all 80 readiness records completed.

| Observation                  | 100 assets    | 1,000 assets    |
| ---------------------------- | ------------- | --------------- |
| First root paint             | 260.7 / 332.9 | 277.2 / 337.9   |
| First laid-out rows          | 351.8 / 420.7 | 384.7 / 467.4   |
| First laid-out inspector     | 351.8 / 420.7 | 384.7 / 467.4   |
| Full index from creation     | 487.6 / 679.3 | 748.8 / 1,899.4 |
| Legacy post-show index clock | 438.9 / 633.1 | 704.8 / 1,857.9 |
| Observer work per opening    | 3.1 / 4.0     | 11.5 / 80.9     |

Realized rows stayed at 18 while the search retained 111 and 1,011 objects, including 11 background objects. This confirms viewport virtualization and growing object retention; it
does not satisfy #309's bounded-retention outcome. Total staging was 2,895.9 / 27,607.6 ms. Longest import batches were 120.5 / 151.6 ms; longest driver ticks were 220.3 / 228.9
ms; observed editor-update gaps reached 461.9 / 655.8 ms. Those gaps include scheduling and editor work outside the package. Instrumentation cost is material and is not subtracted
from the readiness times. These are observational results, not a controlled speedup claim or a 10k/100k support guarantee. The run restored seven editor windows and left no owned
fixture roots or temporary state files. Unity 2021.3 and other platforms were not executed.

The driver now charges a transition tick to the dataset that owned it at entry and resets update-gap observation between sizes. Final fixture teardown completes before report
publication on the next tick; report writing and host-preference restoration remain outside the sampled driver ticks. The archived report above predates this diagnostic repair; its
paint, row, inspector, indexing and retention observations remain valid, but corrected longest-driver-tick values require a repeat run.

## Bounded refresh attempt on 2026-10-21

[The cancellation report](performance-refresh-attempt-2026-10-21.json) records an incomplete real-asset integration run from
[63e4ff0](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/63e4ff04bd49b74e701a02c5037b40223a3230bb), on the same Windows/Unity host described above. It staged and
loaded all 10,000 `PlayModeDataObject` assets with the existing fixture helper, a 32 ms creation budget and a twenty-item cap. It reached its cooperative 15-minute limit in a
label-write loop. Unity calls cannot be preempted; the task was observed terminal by 07:43:20 UTC. Its status clock continues after completion, so that observation does not give an
exact terminal duration.

Before that cancellation site, the probe had passed selected missing-script recovery, tracked rename, label include/exclude, Undo/Redo and move assertions, with zero new refresh
object loads for the cached rename and 9,999 unrelated row identities preserved. This follows from the executed assertions preceding the recorded stop site; the final all-checks
summary was not emitted. Main-thread status requests also timed out after five seconds. Those observation gaps do not identify a continuous package-owned stall.

The 100/1k/10k label-burst matrix, callback timing, final operation counts and the large lifecycle tail **did not complete**. The probe did not record the active burst size, so do
not infer which sizes passed. A separate completed 12-asset smoke covered actual label writes, unavailable deletion, closed-window reopen, stale callbacks, custom order and
selection, and simulated Play notifications. It processed 13 events for 12 changed labels with zero new object loads. These are integration observations, not p95 measurements.

Cleanup was verified independently: no owned fixture roots remained, the editor had one native host and seven windows, and the AssetDatabase was idle. Two earlier 100k attempts
were stopped during staging at 7,175 and 10,536 assets to strengthen the probe and repair review findings; neither measured 100k. The report retains the original terminal error,
source identities, resource limits and unverified cells. Keep [#306](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/306) and
[#308](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/308) open for the remaining measurement work.
