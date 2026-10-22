# Real 10k search metadata observation

[Raw report](performance-shared-search-10k-2026-10-22.json) records the final functional tree at
[c35661b](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/c35661bb61c27b699e12370db77b084b098f997b), using Unity 6000.5.2f1 Windows. The file is retained unchanged.
The same C# tree passed 769 EditMode and 15 PlayMode tests, the full local gate and an exact 257-file npm payload check. This is one warm editor session, not a controlled
comparison or cross-platform guarantee.

## Setup and completeness

The existing `PerfBaseline.RunBaseline` driver created 10,000 real main assets, split evenly between ordinary `OtherEditorOnlyCreationData` ScriptableObjects and
`PlayModeDataObject` BaseDataObjects. Staging used a 32 ms target and at most 20 assets per call, with a 20-minute run limit. All fixtures completed before the time limit; the
report contains no error.

All 40 readiness records completed: five cold-window samples in the same process, five discarded warm-ups and 30 measured warm samples. The 105 interaction records also completed:
five warm-ups and 30 measured samples each for filtering, scrolling and type switches. Search and selection each discard five warm-ups and record 30 samples. Every measured scroll
moved the viewport.

The quick searches alternate `Player` and `World`, which have common name matches. The filter alternates an absent label and a cleared filter; it does not edit real asset labels.
This is not a rare/no-match real-field search, partial-index distribution, many-type matrix, Undo/import/move/delete benchmark or fresh-process experiment.

## Observations

| Scenario                        | Median (ms) | p95 (ms) |
| ------------------------------- | ----------- | -------- |
| window-open-to-index-ready-warm | 6395.25     | 6804.89  |
| search-first-results            | 41.87       | 57.95    |
| selection                       | 19.09       | 20.84    |
| label-filter-observed-ready     | 33.56       | 42.58    |
| object-scroll-observed-ready    | 35.30       | 40.53    |
| type-switch-observed-ready      | 2000.86     | 2381.90  |

p95 uses nearest rank: sample 29 of 30. Search includes setting the field through first observed results, including scheduled work; it is not a full-popover paint measurement. The
common-query p95 is below 100 ms for this scenario only. It does not satisfy #310's complete partial/rare/representative-field acceptance matrix.

Every readiness and interaction record reports zero strong references owned by the global search cache. Readiness records still have 5,000 loaded selected-type rows. This confirms
the search-cache ownership change in this scenario, not bounded browser or native memory. Realized rows and process/Unity memory observations remain in the raw file; native
residency and allocator retention are not isolated.

The driver resets the shared type index when closing each measured window. Its warm mode means a warm editor process, not a persistent shared-index hit across window closes. Shared
record reuse is covered by focused tests and can occur during in-window rebuilds; no across-close speedup is claimed from this driver.

Fixture staging took 572.31 seconds, including 452.82 seconds of import-batch flushes; the final refresh took 329.07 ms. The longest staging tick was 782.04 ms, so the staging
target is cooperative, not a hard bound. The observed editor-update gap and driver-tick maxima are 1163.28 and 8872.79 ms. They have different observer boundaries and do not prove
the longest whole-editor stall or attribute all package versus Unity cost.

## Cleanup and limits

After the driver reported Idle, an independent host check found zero owned fixture roots, zero owned state files, zero shared metadata records, zero pending index paths and no
suspended index. The host returned to its original one native window host and seven editor windows. No pre-existing asset was deleted.

The synthetic [shared-store experiment](million-record-feasibility.md#shared-store-follow-up-on-22-october-2026) is separate and was measured at its own earlier revision. Keep
#309, #310 and #323 open for bounded selected-row retention, Library cache/version/recovery, real/native memory slopes, broader partial/rare query and change-operation matrices,
global stall and input-to-cancellation evidence, fresh-process/Play/compile controls and wider platforms. No million-real-asset support follows from these observations.
