# Shared sorted query snapshots

Measured on 22 October 2026 at [e66610d](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/e66610d). The exact emitted
[schema-4 report](performance-shared-snapshots-2026-10-22.json) has SHA-256 `23c5c1b6490189ac234fe33af35b9baa3fdf3301704d2b88b4829067fe88106c`. The benchmark source SHA-256 is
`41ae6e9f09854e700ee53f854fc831d919e0bccddbcfbeb0bba48437785954e8`. Production snapshot code is unchanged from 1bdff91.

The Windows 11 Unity 6000.5.2f1 host uses a Snapdragon X Elite, Adreno X1-85, 65,011 MB reported RAM, one display and 96 DPI. This is one warm editor process. The opt-in experiment
completed all 18 scenarios in 334.41 seconds, with five warmups and 30 measured queries per condition. It creates synthetic Unicode metadata, nested field text, labels and paths,
without Unity assets or native asset loads. The unchanged limits are 30 minutes and a 2 GiB construction-heap guard.

Complete sorted views use the production cache query factory. Partial views still copy and cooperatively sort 10% of each dataset. Scanned and matched counts are asserted for every
observation. Timing includes constructor work and calls to Advance; runner wall time also includes editor-update waits. These component observations exclude input, debounce,
rendering and real asset extraction.

## Concurrent-query ownership

Both batches contain 32 live queries over the same sorted view. The copied-input constructor is the positive control. Its complete batch stays rooted while the shared batch is
added and measured. The scope clears both batches before returning. Each delta follows a full collection and is process-wide, not an allocation counter; zero means no net increase
at the observed resolution.

| Records   | Copied queries: heap delta (MiB) | Added shared queries: heap delta (MiB) | Full-query preparation p95 range (ms) |
| --------- | -------------------------------- | -------------------------------------- | ------------------------------------- |
| 10,000    | 2.5000                           | 0.0117                                 | 0.0142–0.0436                         |
| 100,000   | 24.5000                          | 0.0000                                 | 0.0147–0.0457                         |
| 1,000,000 | 244.2500                         | 0.0000                                 | 0.0074–0.1372                         |

The ordinary regression first failed with 2,670,592 retained bytes for 32 copied queries at 10k. Its shared-query version passes a 128 KiB bound. Public constructor inputs remain
copied, and cache mutations preserve older query text and order. The test also covers remove, replace, add, clear, synchronous/cooperative sort and loaded-asset reset paths.

## Cost of the first edit

Sharing changes who pays for the reference-array copy: the first later mutation detaches the cache list; another mutation before another shared query reuses that list. These are
single warm observations, not distributions. The timed unit includes UseMetadata and its assertion; replacement-record construction is outside that timer. Heap deltas include the
still-live older query.

| Records   | First change (ms) | Second change (ms) | Additional retained heap (MiB) |
| --------- | ----------------- | ------------------ | ------------------------------ |
| 10,000    | 0.4785            | 0.0586             | 0.0781                         |
| 100,000   | 0.4028            | 0.0238             | 0.7500                         |
| 1,000,000 | 0.8347            | 0.0109             | 7.6211                         |

## Real 10k follow-up

The unchanged existing driver at e66610d completed its real-asset run within the original 20-minute watchdog. The exact emitted
[report](performance-shared-snapshots-real-10k-2026-10-22.json) has SHA-256 `48c2288c963166ae383c88e2a923eb2b3a6defd7f63e6b066eb01ba1604305ec`. Its driver SHA-256 is
`642fbafdd8baa81527b4c89da75414c20335772eafaebfd3062455f7ab9891c4`. Staging uses 64 ms/100-item limits; query slices remain configured at 4 ms.

All 10,000 uniquely owned fixtures were staged, half ordinary ScriptableObjects and half BaseDataObjects. All 140 query, 40 readiness and 105 interaction observations completed.
Each query condition has five warmups and 30 warm samples over 10,014 available records, including the existing background. Every measured scroll moved. Rare and absent conditions
scanned the entire snapshot with expected match counts.

| Condition   | Completed publication median / p95 (ms) |
| ----------- | --------------------------------------- |
| common-name | 46.9809 / 60.5581                       |
| rare-guid   | 46.4008 / 55.0250                       |
| rare-field  | 47.8924 / 53.2614                       |
| no-match    | 25.4281 / 32.8696                       |

p95 is nearest rank over 30 samples. These clocks run from scripted field input to observed current query completion in the UI tree. They include input and editor scheduling, but
do not confirm layout, paint or GPU completion. No-match first-result values remain -1. These observations do not prove rendered partial-index latency or a general 100 ms
guarantee.

Global search retained zero strong asset references. The selected type still retained 5,000 objects, with at most 18 realized rows. The longest observed index slice was 51.5209 ms;
query slices reached 4.0381 ms with 329 counted overruns including warmups. These are not whole-frame bounds.

The editor process stayed warm. Cold windows and warm reopens are not fresh-process samples; the driver resets the shared index between windows, so warm reopens do not demonstrate
an across-close cache hit. This sequential follow-up uses different fixture GUIDs and process history from earlier reports and is not a controlled speedup comparison.

Independent cleanup confirms an idle driver and AssetDatabase, stopped Play Mode, one native host, seven editor windows, no owned fixture roots or state files, zero shared
metadata/pending paths, no suspension and 228 ScriptableObject assets, matching the pre-run count.

## Remaining limits

The metadata store, view and tracker still retain about 1.4 GiB at one million records. Rare and absent full queries still scan every document; their active p95 is about 1.64
seconds at that size. Construction units reached 269.703 ms and a forced collection reached 1,103.6381 ms. Sharing query snapshots does not establish a whole-frame budget,
input-to-cancellation latency or million-asset support.

The raw report retains partial-query preparation, ordering, active/wall times, slice overruns and cancellation-component observations. Current synthetic measurements do not replace
the real/native memory, persistent Library-cache benefit/recovery, bounded selected-row retention, rendered partial/complete latency, fresh-process controls or wider platform
criteria in #309, #310 and #323.
