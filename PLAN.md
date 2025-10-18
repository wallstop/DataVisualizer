# Drag & Drop Ghost Alignment Plan

## Current Progress
- [x] Restored in-place ghost sizing to mirror the dragged row and added detailed diagnostics (margins/height logging).
- [x] Eliminated double-spacing when dropping at index 0 by normalizing container padding and ghost bottom margin.
- [x] Added temporary suppression of adjacent-row margins to remove earlier spacing flicker (since reverted to simplify logic).
- [x] Cached layout height/margins to avoid `NaN` ghost dimensions.
- [x] Simplified ghost spacing to rely on cached row margins (top via container padding, bottom via ghost) so index-0 hover avoids double gaps without mutating sibling margins.
- [x] Reworked ghost margin application to mirror reference row spacing (top/left/right/bottom) instead of zeroing values, preserving consistent gaps across the list.
- [x] Deferred reorder debug EditorPrefs lookup until OnEnable to stop constructor-time Unity exceptions during window creation.

## Outstanding Issues
- [ ] Validate the latest margin suppression tweaks in-editor to confirm index-0 hover is stable and spacing matches expectations.
- [ ] Capture before/after screenshots of drag spacing once verified to document behaviour changes.
- [ ] `Plan.md` previously tracked broader roadmap tasks—need to reinstate summary once ghost work stabilizes.

## Next Steps
- [x] Reintroduce minimal adjacent-row margin suppression with deterministic restore to eliminate index-0 jitter.
- [x] Apply consistent baseline spacing for non-zero indices by blending reference element top margin into ghost while preserving preceding row bottom margin.
- [ ] After visual fidelity validates, reconstruct the full roadmap in PLAN.md (pull from git history) so plan reflects overall project status.
