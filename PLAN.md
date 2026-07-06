# Plan

Active work items. Completed/obsolete items are removed after each session; history lives in `progress/`.

## Active

- [ ] PR [#13](https://github.com/wallstop/DataVisualizer/pull/13) (branch `dev/wallstop/bug-fixes`) — Unity 6000.x compile-cleanliness. Open and green; awaiting merge (merge to `main` publishes 0.0.35 to npm).
  - Issue #12 — CS0619: `Object.GetInstanceID()` → `EntityId` behind `UNITY_6000_4_OR_NEWER`; first EditMode tests; version bump to 0.0.35. Tracking: `progress/session-001-issue-12-entityid-fix.md`.
  - CS0618 deprecations — remove dead `UxmlTraits`/`UxmlFactory` from `HorizontalToggle`; gate `EventBase.PreventDefault()` behind `UNITY_2023_2_OR_NEWER` via `EventExtensions.PreventDefaultCompat`. Tracking: `progress/session-002-uxml-preventdefault-deprecations.md`.
