# Test artifacts

This directory preserves recorded test evidence. It is excluded from the documentation site and the UPM/npm package. Raw JSON reports and their Unity metadata moved here from
`docs/` without changing their bytes or GUIDs. Historical commit links still locate the original reports.

- [Performance observations](performance.md): staging, readiness, search, refresh, intake, raw reports, environments and incomplete attempts.
- [Subasset experiment](subasset-feasibility.md): test-only identity and read-only inspection evidence.
- [Prefab experiment](prefab-feasibility.md): test-only component discovery, inspection and cold-process observations.
- [Million-record experiment](million-record-feasibility.md): synthetic metadata scale, resource estimates and limits.

These are observations at the recorded source revisions, not support guarantees or current benchmark results. Keep emitted samples unchanged; document corrections, provenance and
limitations separately. A cancelled or failed run is not a completed result. New transient outputs belong in ignored `.artifacts/` or the driver's configured temporary directory;
retain reports here only when they support reviewable tests or measurements. Keep source revisions, environments and failure status with retained evidence.

The user-facing [performance guide](../../docs/performance.md) explains expected behavior and support limits. Public docs contain user guidance, API/schema references and required
site assets. Test transcripts, raw samples, benchmark procedures and experiment design belong here; session logs belong in ignored `progress/`.
