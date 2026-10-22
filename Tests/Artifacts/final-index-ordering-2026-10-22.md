# Final index ordering on 22 October 2026

Code and drivers: [46786c9](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/46786c991501efbfd4d0f20348d43928e9518d85). Both observations use Unity 6000.5.2f1 on
Windows 11, Snapdragon X Elite, Adreno X1-85, 96 DPI, one display and the dark theme. They share one warm editor process after the normal test suites. They are not controlled
comparisons with earlier reports.

## Completed synthetic metadata experiment

The [emitted report](performance-final-index-metadata-2026-10-22.json) preserves all 18 partial/full query scenarios at 10k/100k/1m, five warm-ups and 30 measured queries per
condition. It creates no Unity objects or assets. The explicit test passed in 308.81 seconds, within the declared 30-minute timeout and 2 GiB construction heap guard. All
scan/match assertions passed.

Raw SHA-256: `db4b32367e78595cdf025071f3fecb8b23270513e62db3639358df4247a57e48`. Driver SHA-256: `e97fd117b500ed4b0f9163bc1e506fc6efd2db031f6e9d50097e34ec97946a19`.

Final ordering now yields during merging and GUID lookup construction, then publishes both together. Available query snapshots remain independent. The recorded final-sort calls
include snapshot and lookup allocation. Overruns count calls that exceed the 4 ms budget; the budget is not a frame-time guarantee.

| Records   | Store MiB | Store + view MiB | With tracking MiB | Sort active / wall ms | Longest sort call ms | Calls / overruns |
| --------- | --------- | ---------------- | ----------------- | --------------------- | -------------------- | ---------------- |
| 10,000    | 6.89      | 7.25             | 8.19              | 51.24 / 57.39         | 4.06                 | 14 / 12          |
| 100,000   | 130.88    | 134.56           | 142.91            | 683.14 / 770.60       | 4.16                 | 171 / 169        |
| 1,000,000 | 1,224.27  | 1,262.97         | 1,404.23          | 7,758.80 / 8,590.51   | 60.64                | 1,918 / 1,916    |

These are per-operation managed heap deltas after forced collections, not isolated process or native-memory slopes. Peaks must not be summed. The current Unicode candidate cache
adds copied text; earlier source revisions do not establish its current cost. Temporary sort memory is not separately profiled.

At 1m, full no-match active/wall p95 is 1,518.67/1,751.26 ms; rare-match p95 is 1,503.07/1,686.89 ms. A 100k-record partial no-match query has active/wall p95 of 758.36/850.43 ms.
Full common-query snapshot preparation alone reaches p95 61.75 ms. The longest construction unit is 134.97 ms and forced collection 1,104.46 ms. Allocation, comparisons and GC are
indivisible and not separately attributed within the 60.64 ms final-sort maximum. These counters do not observe every editor stall.

No assets are loaded or strongly retained by the synthetic view. Abandonment timings observe cooperative query boundaries, not user input-to-cancellation. There is no disk cache
(`diskCacheBytes=-1`). These component results do not establish million-asset support or justify a storage-engine choice.

## Incomplete real 10k attempt

The [unaltered failure report](performance-final-index-real-10k-attempt-2026-10-22.json) reached the declared 20-minute watchdog in `WaitingWarmReady`. All 10,000 uniquely owned
fixtures were staged, half ordinary ScriptableObjects and half BaseDataObjects. Staging used a 64 ms/100-item limit; query work kept its 4 ms budget. The full matrix was not
reduced: only 15 of 40 readiness samples completed, with one further incomplete sample retained. This is not a completed real-asset run or a warm-open percentile result.

Raw SHA-256: `3f316d8024ce2cb84f17f0b66cb037a4a855b3a17a359f86b983cfdf18b50fc1`. The unchanged `PerfBaseline.cs` driver SHA-256 is
`642fbafdd8baa81527b4c89da75414c20335772eafaebfd3062455f7ab9891c4`.

Within that incomplete run, all 140 query and 105 interaction observations completed. Each query condition contains five warm-ups and 30 measured samples on 10,014 available
records. Rare/no-match conditions scanned the full snapshot and returned the expected counts. Published preview counts matched metadata hits; all measured scrolls moved.

| Completed query subset | Publication median ms | Publication p95 ms |
| ---------------------- | --------------------- | ------------------ |
| Common name            | 50.91                 | 238.22             |
| Rare GUID              | 45.96                 | 62.04              |
| Rare Unicode field     | 48.11                 | 63.44              |
| No match               | 33.16                 | 130.64             |

P95 uses nearest rank over the 30 completed observations. Timings include scripted field input and editor scheduling through UI-tree publication. They do not verify paint,
partial-index latency or fresh-process controls. Common-name and no-match p95 miss the 100 ms target. The changed source, fixture GUIDs and process history prevent attributing
differences from earlier runs to this patch.

Completed readiness observations retain zero strong global-search references, 5,000 loaded selected-type objects and at most 18 realized rows. The maximum recorded index slice is
72.51 ms. These are separate process observations, not bounded selected-object retention or native-memory slopes. Warm opens reset the shared index; they do not measure
across-close shared-cache hits.

## Validation and remaining work

The code tree passed all 813 EditMode and 15 PlayMode cases. New ordering regressions cover both preparation phases, mutations, reset, explicit cancellation, synchronous
compatibility, aligned GUID lookup publication and independent query snapshots. The full local gate and exact 259-file npm payload passed.

Independent cleanup confirms an idle driver/AssetDatabase, stopped Play, one native host and seven editor windows, no owned fixture roots or state files, zero shared
metadata/pending paths and no suspension.

Keep #309, #310 and #323 open for bounded selected rows, persistent-cache benefit/recovery, allocation/extraction pauses, rendered partial/full latency, native memory and platform
evidence. #306 owns the unfinished real readiness matrix. No fresh-process controls, real million-asset staging or release was performed.
