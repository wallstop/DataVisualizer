# First available search snapshot observations

This evidence advances #306 and #310. Their broader acceptance remains open; #324's arrow-order repair is a separate change.

## Source and method

The [emitted report](performance-first-snapshot-1k-10k-2026-10-24.json) retains its original bytes. Measured C# source is
[3e3843e](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/3e3843ea3db6754383fc804ff2b1cb23d65a8b4f); the later evidence commit changes no C# source.
[Preflight and postflight](first-search-snapshot-2026-10-24-provenance.json) match compiler checksums, loaded module IDs and assembly bytes for the driver, metric model, browser
and catalog controller. All 205 Editor/Runtime/Tests C# paths match before and after. The sorted path/NUL/file-SHA-256/newline manifest SHA-256 is
`41abafce122cce60444cfd0b680b5a2befcb19718351c1fab40119d08fba07e5`.

Unity 6000.5.2f1 runs on Windows 11, a Snapdragon X Elite host with 12 logical processors and 65,011 MiB reported memory. The single-display environment uses 96 DPI, one editor
pixel per point and the dark theme. This is one warm process, not a fresh-process or cross-platform experiment.

The original 30-minute watchdog is unchanged. Staging uses 64 ms/100 items; both uniquely owned real-asset datasets complete. Each splits evenly between ordinary
`OtherEditorOnlyCreationData` and `PlayModeDataObject` BaseDataObjects. Each size retains 280 queries, 40 readiness observations and 105 interactions. Every query condition/mode
has five discarded warmups and 30 warm samples; readiness has five cold-window observations in the same process, five warmups and 30 warm samples. All measured scrolls move.

Live extraction is not paused. Every live input starts a fresh warm rebuild after prior extraction settles outside its input clock. Selected rows seed the input view with 500/5,000
records, above the requested 10% threshold. Complete controls have 1,014/10,014 records, including 14 pre-existing managed records. These are different populations; this is not
cold extraction, sustained edits or a controlled speedup.

Focus was not held constant. The editor was observed inactive during the 10k live window. At 05:58:09 UTC, after 58 query observations, public SceneView focus was restored once
while `WaitingPartialIndex` was outside a measured input. Later status probes observed an active editor. Retain this mixed-focus limitation: the distributions do not establish
matched-foreground performance or causal differences from older runs.

## Publication and paint clocks

`firstSnapshotMilliseconds` records the first completed available-record UI publication directly, including zero matches, and stays fixed across later generations. Its population,
generation, index completeness and cache currency are separate fields. `firstSnapshotPaintMilliseconds` records the first later visible popover paint-generation callback for a
published snapshot; its own population and generation can differ if publications coalesce. Unit coverage includes that replacement case. In this run, first publication and first
paint retain the same available-record populations.

The existing `completedMilliseconds` and `publicationPaintMilliseconds` clocks remain separate and require a current cache version. Currency does not mean indexing is complete. All
280 live publications, and their first paint observations, remain partial: 500/5,000 records at publication. Every first live publication is current at its callback, with zero
recorded publication resets. Each dataset has 87 zero-match live snapshots, including rare GUIDs absent from partial input. An early empty snapshot is not a successful find of an
asset that has not arrived.

The table reports warm p95 milliseconds using nearest rank, sample 29 of 30. Common queries may stop at the 25-result cap. Paint means a visible popover `generateVisualContent`
callback, not GPU presentation or a user's physical input. These clocks do not establish input-to-cancellation latency.

| Assets | Index mode   | Query       | First publication | First paint | Current publication | Current paint |
| ------ | ------------ | ----------- | ----------------: | ----------: | ------------------: | ------------: |
| 1,000  | live-rebuild | common-name |             7.936 |      40.183 |              99.977 |       101.658 |
| 1,000  | live-rebuild | rare-guid   |             2.978 |       8.567 |              68.759 |        74.620 |
| 1,000  | live-rebuild | rare-field  |             2.994 |      20.508 |              68.524 |        72.367 |
| 1,000  | live-rebuild | no-match    |             3.471 |      69.035 |              66.359 |        69.035 |
| 1,000  | complete     | common-name |             9.880 |      27.690 |              37.104 |        38.435 |
| 1,000  | complete     | rare-guid   |             3.890 |      22.770 |              19.573 |        22.770 |
| 1,000  | complete     | rare-field  |             5.424 |      27.256 |              24.009 |        27.256 |
| 1,000  | complete     | no-match    |             4.194 |      25.982 |              22.495 |        25.982 |
| 10,000 | live-rebuild | common-name |           479.188 |     485.403 |             480.615 |       485.403 |
| 10,000 | live-rebuild | rare-guid   |           410.216 |     417.759 |             412.414 |       417.759 |
| 10,000 | live-rebuild | rare-field  |           141.307 |     148.505 |             142.795 |       148.505 |
| 10,000 | live-rebuild | no-match    |           261.930 |     272.243 |             263.791 |       272.243 |
| 10,000 | complete     | common-name |            73.553 |     101.257 |              75.242 |       101.257 |
| 10,000 | complete     | rare-guid   |            53.920 |      61.005 |              55.353 |        61.005 |
| 10,000 | complete     | rare-field  |            57.062 |      63.802 |              58.363 |        63.802 |
| 10,000 | complete     | no-match    |            44.721 |      52.150 |              46.539 |        52.150 |

The 10k live p95 values remain above 100 ms. Complete-control common-name paint also slightly exceeds 100 ms. No overall search-target pass is claimed, and mixed focus prevents a
foreground-target conclusion.

## Opening and retention

Warm readiness p95 remains separate from query publication and full-index readiness:

| Assets | Usable rows (ms) | Inspector (ms) | Full index (ms) |
| ------ | ---------------: | -------------: | --------------: |
| 1,000  |          469.513 |        469.513 |        2419.555 |
| 10,000 |          861.951 |        861.951 |       11121.889 |

Opening remains above the 100 ms target. Readiness retains 500/5,000 selected-type objects, at most 18 realized rows and zero strong global-search cache references. Every query
records zero result-resolution loads and zero direct preview-row asset references at current publication paint; those counters exclude index extraction, event closures, selected
rows and native memory. Zero does not mean zero allocation.

The maximum reported query component slice is 70.1954 ms at 10k, including warmups; the index maximum is 39.7521 ms. These are component observations, not global editor-stall
bounds or a hard 4 ms guarantee. Full fixture/distribution/change-operation, fresh-process, Play/compile, native-memory and platform matrices remain outstanding.

Postflight cleanup restores 72 .asset paths, seven editor windows, zero browser windows and one native host. No owned fixture paths, pending index paths or suspended work remain.
The disposable shared metadata cache is cleared; its preflight count was 14. No release or support expansion follows from this experiment.

## Excluded development timeout

The [512-fixture development report](first-snapshot-512-timeout-2026-10-24.json) retains the original cancellation bytes. NUnit hit its 180-second default, but its owned driver
continued because timeout skipped coroutine disposal. At later explicit cancellation, the report contains all 280 queries and 105 interactions but only 25 of 40 readiness records;
these are cancellation-time counts, not exact counts at the NUnit deadline. Cancellation removed its fixtures and window. This preliminary development tree predates the final
publication helper and test-deadline repairs. It is excluded from all final timing tables and makes no final-head claim.

The three full partial/complete matrix tests now allow six minutes around their unchanged five-minute driver watchdog and cancel their fixture-owned baseline from teardown. Fixture
sizes, warmups, measured samples and driver deadlines were not reduced to obtain a passing result.
