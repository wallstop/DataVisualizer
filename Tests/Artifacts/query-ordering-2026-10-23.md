# Shared query ordering and live rebuild observations

This evidence advances #310, #306 and #323. Their broader acceptance matrices remain open. It does not establish real million-asset support.

## Source and method

Production change [b4016d1](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/b4016d1877b0e124c3661ad95c9dd6fdfd897daf) shares cooperative ordering per immutable cache
version while retaining independent query progress. Driver [261a494](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/261a494838e310267e4c7a71229221b093dbd687) adds
live warm-rebuild samples and keeps publication/paint clocks within one generation. Benchmark
[d7d59e1](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/d7d59e127ed6c775c3cf5c3cea208596a9505163) measures query batches after advancement. Each measured checkout
matches all 205 checked Editor/Runtime/Tests C# files on the Unity host. Source stays frozen within each run. The scale and paused runs precede the per-input live refinement; their
actual source identities remain separate from the later live run.

The first scale attempt was cancelled after a source comparison found an older benchmark file copied during a commit hook's temporary stash. Its partial output is excluded. The
retained replacement verifies the compiler checksum, loaded module and assembly bytes before, during and after measurement; source SHA-256 is
`9e62a1f92f25a9d2c13da2a359f55955599c00004bcfdbc78fb2cf3c0cdef755`. The real driver uses preflight/postflight checks of its compiler checksum and loaded assembly.

The later [baf9b9e refinement](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/baf9b9e18ed836cad73d1aaa8f11839424fa823c) starts every live input during a new warm
rebuild. Rebuild setup and previous extraction settling stay outside the query clock; complete publications require positive common-name matches.

## Synthetic metadata

The [emitted scale report](performance-query-ordering-scale-2026-10-23.json) completes all 18 partial/full conditions at 10k/100k/1m in 272.743 seconds, with five warm-ups and 30
samples per condition. It creates no Unity objects or assets. The unchanged limits are a 30-minute runner timeout and 2 GiB construction-heap increase guard. Dataset text includes
Unicode, nested fields, labels, paths and titles. AssetDatabase classification, disk cache and real-asset import costs are excluded.

Both 32-query batches stay rooted. Copied controls and shared queries are first measured without advancement, then every control advances once with zero budget, then every shared
query advances once. Each later heap delta subtracts the preceding forced-GC process-heap observation. These are net retained deltas, not allocation counts, isolated/native memory,
full-sort completion or a controlled end-to-end speedup. Zero/negative deltas do not mean zero allocation. The partial view is unsorted; the complete view is already sorted.

| Dataset records | Available records | Added copied heap after advance (MiB) | Added shared heap after advance (MiB) | Warm absent active p95 (ms) |
| --------------- | ----------------- | ------------------------------------- | ------------------------------------- | --------------------------- |
| 10,000          | 1,000             | 0.246                                 | 0.008                                 | 1.504                       |
| 10,000          | 10,000            | 0.004                                 | 0.000                                 | 16.017                      |
| 100,000         | 10,000            | 2.512                                 | 0.078                                 | 13.293                      |
| 100,000         | 100,000           | 0.000                                 | 0.000                                 | 150.099                     |
| 1,000,000       | 100,000           | 24.504                                | 0.770                                 | 141.061                     |
| 1,000,000       | 1,000,000         | 0.000                                 | 0.000                                 | 1692.603                    |

At 1m, the store/full view/additional 10% partial view/tracker retain 1,460,731,904 process-heap bytes before query batches. The 100k-to-1m slope is about 1,486.6 bytes per
additional dataset record; it includes the added partial view and tracker, not native memory. A rare/absent full query still scans 1m records; this does not make million-record
search interactive. The largest observed construction unit is 129.356 ms and the largest timed setup forced collection is 966.080 ms. Heap collections inside the concurrent-query
probe are not timed separately. Whole-editor stalls and physical input-to-cancellation are not measured. The raw `diskCacheBytes=-1` means no persistent cache exists.

## Real-asset observations

Both real runs stage 10,000 uniquely owned mixed ordinary ScriptableObject/BaseDataObject fixtures with a 64 ms/100-item staging limit. The paused run uses a 20-minute watchdog;
the later live run has a declared 30-minute watchdog because every input initiates another warm rebuild. Initial rebuild setup and previous-index settling occur outside query
clocks. These are warm metadata reuse and sequential process observations, not cold extraction, sustained import traffic or a controlled speedup. Actual partial input is 5,000
selected-type records rather than exactly the requested 10% threshold; complete input includes 10,014 records with pre-existing tracked metadata.

The [paused report](performance-query-ordering-paused-10k-2026-10-23.json) completes all 280 queries, 40 ordinary readiness samples and 105 interactions. Every condition has five
warm-ups and 30 measured queries. p95 uses nearest rank (`ceil(0.95 × N)`). All eight publication and later visible-popover paint-generation p95 values stay below 100 ms. Paint
generation does not prove GPU presentation or physical input latency. Rare GUID/field expectations remain exact; absent partial targets can return zero.

| Mode           | Condition   | Records at input | Publication p95 (ms) | Paint-generation p95 (ms) |
| -------------- | ----------- | ---------------- | -------------------- | ------------------------- |
| complete       | common-name | 10,014           | 56.783               | 74.355                    |
| complete       | no-match    | 10,014           | 35.648               | 39.955                    |
| complete       | rare-field  | 10,014           | 60.131               | 67.162                    |
| complete       | rare-guid   | 10,014           | 51.722               | 57.406                    |
| paused-partial | common-name | 5,000            | 61.680               | 65.860                    |
| paused-partial | no-match    | 5,000            | 24.942               | 28.074                    |
| paused-partial | rare-field  | 5,000            | 32.605               | 37.502                    |
| paused-partial | rare-guid   | 5,000            | 32.042               | 38.689                    |

Paused-run warm usable-row/inspector p95 is 947.372 ms, while full-index readiness p95 is 21,530.564 ms. Opening still misses 100 ms. The run retains zero objects in the global
cache's strong list, 5,000 selected-type loaded objects and at most 18 realized rows. This does not establish bounded selected-object retention. Maximum observed index/query slices
are 75.593/4.075 ms; 4 ms remains a cooperative target. Cleanup returns the seven editor windows, one native host and 228 pre-existing assets, with no fixture roots, browser
windows or pending index work.

The [live report](performance-query-ordering-live-10k-2026-10-23.json) also completes 280 queries, 40 ordinary readiness samples and 105 interactions. All 140 live-window inputs
start incomplete, including all 120 measured inputs after warm-up. Each starts a fresh rebuild, with 5,000 records at input; current publication can contain more records and can
observe a completed index. The per-input driver source/compiler hash is `3ddc133a1cf1f692e53df3a227ea0620d3b1865ea42bb97f095191dba90ed6fa` at
[baf9b9e](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/baf9b9e18ed836cad73d1aaa8f11839424fa823c). These are independent warm rebuilds, not one sustained import
stream.

| Mode         | Condition   | Inputs partial | N   | Publication p95 (ms) | Paint-generation p95 (ms) | Complete at publication |
| ------------ | ----------- | -------------- | --- | -------------------- | ------------------------- | ----------------------- |
| complete     | common-name | no             | 30  | 91.680               | 119.695                   | 30/30                   |
| complete     | no-match    | no             | 30  | 43.897               | 49.346                    | 30/30                   |
| complete     | rare-field  | no             | 30  | 56.314               | 62.328                    | 30/30                   |
| complete     | rare-guid   | no             | 30  | 58.286               | 64.332                    | 30/30                   |
| live-rebuild | common-name | yes            | 30  | 4387.301             | 4406.969                  | 30/30                   |
| live-rebuild | no-match    | yes            | 30  | 2516.083             | 2522.169                  | 30/30                   |
| live-rebuild | rare-field  | yes            | 30  | 2770.094             | 2776.896                  | 30/30                   |
| live-rebuild | rare-guid   | yes            | 30  | 2564.785             | 2569.218                  | 30/30                   |

The live target is not met by these observation clocks: publication p95 spans 2,516.083–4,387.301 ms. The complete control's common-name paint p95 also misses 100 ms at 119.695 ms.
The 120 measured live inputs have 3,027 superseded publication observations. The clocks require a current generation at driver observation and a later matching paint-generation
callback; newer generations can reset both clocks. They include observer scheduling and stabilization, not the first ever preview, physical input-to-cancellation, GPU presentation
or a whole-editor stall bound. `firstResultMilliseconds` remains a separate earlier-preview observation where matches exist. These values must not be presented as user-perceived
first-result latency or a controlled speedup.

The [provenance record](query-ordering-2026-10-23-provenance.json) preserves matching preflight/postflight compiler/source/module/assembly identities for each real run and
successful independent cleanup. Raw SHA-256 values: scale `92692cc79d443a195d1cfe6ec79351eb6d8b3eb062aafd5fa439ba1683315484`, paused
`beb7784376fda59a40c2610651a239270fd3e55a539a9cbe2330f244c6a193f3`, live `de3ce8b247f313515d79d9c92ed73acc5fe628331dfdf21bcccddefe362d74e5`.

## Unsupported-condition repair

A later review found an omitted phase transition when ordinary-only fixtures skip rare-field queries during live rebuilds. The
[pre-fix probe](query-ordering-skip-red-2026-10-23.json) intentionally fails in `WaitingPartialIndex` after 70 queries, with all 512 fixtures staged; its owned resources clean up.
It is failure evidence, not a successful performance sample. The repair returns to the sample runner and adds a hosted ordinary-only regression requiring 210 complete queries and
40 ordinary readiness samples. The mixed real datasets above use supported rare fields and do not execute that skip branch. Their source identities remain those measured before
this repair; no final-head timing claim is made.

## Limits and remaining acceptance

The host is Unity 6000.5.2f1 on Windows 11, Snapdragon X Elite, 65,011 MB reported RAM, Adreno X1-85 GPU, one 96-DPI display, dark theme, package 0.1.1. These are sequential runs
in one warm process, not fresh-process or platform controls. Source identities, exact raw bytes and failed/cancelled status must remain attached to their own runs.

Keep #306 open for its full fixture/distribution/change-operation, Play/compile and fresh-process matrices. Keep #310 open for unmet latency targets, cancellation and extraction
responsiveness evidence. Keep #323 open for representative real-project/native/cache costs, global stalls, input-to-cancellation and broader invalidation workloads. Selected-type
object retention remains #309. No release or support expansion follows from these observations.
