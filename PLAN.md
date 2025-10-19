# Drag & Drop Ghost Alignment Plan

## Current Progress
- [x] Restored in-place ghost sizing to mirror the dragged row and added detailed diagnostics (margins/height logging).
- [x] Eliminated double-spacing when dropping at index 0 by normalizing container padding and ghost bottom margin.
- [x] Added temporary suppression of adjacent-row margins to remove earlier spacing flicker (since reverted to simplify logic).
- [x] Cached layout height/margins to avoid `NaN` ghost dimensions.
- [x] Simplified ghost spacing so interior placements zero-out the ghost margins while index-0 insertions reuse cached spacing, removing the doubled gaps and jitter around the list head.
- [x] Reworked ghost margin application to mirror reference row spacing (top/left/right/bottom) instead of zeroing values, preserving consistent gaps across the list.
- [x] Tuned the drop-index adjustment so same-list reorders land in the intended slot without cancelling the move.
- [x] Rebound the object command dispatcher whenever the event hub changes so reorder events always reach the command handlers after domain reloads.
- [x] Zeroed ghost margins for interior placements while using container padding at index 0, eliminating the extra gap while dragging past the first row.
- [x] Deferred reorder debug EditorPrefs lookup until OnEnable to stop constructor-time Unity exceptions during window creation.

## Outstanding Issues
- [ ] Validate the latest margin adjustments in-editor to confirm index-0 hover is stable and no new gaps linger after a drop.
- [ ] Capture before/after screenshots of drag spacing once verified to document behaviour changes.
- [ ] `Plan.md` previously tracked broader roadmap tasks—need to reinstate summary once ghost work stabilizes.
- [ ] Verify the object list visually reorders after drops now that the target index fix is in place.
- [ ] Capture before/after screenshots of drag spacing once verified to document behaviour changes.
- [ ] `Plan.md` previously tracked broader roadmap tasks—need to reinstate summary once ghost work stabilizes.

## Next Steps
- [x] Reintroduce minimal adjacent-row margin suppression with deterministic restore to eliminate index-0 jitter.
- [x] Apply consistent baseline spacing for non-zero indices by blending reference element top margin into ghost while preserving preceding row bottom margin.
- [ ] After visual fidelity validates, reconstruct the full roadmap in PLAN.md (pull from git history) so plan reflects overall project status.
