# Million-record feasibility

This is a synthetic metadata experiment for [#323](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/323), not a claim that DxVisualizer supports one million Unity
assets. At the measured source below, the browser retained loaded assets and the shared metadata index proposed in
[#309](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/309) is unfinished. A later shared-store observation is recorded at the end of this report.

## What was measured

The opt-in `MetadataScaleBenchmarkTests.ShouldMeasureSyntheticMetadataScale` calls production `AssetSearchDocument`, `AssetSearchQuery`, and `AssetChangeRefreshService` code. It
creates 10k, 100k and 1m ASCII-only metadata documents with unique 32-digit GUIDs and names. No ScriptableObjects, `.asset` files, imports or inspectors are created. Identity
tracking uses synthetic paths and an injected resolver, so its timings exclude AssetDatabase work. It is a per-window tracker, not the future shared metadata index.

Each size runs three queries over 10% and 100% snapshots: no match, one match at the final record, and a common match capped at 25 results. Every query discards five warmups and
records 30 samples, checking both scanned and returned counts. Partial snapshots exclude the final rare match. Metadata construction and scanning yield at a 4 ms target; individual
operations and garbage collection can exceed it. Total query time includes snapshot copying and inter-slice waits imposed by Unity Test Framework. Separate active execution samples
sum preparation and each `Advance` call; it excludes window debounce, rendering and object-field extraction. These measurements cannot be compared directly with the hosted-window
10k end-to-end search target.

Active execution means Stopwatch wall time inside snapshot construction and `Advance`, including GC or scheduling interruptions; it is not a hardware CPU counter.

The report records snapshot preparation, longest query slices, retained managed heap deltas after full collection, construction wall time, a single tracked change, and forced
collection cost. Heap deltas are process-wide observations, not allocation counts or isolated peak working sets. A separate iterator releases each dataset before the next size.
There is no persistent metadata cache to measure; `diskCacheBytes: -1` means unavailable, not zero cost.

Cancellation is cooperative: the owner stops advancing an abandoned query after one slice. The regression checks that its scan count stays fixed on the next editor tick. The
reported preparation-plus-first-slice interval is one measured indivisible unit before the owner can stop; it is not input-to-cancellation latency. Snapshot copying, each
individual match and automatic garbage collection can delay that boundary. No internal cancellation token interrupts those operations.

## Results

The complete 25-minute run passed all 18 scenarios on Unity 6000.5.2f1 Windows on 20 October 2026. [Download raw samples and environment](performance-metadata-1m-2026-10-20.json).
The measured source is [6924001](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/69240017d898b6a66957e52ade3b8d0906fa8af3); the report records its SHA-256.

| Records   | Documents (MiB) | With tracker (MiB) | Construction wall (s) | Longest construction unit (ms) | Longest forced GC (ms) |
| --------- | --------------- | ------------------ | --------------------- | ------------------------------ | ---------------------- |
| 10,000    | 2.08            | 3.01               | 2.13                  | 4.52                           | 325.65                 |
| 100,000   | 44.90           | 54.22              | 17.51                 | 26.84                          | 313.84                 |
| 1,000,000 | 485.88          | 637.06             | 183.93                | 62.59                          | 488.12                 |

The process-wide heap delta is noisy at small sizes. The 100k-to-1m increase with tracking was about 679 bytes per additional record in this run; this is not an allocation contract
or a native-memory estimate. Forced collection is measurement overhead, separately reported because it stalls the editor.

| Records   | Query         | Active median / p95 (ms) | Runner wall median / p95 (ms) | Snapshot-copy p95 (ms) |
| --------- | ------------- | ------------------------ | ----------------------------- | ---------------------- |
| 10,000    | absent-marker | 12.17 / 13.56            | 107.65 / 110.45               | 3.08                   |
| 10,000    | needle        | 11.17 / 13.87            | 106.87 / 110.19               | 3.07                   |
| 10,000    | record        | 2.71 / 3.51              | 2.72 / 3.52                   | 3.09                   |
| 100,000   | absent-marker | 127.53 / 136.04          | 1297.04 / 1404.72             | 29.01                  |
| 100,000   | needle        | 130.11 / 146.24          | 1340.17 / 1471.10             | 34.23                  |
| 100,000   | record        | 26.64 / 28.72            | 26.65 / 28.73                 | 28.35                  |
| 1,000,000 | absent-marker | 1416.98 / 1658.58        | 14933.06 / 17600.73           | 368.84                 |
| 1,000,000 | needle        | 1291.88 / 1617.96        | 13699.15 / 17952.13           | 301.29                 |
| 1,000,000 | record        | 265.16 / 302.24          | 265.17 / 302.24               | 301.81                 |

p95 uses nearest rank: sample 29 of the 30 sorted values. All recorded queries verified exact match and scan counts. Active timing includes synchronous snapshot copying; runner
wall time adds coroutine scheduling. Do not read either column as production-window end-to-end latency.

The 1m full snapshot requires copying one million references even when only 25 matches are requested. Its preparation is synchronous and exceeds the 4 ms slice target. The raw
report includes the measured preparation-plus-first-slice cancellation boundary; it excludes input dispatch and does not prove interactive cancellation latency.

Recommendation: do not claim one-million-asset support. These minimal metadata records already retain about 637 MiB with per-window tracking, and rare/no-match search needs over a
second of active execution. Prioritize #309's shared metadata ownership and bounded snapshot preparation before a real-asset scale claim. Partitioning or an inverted text index may
help rare queries, but labels, Unicode, field extraction and invalidation need representative measurement first. A disk-backed engine is not justified by these synthetic
measurements alone.

This completes the synthetic corpus experiment, not #323's full acceptance. The actual shared metadata index, representative real-asset/native-memory costs, complete editor stall
observation and input-to-cancellation latency remain unverified. #323 stays open for that evidence.

## Real-asset resource budget

A separate disposable-project experiment needs its own resource budget before generating assets. Synthetic records do not account for YAML, `.meta` companions, filesystem metadata,
Library artifacts, Unity's native object graph, custom importers, callbacks or inspector allocations.

The [recorded 10k real-asset probe](performance-refresh-10k-2026-10-19.json) spent 290.551 seconds staging, including 34.449 seconds in creation calls, 194.406 seconds in batch
imports and 0.623 seconds in the final refresh. Multiplying those observations by 100 gives **8.07 hours staging**, **57.42 minutes creation**, **5.40 hours imports**, and **62.29
seconds final refresh** at 1m. These are arithmetic planning estimates, not forecasts: Unity import and filesystem costs need not remain linear. The creation budget was 32 ms with
at most ten records per tick; the measured longest staging tick was 308.276 ms. The source report retains the complete scenario and host limits.

For disk planning, assume 0.5–4 KiB of YAML and 0.2–0.4 KiB of metadata per simple asset: 1m assets would use roughly **0.67–4.20 GiB** before directory/filesystem overhead. These
assumed sizes require calibration with the intended real fixture, especially for long strings, arrays, labels and custom serialized fields. Library and import-cache size are
unmeasured and may exceed source size; no synthetic cache number substitutes for them. Reserve at least 20 GiB free for a pilot and revise that budget from measured source/Library
growth before scaling. Use the actual available disk volume, not system RAM, to check this threshold.

Start with owned 1k/10k/100k fixtures in a fresh disposable project and compare ordinary ScriptableObjects, BaseDataObjects and many-type distributions. Store a unique ownership
marker outside Assets, refuse cleanup unless it matches, and retain logs plus partial counts on failure. Do not use the everyday project. Stop a pilot after 30 minutes, a 2 GiB
managed-heap increase, 8 GiB working-set growth, free disk below 20 GiB, or any single uninterruptible import over five seconds. Check resource thresholds between safe units and
record overshoot; a blocked Unity call cannot be stopped cooperatively. Stop before the next tier if extrapolation exceeds the approved run budget. These are proposed experiment
limits, not implemented guards in the synthetic driver.

The million-asset experiment was not started: the existing staging extrapolation already exceeds this session's two-hour aim. No partial real fixture is reported as complete.

## Reproduce and next decision

Run only the explicit fixture through the Unity host bridge:

```text
run_tests(mode="editor", filter="MetadataScaleBenchmarkTests", include_explicit=true, async_tests=true, timeout=1800)
```

The report is written to the package's ignored `.artifacts/metadata-scale.json`. The opt-in run stops at thirty minutes or a 2 GiB heap increase during construction. A cleanup
scope writes `complete: false` on ordinary failure or disposal; process termination cannot promise a final report. The test runner adds roughly 100 ms per yielded slice on this
host, so wall samples measure runner scheduling as well as synchronous work. Run it in a disposable editor session when heap collection or resource pressure could disrupt
interactive work. Normal EditMode runs skip the fixture.

Repeat against #309's actual shared index after it exists. Measure representative Unicode/field text, repeated invalidation, UI debounce/rendering, native retention and working
set, then stage the real-asset pilots under the budget above. Partitioning or disk-backed queries need a measured workload and invalidation/caching design; this experiment does not
authorize adding a storage engine or promising one-million-asset support.

## Shared-store follow-up on 22 October 2026

[Raw samples](performance-shared-metadata-2026-10-22.json) record the proposed shared metadata store and window document view at
[4bbf255](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/4bbf255456643460d2a5a3cf82ee7cda299decc7). The emitted report is preserved unchanged; its driver SHA-256 is
`ebbffcb27eae6d16cc935b6d561516007b800c49a512bf2c709e1d758bd82d91`. Subsequent lifecycle and navigation repairs in PRs #395 and #396 were validated separately. These timings
describe the measured revision, not the later repaired tree.

The opt-in driver completed all 18 partial/full scenarios in 888.85 seconds on Unity 6000.5.2f1 Windows. Each query discards five warm-ups and records 30 samples. Each synthetic
record has a unique GUID/path/name/title, two copied labels, and two shared field-text values including nested text, with Unicode in names and one field. No Unity objects, asset
files, native imports or AssetDatabase classification are created or measured. The store is owned by an isolated production `AssetGuidTypeIndex`; queries use its copied documents
through `AssetObjectCache`.

| Records   | Store (MiB) | Store + view (MiB) | With tracker (MiB) | Final sort (ms) |
| --------- | ----------- | ------------------ | ------------------ | --------------- |
| 10,000    | 3.84        | 4.45               | 5.39               | 42.14           |
| 100,000   | 95.07       | 100.25             | 109.27             | 504.24          |
| 1,000,000 | 974.59      | 1019.86            | 1156.45            | 6233.71         |

Heap deltas follow forced collections in one warm editor process and include harness references. They are not isolated allocation counts or native memory. Construction, sorting,
invalidation and heap observations are single samples per size. Zero retained object references and zero object-load calls refer to the synthetic metadata view. The selected-type
browser still retains loaded rows; no bounded real-asset/native-memory claim follows.

| Full records | Query         | Active median / p95 (ms) | Runner wall median / p95 (ms) |
| ------------ | ------------- | ------------------------ | ----------------------------- |
| 10,000       | absent-marker | 70.97 / 74.49            | 78.30 / 82.55                 |
| 10,000       | needle        | 96.43 / 99.17            | 106.28 / 113.32               |
| 10,000       | record        | 0.20 / 0.23              | 0.20 / 0.23                   |
| 100,000      | absent-marker | 667.05 / 726.79          | 726.32 / 793.24               |
| 100,000      | needle        | 923.85 / 992.69          | 1003.41 / 1066.79             |
| 100,000      | record        | 0.24 / 0.45              | 0.24 / 0.45                   |
| 1,000,000    | absent-marker | 6839.30 / 7311.97        | 7446.14 / 7965.52             |
| 1,000,000    | needle        | 9268.66 / 9778.27        | 10117.44 / 10591.04           |
| 1,000,000    | record        | 2.45 / 3.24              | 2.45 / 3.24                   |

p95 is nearest rank (sample 29 of 30). Raw data also separates the 10% partial snapshots. Partial snapshots use cooperative ordering; complete views use their separately measured
final sort. Common queries stop after 25 results; rare/no-match queries scan every available record. Timings exclude production window input/debounce, rendering and real object
extraction. The final synchronous sort can exceed the 4 ms target; this run accounts for that cost and does not make it cooperative.

Each size also removes and restores 1,000 identical records and applies one synthetic tracked change. These are instrumented component observations, not concurrent real imports or
a full invalidation workload. The cancellation boundary measures preparation plus one query slice, then verifies the abandoned scan stays stopped. It does not measure
input-to-cancellation latency or every editor stall. Forced collection and longest observed construction/query units are reported separately. There is no persistent cache;
`diskCacheBytes: -1` means unavailable.

The explicit run has a 30-minute runner limit and a 2 GiB managed-heap increase guard during construction. It reached every declared size. No real million-asset experiment was
started. The earlier disposable-project resource/time stop criteria remain required before real staging.

Keep the recommendation unchanged: do not claim million-asset support. Representative metadata already costs about one GiB here, and rare scans and final sorting stall for seconds.
Bounded selected-row retention, global stall/input cancellation observations, native/cache measurements and real-asset matrices remain open under #309, #310 and #323. Measure
representative projects before choosing partitioning, an inverted index or disk-backed storage; this synthetic result alone does not justify a storage engine.
