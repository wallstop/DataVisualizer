# Test artifacts

This directory preserves recorded test evidence. It is excluded from the documentation site and the UPM/npm package. Raw JSON reports and their Unity metadata moved here from
`docs/` without changing their bytes or GUIDs. Historical commit links still locate the original reports.

- [Performance observations](performance.md): staging, readiness, search, refresh, intake, raw reports, environments and incomplete attempts.
- [Subasset experiment](subasset-feasibility.md): test-only identity and read-only inspection evidence.
- [Prefab experiment](prefab-feasibility.md): test-only component discovery, inspection and cold-process observations.
- [Real 10k metadata/search observation](shared-search-10k-2026-10-22.md): final-tree interaction samples, ownership and cleanup limits.
- [Controlled real partial/full search](partial-real-search-2026-10-23.md): publication and visible-popover paint over 10k real fixtures.
- [Partial query snapshots](partial-snapshots-2026-10-23.md): partial/full construction, cooperative sorting and first-edit ownership costs.
- [Shared sorted query snapshots](shared-snapshots-2026-10-22.md): concurrent-query ownership, preparation and first-edit costs.
- [Million-record experiment](million-record-feasibility.md): synthetic metadata scale, resource estimates and limits.

These are observations at the recorded source revisions, not support guarantees or current benchmark results. Keep emitted samples unchanged; document corrections, provenance and
limitations separately. A cancelled or failed run is not a completed result. New transient outputs belong in ignored `.artifacts/` or the driver's configured temporary directory;
retain reports here only when they support reviewable tests or measurements. Keep source revisions, environments and failure status with retained evidence.

The user-facing [performance guide](../../docs/performance.md) explains expected behavior and support limits. Public docs contain user guidance, API/schema references and required
site assets. Test transcripts, raw samples, benchmark procedures and experiment design belong here; session logs belong in ignored `progress/`.

## Search completion observations

`PerfBaseline.RunBaseline` records `searchObservations` for common names, a rare GUID, a rare field token, and no matches. Each supported condition has five warm-ups and 30 warm
samples. It waits for a current query publication, including zero matches. By default, queries use a complete window index. `firstResultMilliseconds` records the first observed
preview row; it stays `-1` for no matches. `completedMilliseconds` records the first observed completed snapshot. Both include field input and editor scheduling, and do not verify
layout, paint or GPU completion. `publicationPaintMilliseconds` records the first subsequent `generateVisualContent` callback on the visible search popover after current
publication is observed. The observer rejects hidden/detached popovers and changed query text or generations. The driver requests a repaint after observing publication, so this is
a conservative later UI Toolkit paint-generation observation; it includes observer scheduling and does not establish GPU presentation, compositor completion or a user's physical
input latency. The publication clock remains separate. Missing paint observations stay `-1` and prevent a successful sample.

Rare field tokens include Unicode and are deterministic fixture strings on `BaseDataObject.Description` and `OtherEditorOnlyCreationData.SearchText`. Other ordinary fixture types
skip that condition. The legacy `search-first-results` scenario retains common-name first-result observations. Common queries can stop at the production result cap; rare/no-match
cases scan the full snapshot. Each observation records metadata hits, published preview rows, result-cap status, query overrun deltas and the cumulative window maximum query slice.
Cancelled reports keep the active sample with `complete=false` and unavailable completion time. Optional `partialSearchFraction` (zero by default; otherwise strictly between zero
and one) opens an extra controlled window before ordinary readiness measurements. The window first reaches initial readiness, then starts a production cache rebuild. Extraction
pauses once the available record count reaches that fraction of the requested fixture count. The report records the requested fraction and actual available records; pre-existing
tracked assets can contribute records, so this is a count threshold, not an exact fixture-completeness percentage. Cache completion before capture or any later cache version/count
change fails the run. Selected rows seed this warm rebuild, so the actual count can exceed the requested threshold; this is not cold-index latency. The extra window is discarded
after queries and excluded from ordinary readiness observations.

`indexMode` distinguishes `paused-partial`, `live-rebuild`, and ordinary `complete` windows. `availableRecordsAtInput` and `indexCompleteAtInput` describe the input boundary;
`records` and `indexComplete` describe the published snapshot. Optional `pausePartialExtraction: false` leaves extraction running after the threshold and starts the first query
immediately, before that rebuild completes. Later inputs can arrive after completion: group live-rebuild results by actual input completeness rather than treating all of them as
partial-index samples. This is a warm metadata rebuild, not cold extraction or sustained import traffic.

Each supported condition retains five warm-ups and 30 samples. Rare targets expect their presence in the current published snapshot; common names can be absent from partial input.
The driver resets publication and paint clocks when a new query generation supersedes an observed publication, and records `invalidatedPublications`. The final clocks refer to the
same generation; first-result time can refer to an earlier preview. `targetGuid` identifies a rare fixture independently of its text. Paused observations reject all count/version
changes. Live observations permit arrivals and verify rare-target presence at current publication. Neither mode measures physical input-to-cancellation or GPU presentation.

The [22 October real 10k query-publication report](search-completion-10k-2026-10-22.md) retains complete rare/no-match observations and the earlier cancelled staging attempt.

The [Unicode-prefilter follow-up](search-unicode-10k-2026-10-22.md) records the later 10k publication observations, matching coverage and normalized-string memory tradeoff.

The [final-index ordering experiment](final-index-ordering-2026-10-22.md) records current Unicode metadata scaling, cooperative final-sort calls and an incomplete real 10k
readiness attempt.

## Benchmark code provenance

The test host must provide Unity's `com.unity.nuget.mono-cecil` package (`Mono.Cecil.dll`). This is a test-only reference; it is excluded from the shipped package.

The synthetic metadata driver emits schema 6. Before collecting records, it matches the loaded test module ID against the disk assembly and compares the driver source bytes with
its compiler-recorded PDB document checksum. Missing symbols, stale source, a pending compilation/import or a replaced assembly stop the run. Reports distinguish the on-disk source
SHA-256, compiler checksum/algorithm, loaded module ID and assembly SHA-256. Checkpoints and final completion recheck source and assembly bytes outside query timing intervals.
`provenanceVerified=false` or a nonempty `provenanceFailure` invalidates a report; `complete=false` remains incomplete. This proves driver identity, not the identity of every
production dependency. Historical schemas do not provide these checks and remain observations at their documented revisions.
