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

The window enforces a minimum of 860x480 pixels, and each pane enforces its own minimum width: 320 px for namespaces, 220 px for the object list, and 260 px for the inspector.
Layout below a pane minimum is refused rather than persisted, so shrinking and growing the window back does not corrupt your saved widths.

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

The six images in `docs/images/` are generated from the live window, not hand-taken. The capture driver lives at `Tests/Editor/DocsImageCapture.cs` and arranges a known window
state before each shot.

Regenerate all shots with a batch-mode Unity invocation:

```sh
unity -projectPath <project> -batchmode -quit -executeMethod \
    WallstopStudios.DataVisualizer.Tests.Editor.DocsImageCapture.RunFromCommandLine \
    -docsImageOutputDir <path>
```

- The output directory also comes from the `DATAVISUALIZER_DOCS_IMAGE_DIR` environment variable; without either, shots land in the project's `Temp/DocsImageCaptures`. The driver
  never writes into `docs/` by default; review results before committing them.
- Relative output paths resolve against the Unity **project root**, not the package folder. To stage shots under the package, pass an absolute path to a dot-folder such as
  `.docs-captures/` — Unity generates no `.meta` files for dot-folders, so the staging area stays out of the importer.
- Each capture is validated against the laid-out window: regions Unity's offscreen renderer should have painted are measured in the PNG, and a capture that omits them fails closed
  instead of saving a misleading image.

After reviewing the captured PNGs, copy them into `docs/images/` and export the smaller copies used by the README and site:

```sh
uv run scripts/export_docs_webp.py
```

The [capture inventory](images/capture-manifest.json) requires layout, instance actions, create, import, search, and settings. It records the reviewed pixel dimensions at
pixelsPerPoint 1. Review fresh captures before updating dimensions; pass `--manifest <path>` to validate against a separately reviewed inventory.

The exporter rejects missing or unexpected shots, wrong dimensions, uniform blank captures, and duplicate encoded images before writing. It checks that every decoded WebP pixel
matches its PNG and that every copy is smaller. It stages the complete changed set before replacing outputs and keeps unchanged files intact. If publication fails, it restores
previous files and removes new outputs. If restoration cannot finish, the error names the retained `.docs-webp-*` recovery directory and affected files; restore the listed backups
from its `previous/` folder and remove listed new outputs before retrying. Keep the reviewed PNGs in `docs/images/` as capture sources. Run
`uv run scripts/export_docs_webp.py --check` to verify the committed copies. Pillow is pinned by the export script and installed only for that local command; the site build uses
the committed images.

Known capture limit, recorded on [#114](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/114): Unity's offscreen panel renderer on macOS 6000.4 omitted several
inspector and list subtrees (the same window paints them fully on screen and on Windows 6000.5). The driver detects that host gap and fails closed rather than publishing blanked
imagery; every committed PNG was produced and pixel-verified on Windows 6000.5.

## Regenerate the creation-controls animation

The animation uses two full-window frames, before and after opening Create. Run the same capture command above with
`-executeMethod WallstopStudios.DataVisualizer.Tests.Editor.DocsImageCapture.CaptureCreationFramesFromCommandLine` and a staging directory such as `.docs-animation/`. Review both
`data-visualizer-layout-frame.png` and `data-visualizer-create-frame.png`, then encode them from the repository root:

```bash
uv run scripts/export_docs_gif.py --frames .docs-animation
uv run scripts/export_docs_gif.py --frames .docs-animation --check
```

Commit the GIF and its `.meta` file. Keep the PNG frames in the staging directory; they are full-color capture sources, whereas GIF uses a shared 256-color palette. The exporter
checks both frame dimensions, decoded quantized pixels, loop, and 1.2/1.8-second timing before replacing the published file. The codec is a pinned local dependency; CI does not
install it. `--check` verifies the reviewed captures against the published GIF without writing. Unchanged exports preserve the file timestamp.

## Working from a container

When the package checkout is edited inside a container while Unity runs on the host, the host picks up container-side edits through the mounted folder and recompiles on focus. Do
not edit files while a test suite or capture run is in progress on the host: the host watcher racing a mid-run edit can fail the run with unrelated errors. Let the host finish,
recompile, and report up to date before the next edit, then re-run.

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
