# Troubleshooting

This page lists behaviors that can look like a bug but are the window working as designed, and what to do when something genuinely is wrong. Each section links to the page that
documents the feature in full.

## The object list fills in gradually

Selecting a type loads its instances asynchronously, so the list can still be filling while you work. The header row shows an italic `Loading... (n/total)` counter until the load
finishes.

- Assets whose script is missing, or whose type does not exactly match the selected type, are skipped and do not count toward the total, so the counter reaches its end instead of
  stalling.
- Running a processor while the selected type is still loading is refused with a **Loading In Progress** dialog, because it would touch only the subset that has arrived. Wait for
  the counter to disappear. See [Refusal while loading](managing-assets.md#refusal-while-loading).
- A list that never finishes loading usually means the console holds script compile errors. Fix those first; the window reads only assets Unity can resolve.

## Search says "Building search index…"

The first search after the window opens may show **Building search index…** while the background index loads. The popover stays open with your query, and the search re-runs
automatically when the index is ready — no action needed. See [Global search](search-and-filter.md#global-search).

## The window does not update after an asset change

While Unity reports the AssetDatabase busy — a script compile or a running import — the window defers work that would force asset type resolution. The deferred work happens once
the database idles; nothing is lost.

The window refreshes itself when a tracked `.asset` file is imported, moved, or deleted, and a theme change to the applied theme applies immediately. If a view looks stale after
such a change, check the console for errors from other packages first: an exception in another import handler can stop Unity's broadcast chain.

While the editor is playing, package-driven work pauses entirely and refresh requests are remembered until you leave Play Mode. See
[While the editor is playing](organizing.md#while-the-editor-is-playing).

## An asset is missing from the list

- The object list shows only assets whose exact type matches the selected row. An asset of a subclass appears when that subclass is selected, not under the base type.
- An asset whose script no longer compiles or whose script GUID is broken resolves to no type at all and is skipped. Unity flags such assets as _Missing (Mono Script)_ in the
  Inspector; fixing the script restores the asset in the list.
- A type whose assembly failed to compile disappears from the catalog until the code compiles again. Tracked types and your ordering survive; nothing on disk changes.
- Removing a type from the catalog never deletes assets. See [Removing a type from the catalog](extending.md#removing-a-type-from-the-catalog).

## The selection resets itself

The saved selection for a type is verified against the AssetDatabase. Right after a recompile or import, verification would be unreliable and could log Unity's missing-script
warning for every unresolved asset, so the window waits until the database idles and then keeps selections that are still valid and clears only genuinely invalid ones — typically
an asset whose script is gone. If your selection drops after a recompile, the underlying asset usually failed to resolve; check the console.

## The window will not shrink below a certain size

The window enforces a minimum of 360x480 points. Below 912 points it shows one pane at a time; use **Types**, **Objects**, and **Inspector** to move between panes. At wider sizes,
**Hide types**, **Hide list**, and **Inspector** can free space for editing. Preferred splitter widths survive these temporary layouts.

When the operating system cannot honor the minimum — a small monitor or a tight side-docked placement — the window clamps to the available space and treats that as temporary: your
preferred size is kept separately and restored when the space returns. See [Pane widths](organizing.md#pane-widths).

## The arrangement did not load

State lives in one of two places, chosen by the **Persist State in Settings Asset** toggle:

- User state: `DataVisualizerUserState.json` in Unity's per-user persistent data path. Each developer has a private copy.
- Project asset: a `DataVisualizerSettings` asset, created at `Assets/Editor/DataVisualizerSettings.asset` on first use if none exists.

If the stored state is empty or corrupt, the window logs a warning or error, starts with default state, and keeps going — it never half-applies broken state. To recover your
arrangement, restore the file or asset from backup or let the window rebuild it; switching between the two stores copies the current state across. See
[Persistence](organizing.md#persistence).

The pane widths and the Data Folder are not part of that state; they stay with the settings asset or editor preferences as described on the [Organizing](organizing.md) page.

## Recreating the documentation images

Contributor capture and export instructions moved to [Contributing documentation images](contributing-images.md#recreating-the-documentation-images).

## Regenerate the creation-controls animation

Use the [contributor animation workflow](contributing-images.md#regenerate-the-creation-controls-animation).

## Working from a container

See [host and container capture work](contributing-images.md#working-from-a-container). For package installation, start with [Getting started](getting-started.md).

## Validate your Unity version

The package declares Unity 2021.3 as its minimum. The current local host is Windows Unity 6000.5.2f1; that evidence does not prove other editor versions or operating systems.

In a disposable project on your target editor version, install the package, check the Console for compile errors, then add a type, create an asset, edit it, search, filter, clone,
and reopen the window. Check saved selection and order, and enter and exit Play Mode. Contributors should enable the package's tests and run its EditMode and PlayMode suites.
Record the package revision, editor version, OS, theme, and display scale with failures.

The remaining version matrix belongs to
[the validation roadmap](https://github.com/Ambiguous-Interactive/DxVisualizer/blob/main/.llm/references/roadmap.md#t12---acceptance-matrix-and-release-evidence-partial).
[Issue #270](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/270) owns the remaining physical display, DPI, and platform smoke evidence. A passing host run is not a
claim that those cells passed.

## Still stuck

- [Getting started](getting-started.md) — the first-session walkthrough.
- [Search and filtering](search-and-filter.md) — what each finder matches.
- [Managing assets](managing-assets.md) — asset operations and processors.
- [Organizing your data](organizing.md) — layout, themes, persistence.
- [Issues](https://github.com/Ambiguous-Interactive/DxVisualizer/issues) — known problems and where to report new ones. DxVisualizer is alpha software; a report with the console
  output and Unity version helps a lot.
