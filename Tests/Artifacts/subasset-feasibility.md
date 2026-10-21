# Read-only ScriptableObject subasset feasibility

This is an experiment for [#320](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/320), not shipped subasset support. The browser, query service and JSON API still
accept exact main assets only. The experiment creates and deletes only its own temporary fixtures.

## Decision

Proceed with a separate, opt-in read-only source adapter after the shared metadata and editor-service foundations (#309/#315). Do not add subassets to existing GUID-only queries or
saved selections. Native `.asset` containers passed the identity and default-inspection checks below. Imported containers and custom inspectors need their own evidence.

A parent GUID identifies a file, not an object inside it. The main-only control returned 20 records; the prototype returned all 80 ScriptableObjects from those same 20 files.
Duplicate names and duplicate authored IDs cannot distinguish those objects. Use the parent GUID and a signed 64-bit local file ID together; do not use array position or names.

## Reproduction and results

Source: `052776766ebada075c18dea1e7dee498080b5f86`. [Raw report](subasset-feasibility-2026-10-20.json) includes every warm sample and environment field. Run in a disposable host
with the package's test assembly enabled:

```csharp
SubassetFeasibilityTests.RunExperiment(outputPath, sourceRevision, parentCount: 20);
```

The public driver is in `WallstopStudios.DataVisualizer.Tests.Editor`. It rejects counts outside 1–100, owns a unique `Assets/SubassetFeasibility-*` folder, checks every expected
record count, and deletes that folder through its cleanup scope. No pre-existing fixture path is reused. Production does not reference the prototype.

| Observation                           | Result                                      |
| ------------------------------------- | ------------------------------------------- |
| Fixtures                              | 20 real `.asset` files; 80 SOs, 20 textures |
| Fixture creation and saves            | 5,501.355 ms                                |
| Main-only control, one warm pass      | 20 records, 1.552 ms                        |
| First prototype read in warm session  | 80 records, 9.027 ms                        |
| Warm discovery, 5 warmups/30 samples  | median 1.043 ms; p95 1.558 ms; max 1.590 ms |
| Managed heap delta per warm pass      | median 28,672 B; range 28,672–32,768 B      |
| Metadata fields holding Unity objects | 0                                           |
| Owned fixture cleanup                 | Complete                                    |

Environment: Unity 6000.5.2f1, Windows 11, Snapdragon X Elite, Adreno X1-85, 65,011 MB reported RAM, 96 DPI, 1 pixel per point, dark theme. There were 6,510 background asset paths.
Storage-device and filesystem-cache state were not measured. Fixture creation is separate from discovery; this is not a first-paint, project-import or cold-start benchmark. The
first read follows fixture creation and the main-only control. There are no fresh-process cold samples or scaling claims.

The host's `GC.GetAllocatedBytesForCurrentThread` returned zero even for a deliberate 1,024-byte allocation. The report marks it unsupported and uses `-1` for those samples.
`Profiler.GetMonoUsedSizeLong` deltas describe heap occupancy during each pass, not allocation totals or retained memory. Metadata contains copied strings/value fields; each
parent's loaded-object array is temporary. Unity may still cache native objects, so this does not prove bounded engine memory or justify a Library cache yet.

## Behavior verified

`SubassetFeasibilityTests` checks mixed main/subasset types, three duplicate child names, two BaseDataObjects sharing an authored ID, and a non-SO object in the same file. It
checks unique composite identities across a forced reimport and parent move. Resolution follows the GUID to the current parent and finds the exact local ID and type, never falling
back to the main asset. Parent deletion makes every selection unavailable. Unity can retain the deleted GUID's old path, so a nonempty GUID-to-path result alone is not an existence
check.

The inspector prototype binds only default serialized properties in a disabled hosted UI Toolkit tree. All property fields are disabled; inspection leaves asset bytes, dirty state
and authored IDs unchanged. It never invokes asset-operation hooks or a custom inspector. This is read-only interaction, not a security boundary against user callbacks.

The observed subasset IDs include signed values beyond 32 bits. Preserve the full `long`; use decimal strings when exposing IDs to JSON clients that use floating-point numbers. The
authored `BaseDataObject.Id` remains unrelated and is never rewritten.

## Identity and state proposal

- Add a versioned source identity: source kind, parent GUID, signed 64-bit local ID and exact type identity. Keep the main-asset source kind distinct.
- Migrate an old GUID-only selection only to that file's exact main object. Never choose the first child or infer a match from a duplicate name or authored ID.
- Keep unresolved selections explicit when a parent, local ID or type disappears. Preserve old user data until migration succeeds and support user/project state equally.
- Store metadata by composite identity. Invalidate every child record when its parent changes, moves, disappears or is reimported; importer-generated IDs may change.
- Resolve only the selected object for inspection. Drop the previous binding before switching or closing. Keep operation and processor APIs main-asset-only initially.
- Version any future Library cache separately, fingerprint parent dependencies/scripts, discard stale or corrupt records, and measure benefit before adding disk persistence.

## Follow-up boundary and API floor

The implementation proposal needs opt-in source configuration, shared-index invalidation, composite paging/selection contracts, read-only UI affordances, and migration tests.
Create/delete/move, arbitrary processor access, imported-container guarantees and custom inspectors require separate design. No automatic adoption is authorized by these results.

The Unity 2021.3 API pages document [LoadAllAssetsAtPath](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AssetDatabase.LoadAllAssetsAtPath.html), including hidden
objects and no main-first guarantee, and the
[long local-ID overload](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AssetDatabase.TryGetGUIDAndLocalFileIdentifier.html). The prototype identifies main assets
explicitly. Its default inspection uses serialized properties and [PropertyField](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/UIElements.PropertyField.html), not
newer TreeView APIs. Execution was verified only on the host above; Unity 2021.3, macOS/Linux, importer behavior and large-project native retention remain unverified. Keep the
Unity 2021.3 package floor unchanged.
