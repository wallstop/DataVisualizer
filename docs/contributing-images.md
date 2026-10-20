# Contributing documentation images

This page is for contributors who regenerate repository imagery. For window behavior and recovery steps, use [Troubleshooting](troubleshooting.md).

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

## Review images before publication

Review the controls and representative inspector values, not only dimensions. Check that text fits, labels are readable, selected assets are visible, captions and alt text match,
and no private project data appears. Capture actual production UI; do not substitute mockups.

Record the captured source revision, Unity version, OS, theme, editor skin, pixels per point, screen DPI, display count, and review scope in the capture inventory. Changes to
controls, pane allocation, theme tokens, or fixtures make affected images stale: inspect and recapture them before publishing the change. Keep other platform/DPI evidence with
[issue #270](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/270).

The driver uses a unique `Assets/DataVisualizerDocsCapture-*` folder and an ownership marker for each run. Cleanup removes that owned folder, including on setup failure. It leaves
pre-existing folders and replaced folders untouched. Do not use an existing project folder as capture scratch space.

## Working from a container

When the package checkout is edited inside a container while Unity runs on the host, the host picks up container-side edits through the mounted folder and recompiles on focus. Do
not edit files while a test suite or capture run is in progress on the host: the host watcher racing a mid-run edit can fail the run with unrelated errors. Let the host finish,
recompile, and report up to date before the next edit, then re-run.

### Saved searches and validation

`DocsImageCapture.CaptureAssetTools(outputDirectory, width, height)` captures both themed workspaces inside a real DxVisualizer window. It creates two owned assets, waits for the
saved query to finish, captures duplicate-ID findings, and restores saved searches and selection. Use 960 × 680 for published images and 640 × 600 to inspect compact layout. Copy
`saved-searches.png` and `asset-validation.png` from the output directory into `docs/images/`, keeping their existing metadata GUIDs.
