# Measured performance

This page reports a complete 100-, 1,000-, and 10,000-asset run on one recorded host. It gives measured window, search, and selection costs, with the fixture-creation cost and
other limitations stated separately. These samples do not establish a performance guarantee.

## How the numbers are produced

The driver lives at `Tests/Editor/PerfBaseline.cs`. It stages deterministic `PlayModeDataObject` fixture datasets and measures with monotonic `Stopwatch` timers inside the editor.
Transport latency is not added to each duration.

- **Fixture import** is the wall time of the single `AssetDatabase.Refresh` after fixture creation. The timer excludes the preceding `AssetDatabase.CreateAsset` loop, so this
  number does not measure the total cost of creating or importing the dataset.
- **Window open to index ready** runs from `CreateGUI` until the production search cache reports populated. _Cold_ means the first open after staging in an already-running editor;
  it does not mean a fresh editor or an empty filesystem cache. _Warm_ numbers come from repeated open-close cycles of the same dataset, after two discarded warm-ups.
- **Search first results** changes the query in the production search field and waits for a result row, including any search deferral. The visible list is capped at 25 rows, so
  match counts saturate at that cap.
- **Selection** measures the production `SelectObject` call for a mid-list fixture asset.

The run writes a schema-1 JSON report with the environment, raw samples, and repetition counts. A 45-minute watchdog is checked on editor ticks and fails the run closed. It cannot
interrupt synchronous fixture staging; teardown runs when control returns to the driver and removes its fixtures and windows.

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

- Total fixture creation/import time and a controlled 10,000-asset import-burst measurement remain unmeasured. The final refresh alone excludes preceding work.
- Play Mode entry overhead and compile timings are separate roadmap areas (T03, T08) and are not measured here yet.
- Cold scenarios are single samples; the full acceptance wants 30+ repetitions per warm scenario and cross-host spread (T01, T12).

Reproduce: run `WallstopStudios.DataVisualizer.Tests.Editor.PerfBaseline.RunFromCommandLine` in an interactive editor (directly or through the Unity MCP bridge), then read
`Temp/PerfBaseline/perf-baseline.json`. The output directory can be redirected with `-perfBaselineOutputDir` or the `DATAVISUALIZER_PERF_BASELINE_DIR` environment variable.
