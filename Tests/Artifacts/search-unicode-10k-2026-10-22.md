# Unicode query publication over 10,000 real fixtures

Measured on 22 October 2026 at [9942214](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/994221432c97e01248201fbf06ba9cca39e00c55). The exact emitted
[report](performance-search-unicode-10k-2026-10-22.json) has SHA-256 `a629e8ba85f3ff85eee0ff79e6287e7801a661b8d35fe9c75e961efa21492191`. The unchanged driver SHA-256 is
`642fbafdd8baa81527b4c89da75414c20335772eafaebfd3062455f7ab9891c4`.

The Windows Unity 6000.5.2f1 run owns 10,000 real assets, half ordinary ScriptableObjects and half BaseDataObjects. It completed within a declared 20-minute watchdog, with 140
complete query, 40 readiness and 105 interaction records. Every measured scroll moved. Each query condition has five warm-ups and 30 measured warm samples. All snapshots contain
10,014 records, including 14 existing assets.

These are sequential observations in one warm editor process. The [baseline](search-completion-10k-2026-10-22.md) used 32 ms/20-item staging; this follow-up used 64 ms/100-item
staging, with new fixture GUIDs and different process history. Both keep the 4 ms query budget. This is not a controlled speedup experiment.

| Condition   | Baseline completion p95 (ms) | Follow-up first preview p95 (ms) | Follow-up completion median / p95 (ms) |
| ----------- | ---------------------------- | -------------------------------- | -------------------------------------- |
| common-name | 273.16                       | 48.94                            | 40.26 / 48.94                          |
| rare-guid   | 102.05                       | 34.11                            | 31.84 / 41.58                          |
| rare-field  | 329.60                       | 34.02                            | 45.53 / 56.53                          |
| no-match    | 101.55                       | unavailable                      | 22.42 / 31.00                          |

Timings run from scripted field input to the first observation of preview rows in the UI tree or a current completed query publication. They include input work and editor
scheduling. They do not verify layout, paint or GPU completion. No-match first-result values remain `-1`; p95 uses nearest rank over 30 samples. These conditions are below 100 ms
at publication, without proving the broader user-visible target or partial-index latency.

The cumulative maximum query slice is 4.0380 ms, with 286 counted overruns including warm-ups. These counters exclude rendering and do not bound complete frames. Cold windows and
warm opens share one warm process; they are not fresh-process controls.

The candidate cache follows [invariant uppercase ordinal comparison semantics](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings). Original
strings still decide matches, sources, GUID equality and OR behavior. Eligible Unicode documents retain one additional normalized text string; ASCII documents keep their existing
single cache. This run does not establish native memory or the million-record memory slope; #309 and #323 own those measurements.

The measured version conservatively bypassed supplementary characters. The final eligibility change accepts well-formed surrogate pairs while leaving malformed UTF-16 and
partial-surrogate terms on runtime matching. It produces the same candidate strings for this BMP field workload, but this report is not a real-asset supplementary-character latency
measurement or a timing of that later head. Runtime parity coverage includes BMP case groups, emoji, supplementary case pairs and malformed strings.

Global search retained zero strong object references. Selected-type rows retained 5,000 objects, and realized rows peaked at 18. This is not a zero-object window or a native-memory
slope claim.

Independent cleanup confirms an idle driver and AssetDatabase, stopped Play Mode, one native host, seven editor windows, no owned fixture roots or state files, zero shared
metadata/pending paths and no suspension.

Partial indexes, confirmed paint latency, fresh processes, actual domain-reload refresh, matched Play/compile controls, the full 50k/100k and many-type matrices, and wider
platforms remain open in #306, #308 and #310.
