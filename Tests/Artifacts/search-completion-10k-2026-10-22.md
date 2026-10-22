# Query publication over 10,000 real fixtures

Measured on 22 October 2026 with the existing `PerfBaseline` driver at
[d831507](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/d831507bff92f2df22372843e2ec3946e6e781df). The driver SHA-256 is
`642fbafdd8baa81527b4c89da75414c20335772eafaebfd3062455f7ab9891c4`. The retained JSON has SHA-256 `1ede5e328062e164990a686aeadde1d11ada92a581e18b42cd8323e10b3fbb06`.

This Windows Unity 6000.5.2f1 run uses 10,000 uniquely owned real assets, split equally between an ordinary ScriptableObject and `BaseDataObject`. A 20-minute watchdog was declared
before the run. Staging uses the prior successful 32 ms/20-item profile; the query budget remains 4 ms. Individual native calls and complete editor frames can exceed those budgets.

The raw report contains 140 complete query records, 40 complete readiness records and 105 complete interaction records. Each supported query condition has five warm-ups and 30
measured warm samples. Rare fields use a deterministic Unicode token. Common names can stop at the production result cap; rare and absent queries scan all available records. The
snapshot includes 10,014 records, including existing assets of the managed types.

| Condition   | First preview row median / p95 (ms) | Completion median / p95 (ms) |
| ----------- | ----------------------------------- | ---------------------------- |
| common-name | 211.95 / 273.16                     | 213.11 / 273.16              |
| rare-guid   | 28.13 / 37.48                       | 92.92 / 102.05               |
| rare-field  | 140.28 / 173.52                     | 283.41 / 329.60              |
| no-match    | unavailable                         | 71.88 / 101.55               |

Times start at scripted text-field input and end at the first editor observation of a preview row in the UI tree or a current completed query publication. They include scheduling
and synchronous input work. They do not verify layout, paint or GPU completion. No-match first-result values are unavailable (`-1`). The p95 values use nearest rank over 30
samples. These observations do not prove the full 100 ms user-visible target, partial-index latency, fresh-process performance, other platforms or a controlled speedup.

Common-name and rare-field publication p95 exceed 100 ms. The largest cumulative window query slice was 8.8324 ms; the query counter recorded 1483 overruns including warm-ups.
These counters exclude rendering and do not bound complete frames.

Five cold-window observations reuse the warm editor process. Warm window opens, filtering, selection, scrolling and type switches remain in the raw report; they are not
fresh-process controls. Selected-type rows still retain loaded assets. Zero strong global-search references does not mean that the window retains no objects or establish a
native-memory slope.

An earlier default-profile attempt was deliberately cancelled at 310/10,000 fixtures because its observed staging throughput did not fit this run's budget. Its error report retains
that partial count; it is not a completed 10k measurement or a controlled staging comparison. Cleanup removed its owned root before the measured run started.

All measured scroll samples moved. Global search retained zero strong object references; selected-type rows retained 5,000 objects and realized rows peaked at 18. Independent
cleanup confirms an idle driver, one native host, seven editor windows, no owned fixture roots or state files, zero shared metadata/pending paths, no suspension and an idle
AssetDatabase.

Raw reports: [completed run](performance-search-completion-10k-2026-10-22.json) and [cancelled staging attempt](performance-search-staging-attempt-2026-10-22.json).

The same C# tree passes all 772 EditMode and 15 PlayMode cases on this host. Unity 2021.3, domain-reload refresh, the full 50k/100k and many-type matrices, partial-index and
broader end-to-end search distributions, matched Play/compile controls and wider platform evidence remain open in #306, #308 and #310.
