# Controlled partial and complete real-asset search

This observation addresses measurement gaps in #306 and #310. It does not complete either issue's broader matrix. No production search behavior changes in these PRs.

## Method and identity

The existing `PerfBaseline` driver stages 10,000 uniquely owned ordinary ScriptableObject/BaseDataObject fixtures. The run uses a 20-minute watchdog, 64 ms staging budget and
100-item staging limit. Creation/import time stays separate from interaction timing. Five warm-ups precede 30 measured queries for each index-mode/condition pair.

The controlled partial window first reaches initial readiness, starts a fresh production cache rebuild, then pauses extraction after the requested 0.1 count threshold. Selected
rows seed that rebuild, so actual available records can exceed 10% of requested fixtures. The report records actual input counts and rejects cache count/version changes. This is
warm metadata with paused extraction, not cold-index or sustained-arrival latency. That window is discarded before the ordinary readiness observations.

The same production search field, query, previews and popover serve common names, a rare GUID, a rare Unicode field token and absent text. A target not yet available can have zero
partial matches. Complete-index common-name queries require a positive match; rare/absent expectations remain exact. Common names can reach the production result cap.

`completedMilliseconds` observes current UI-tree publication. `publicationPaintMilliseconds` observes a later visible popover `generateVisualContent` callback for the same query
text/generation. The driver requests repaint after observing publication, so this is a conservative later paint-generation clock with observer scheduling. It does not establish GPU
presentation, compositor completion, physical input latency or a controlled speedup.

Measured functional source: [bd533c1](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/bd533c13d012c2b052d31b6d98c80dabb45c5804). All 207 C# files match the Unity host.
The loaded-code guard verified the driver before the run: source SHA-256 `e209175eaaa7124a4aaf917c4336a185e9cd2ab6d1d07766b761f6c1288a5d35`, loaded module
`c0360228-36ff-4e5c-8bbe-a6a08c2878a4`, assembly SHA-256 `6d29017da45aa6756018ca2dc3643c39570a6aa9ed339f5732f5b2f0cc2f642f`. Preflight and postflight match; no C# source changed
during measurement.

## Results

The [emitted report](performance-partial-real-10k-2026-10-23.json) preserves its original bytes (SHA-256 `ffa0406787fd2583d4c1348a18c8dce4324050e91b0f887df3e503243340676d`). All
280 query, 40 ordinary readiness and 105 interaction observations completed before the 20-minute watchdog. Every scroll observation moved the viewport. Each table row has 30
measured queries after five discarded warm-ups. p95 uses nearest rank (`ceil(0.95 × N)`); medians average the two central samples.

| Index mode     | Condition   | Records | Publication median (ms) | Publication p95 (ms) | Paint p95 (ms) |
| -------------- | ----------- | ------- | ----------------------- | -------------------- | -------------- |
| complete       | common-name | 10,014  | 44.756                  | 66.354               | 83.148         |
| complete       | no-match    | 10,014  | 25.468                  | 36.985               | 40.970         |
| complete       | rare-field  | 10,014  | 47.842                  | 59.920               | 65.551         |
| complete       | rare-guid   | 10,014  | 32.283                  | 45.265               | 50.033         |
| paused-partial | common-name | 5,000   | 77.440                  | 96.980               | 102.745        |
| paused-partial | no-match    | 5,000   | 78.462                  | 90.225               | 94.508         |
| paused-partial | rare-field  | 5,000   | 88.768                  | 94.720               | 99.608         |
| paused-partial | rare-guid   | 5,000   | 89.743                  | 125.753              | 133.393        |

The partial view contains 5,000 selected-type records; the complete view has 10,014 records including pre-existing tracked assets. Partial Player/World inputs produce either 25
hits at the cap or zero available hits. The rare GUID is absent from that partial view and returns zero; the rare Unicode field returns one in both modes. These are different
available datasets and result populations, not an equal-work controlled speedup comparison.

The 100 ms goal is not met across both modes: partial rare-GUID publication p95 is 125.753 ms, with paint p95 133.393 ms. Partial common-name paint p95 is 102.745 ms. Complete
publication/paint p95 stays below 100 ms in these observations. Warm usable-row/inspector p95 is 980.242 ms and full-index readiness p95 is 11130.802 ms; opening still misses 100
ms. These clocks and completeness states stay separate.

Observed maximum query/index slices are 4.1866/5.3991 ms. There are 1,133 query overruns including warm-ups. The run observes zero retained global-search objects, 5,000 loaded
selected-type rows and at most 18 realized rows; this does not establish bounded selected-object retention. Fixture staging takes 413993.298 ms, with maximum staging tick 1032.711
ms. Whole-run driver/update-gap maxima include setup and observer work and are not isolated package stalls.

Host: Unity 6000.5.2f1, Windows 11, Snapdragon X Elite, 12 logical processors, 65,011 MB reported RAM, Adreno X1-85 GPU, one display, 96 DPI, one pixel per point, dark theme,
package 0.1.1. This is one warm editor process. The [provenance record](partial-real-search-2026-10-23-provenance.json) preserves successful preflight/postflight identities and the
matching compiler SHA-256. Cleanup independently confirms zero owned fixture roots, temporary state files and browser windows, 228 pre-existing ScriptableObject assets, zero shared
metadata/pending paths, an unsuspended index and an idle driver/AssetDatabase.

## Limits and remaining work

The paused partial view excludes concurrent extraction and sustained edits. These are scripted warm-process observations, not a fresh-process or platform matrix. The driver records
setup/stalls and memory separately; those process-wide values do not isolate package-native memory. Selected-type object retention remains unbounded through #309.

Keep #306 and #310 open for their remaining fixture/distribution/change-operation, live partial-index, platform and end-to-end acceptance. No million-asset support or hard frame
budget is established here.
