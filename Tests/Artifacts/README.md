# Test artifacts

This directory preserves recorded test evidence. It is excluded from the documentation site and the UPM/npm package. Raw JSON reports and their Unity metadata moved here from
`docs/` without changing their bytes or GUIDs. Historical commit links still locate the original reports.

- [Performance observations](performance.md): staging, readiness, search, refresh, intake, raw reports, environments and incomplete attempts.
- [Subasset experiment](subasset-feasibility.md): test-only identity and read-only inspection evidence.
- [Prefab experiment](prefab-feasibility.md): test-only component discovery, inspection and cold-process observations.
- [Real 10k metadata/search observation](shared-search-10k-2026-10-22.md): final-tree interaction samples, ownership and cleanup limits.
- [Million-record experiment](million-record-feasibility.md): synthetic metadata scale, resource estimates and limits.

These are observations at the recorded source revisions, not support guarantees or current benchmark results. Keep emitted samples unchanged; document corrections, provenance and
limitations separately. A cancelled or failed run is not a completed result. New transient outputs belong in ignored `.artifacts/` or the driver's configured temporary directory;
retain reports here only when they support reviewable tests or measurements. Keep source revisions, environments and failure status with retained evidence.

The user-facing [performance guide](../../docs/performance.md) explains expected behavior and support limits. Public docs contain user guidance, API/schema references and required
site assets. Test transcripts, raw samples, benchmark procedures and experiment design belong here; session logs belong in ignored `progress/`.

## Search completion observations

`PerfBaseline.RunBaseline` records `searchObservations` for common names, a rare GUID, a rare field token, and no matches. Each supported condition has five warm-ups and 30 warm
samples. It waits for a current query publication, including zero matches, and a complete window index. `firstResultMilliseconds` records the first observed preview row; it stays
`-1` for no matches. `completedMilliseconds` records the first observed completed snapshot. Both include field input and editor scheduling, and do not verify layout, paint or GPU
completion.

Rare field tokens include Unicode and are deterministic fixture strings on `BaseDataObject.Description` and `OtherEditorOnlyCreationData.SearchText`. Other ordinary fixture types
skip that condition. The legacy `search-first-results` scenario retains common-name first-result observations. Common queries can stop at the production result cap; rare/no-match
cases scan the full snapshot. Each observation records metadata hits, published preview rows, result-cap status, query overrun deltas and the cumulative window maximum query slice.
Cancelled reports keep the active sample with `complete=false` and unavailable completion time. These runs do not measure partial-index or fresh-process latency.

The [22 October real 10k query-publication report](search-completion-10k-2026-10-22.md) retains complete rare/no-match observations and the earlier cancelled staging attempt.
