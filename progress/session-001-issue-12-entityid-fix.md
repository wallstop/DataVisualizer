# Session 001 — Issue #12: Unity 6000.5 `GetInstanceID` CS0619 → `EntityId`

- **Date:** 2026-07-05
- **Issue:** [wallstop/DataVisualizer#12](https://github.com/wallstop/DataVisualizer/issues/12)
- **Branch:** `dev/wallstop/bug-fixes`
- **Goal:** Minimal, tested, stable fix + version bump to `0.0.35`, verified red-green across Unity 2022.3 / 6000.4 / 6000.5, shipped as a fully green PR.

## Findings (research phase)

- Exactly **one** `GetInstanceID()` call site repo-wide: `Editor/DataVisualizer/DataVisualizer.cs:5890` (`BuildObjectRow`), used only to build a write-only UI Toolkit element name `object-item-row-{id}` (grep: the prefix appears nowhere else).
- `UnityEngine.EntityId` / `Object.GetEntityId()` were introduced in **Unity 6000.4** — the 6000.3 ScriptReference pages 404. Issue #12's suggested `UNITY_6000_3_OR_NEWER` guard is therefore wrong; correct guard is `UNITY_6000_4_OR_NEWER`.
- Official migration guidance (Unity 6000.5 manual, "Migrate from InstanceID to EntityId"): use `EntityId.ToULong`/`FromULong` for raw values; do not cast to `int`; do not persist `ToString()` output.
- Failure-mode sweep: no other obsolete InstanceID-family APIs in the package (`InstanceIDToObject`, `InstanceID` types, etc. — zero hits). The Unity 6000.5 RED compile log is the authoritative confirmation.

## Decisions (final)

- Helper `GetObjectIdString(this Object obj)` in new `Editor/DataVisualizer/Extensions/ObjectIdExtensions.cs` (testable; keeps `#if` out of the 6000-line `DataVisualizer.cs`). Name deliberately avoids "stable" — instance/entity IDs are per-session only.
- Helper is **`public`** (not `internal`). The initial plan used `internal` + `[assembly: InternalsVisibleTo("WallstopStudios.DataVisualizer.Tests.Editor")]`, but that was proven non-functional for this editor→test assembly pair (see the Investigation section below) — so the helper is public and there is no `InternalsVisibleTo`.
- Bootstrap `Tests/Editor` (first tests in this package), modeled on the sibling `unity-helpers` package's test asmdef. Single behavior-focused test; no bloat. The test references only the Editor assembly + `UnityEngine` + NUnit.
- Version: `0.0.35-rc05.6` → `0.0.35` (stable; user-confirmed).

## Evidence log

### RED-1 — Unity 6000.5.2f1 CLI compile of pristine package (reproduces #12)

Throwaway project `uproj-6000_5` (created via `Unity.exe -createProject`), manifest references the package via `file:` + `testables`. Batchmode compile (`-batchmode -nographics -quit`, no `-accept-apiupdate`):

```text
DataVisualizer.cs(5890,43): error CS0619: 'Object.GetInstanceID()' is obsolete:
'GetInstanceID is deprecated. Use GetEntityId instead. This will be removed in a future version.'
```

- The **only** `error CS` in the log; confirms the single-call-site failure mode. `git status` on the package stayed clean (Unity did not rewrite package source).
- Also observed: 72 `warning CS0618` (non-blocking) for unrelated UIToolkit deprecations — `EventBase.PreventDefault()` (45×) and `UxmlTraits`/`UxmlFactory` (27×) in `DataVisualizer.cs` + `UI/HorizontalToggle.cs`. These are **warnings, not errors**, a different failure mode from #12, and out of scope for this minimal fix (noted for a future task).

### Test-first RED — Unity 2022.3.62f2 CLI compile of package+tests (no helper yet)

Test assembly compiled and failed with **only** `CS1061: 'ScriptableObject' does not contain a definition for 'GetObjectIdString'` (3×). Confirms the test asmdef is wired correctly (nunit + refs resolve) and the test genuinely exercises the helper — it is not vacuous. On 2022.3 the package's `DataVisualizer.cs` compiles clean (GetInstanceID not obsolete pre-6000.4), so the missing helper is the sole error.

### GREEN — fix applied (helper + call-site)

- Editor assembly compiles with **0 errors** on 6000.4.6f1 (live, via MCP) and 6000.5.2f1 (CLI); the old `GetInstanceID` deprecation is gone. The actual issue #12 fix is verified.
- Confirmed in the built `WallstopStudios.DataVisualizer.Editor.dll`: contains `GetObjectIdString` and the `InternalsVisibleTo` friend string `WallstopStudios.DataVisualizer.Tests.Editor` (exact match to the test asmdef name).

### Investigation — test assembly CS1061 despite correct IVT

The test assembly initially failed with CS1061 (cannot see the `internal` helper) on both the live editor and the first CLI runs. The Editor DLL is correct (friend name + method present), so IVT *should* grant access. First hypothesis was Bee incremental-cache staleness — **ruled out**: a fully clean compile (deleted `Library/ScriptAssemblies` + `Library/Bee`) still produced CS1061. Conclusion: **InternalsVisibleTo does not grant the test assembly access to the `internal` extension method in this Unity/Roslyn setup** (root cause not pinned down, but proven non-functional across two clean compiles — not worth further spelunking).

**Second experiment (to be certain before conceding `public`):** reverted to `internal` + IVT and changed the test to call the helper via *static* syntax (`ObjectIdExtensions.GetObjectIdString(obj)`) instead of extension syntax, on a fully clean 6000.5 compile. Result: **CS0122 'ObjectIdExtensions' is inaccessible due to its protection level** — the internal *type itself* is invisible to the test assembly, not merely extension-method discovery. Combined with the earlier CS1061 (extension syntax), this conclusively proves IVT grants the test assembly **no** access to editor-assembly internals in this Unity/Roslyn setup, despite the correct friend string being present in the compiled editor DLL.

**Resolution (robust, not fragile):** make `ObjectIdExtensions` a `public static class` with a `public` method and drop the `InternalsVisibleTo` dependency entirely. The helper lives in the editor-only assembly; a public editor utility is a minimal, defensible API addition and removes all dependence on IVT quirks. The XML doc on the class records exactly why it is public. Verified by clean recompiles across the full matrix.

### Adversarial review dispositions (session sub-agent)

- **Version bump (must-fix):** performed last, after all green (below).
- **`public` vs `internal` (should-consider):** kept `public` — backed by the two IVT experiments above; documented the rationale in the class XML doc.
- **`.gitignore` accidentally modified (real find):** a stray edit had prepended a UTF-8 BOM to line 1 and appended `PLAN.md*` / `progress/` / `progress.meta` ignore rules — which would have *untracked* the very progress files this workflow requires. **Reverted** `.gitignore` to its original committed state.
- **Redundant asmdef reference (should-consider):** removed the unused `WallstopStudios.DataVisualizer` (Runtime) reference from the test asmdef; the test only needs the Editor assembly + UnityEngine + NUnit.
- **`.meta` MonoImporter block (nit):** left minimal 3-line script metas — matches the repo's existing convention (e.g. `ColorExtensions.cs.meta`); Unity fills defaults, GUIDs verified unique.
- **`int` → `ulong` behavior change (nit):** benign — the element name is write-only (never parsed back); the test's `#if` encodes the type difference.
- **Docs shipping to npm:** `PLAN.md` + `progress/` live at the package root and will ship in the npm tarball. Kept tracked per the working-style requirement; harmless for consumers (imported as TextAssets, not compiled). Not adding an `.npmignore` to avoid scope creep.

### GREEN — final results (public helper), clean CLI EditMode runs (delete `Library/ScriptAssemblies` + `Library/Bee`, then `-runTests`)

- **6000.5.2f1** — `EntityId` branch (`UNITY_6000_4_OR_NEWER`): 0 compile errors, `GetObjectIdStringIsNonEmptyStableAndUnique` **Passed** (1/1). CS0619 is gone — issue #12 fixed.
- **6000.4.6f1** — `EntityId` branch: 0 compile errors, **Passed** (1/1).
- **2022.3.62f2** — legacy `GetInstanceID` branch: 0 compile errors, **Passed** (1/1). Nearest installed editor to the `"unity": "2021.3"` floor.

MCP note: the live-editor MCP `RunCommand` (execute) capability was **revoked mid-session** ("Connection revoked… Project Settings > AI > Unity MCP"); read-only MCP calls still worked. 6000.4.6f1 coverage was therefore obtained via a clean CLI throwaway project on the same editor binary (identical compiler/runtime) rather than the live editor. The user's live editor will clear its stale CS1061 on its next recompile, since the on-disk source is now correct.

## PR

[wallstop/DataVisualizer#13](https://github.com/wallstop/DataVisualizer/pull/13) — addressed one Copilot review comment (synced the "Decisions" section above to the final `public` choice); Cursor Bugbot passed ("Low Risk").
