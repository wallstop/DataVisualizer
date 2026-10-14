# Measured performance

This page reports what the editor window actually costs on a recorded host, produced by a reproducible measurement driver instead of impressions. The numbers below are the first
published baseline: one host, one session, with every limitation stated. The measurement harness (roadmap area T01) keeps extending the scenario set, and this page grows with it.

## How the numbers are produced

The driver lives at `Tests/Editor/PerfBaseline.cs`. It stages deterministic `PlayModeDataObject` fixture datasets (100 and 1,000 assets in this baseline), then measures inside the
editor with monotonic `Stopwatch` timers, so no tool transport ever pollutes a sample:

- **Fixture import** is the wall time of the single `AssetDatabase.Refresh` that imports the staged files.
- **Window open to index ready** runs from `CreateGUI` until the production search cache reports populated — the same flag the window's asynchronous batch loader flips for a user.
  _Cold_ is the first open after staging; _warm_ numbers come from repeated open-close cycles of the same already-imported dataset, after two discarded warm-ups.
- **Search first results** types a query into the production search field and measures until the results list carries a row, including any deferral the search performs. The results
  list displays at most 25 rows, so match counts saturate at the cap.
- **Selection** measures the production `SelectObject` call for a mid-list fixture asset.

The run writes one JSON report (schema 1) recording the environment block, every raw sample, and the repetition counts, so two runs compare without reinterpreting prose. A
25-minute watchdog fails the run closed, removing its fixtures and windows on every exit path.

## Recorded environment

| Property                                            | Value                                                |
| --------------------------------------------------- | ---------------------------------------------------- |
| Unity                                               | 6000.5.2f1 (Windows Editor)                          |
| OS                                                  | Windows 11 (10.0.26200)                              |
| CPU                                                 | Snapdragon X Elite X1E80100, 12 cores                |
| Memory                                              | ~65 GB                                               |
| GPU                                                 | Qualcomm Adreno X1-85                                |
| Display                                             | one, 96 DPI, editor pixels-per-point 1.0, dark theme |
| Pre-existing ScriptableObject assets in the project | 214                                                  |

## Results

Every row is milliseconds; warm and sampled scenarios report min / median / max over five samples.

| Scenario                               | 100 assets            | 1,000 assets             |
| -------------------------------------- | --------------------- | ------------------------ |
| Fixture import (single sample)         | 40.3                  | 40.7                     |
| Window open to index ready, cold       | 2478.3                | 2050.1                   |
| Window open to index ready, warm       | 483.1 / 511.8 / 581.8 | 1043.7 / 1251.5 / 1715.8 |
| Search first results (13 / 25 matches) | 54.6 / 58.3 / 235.1   | 79.4 / 92.0 / 107.0      |
| Selection                              | 32.4 / 33.1 / 45.2    | 20.3 / 23.0 / 25.2       |

Reading the numbers:

- The **cold** open includes one-time session costs (type catalog construction, first asset resolution), so it is not the number a daily user sees after the first open; the warm
  median is the steady-state open cost. The cold single samples also vary with session history, which is why they are recorded as single samples and not medians.
- **Search first results** stays well under the acceptance target of 100 ms including debounce at both sizes; the visible result cap keeps row building bounded.
- The **warm** open grows roughly with dataset size at this scale, and the async batch loader keeps the window responsive while it fills; see
  [The object list fills in gradually](troubleshooting.md#the-object-list-fills-in-gradually) for what that looks like in practice.

## Current gaps

Recorded explicitly rather than smoothed over:

- A 10,000-asset dataset is not yet published: the driver stages and measures it, but a clean run has not been recorded on this host yet, because the package's asset postprocessor
  currently loads every imported asset synchronously during a large import burst, which dominates that scenario and is tracked in the repository issue tracker (#226).
- Play Mode entry overhead and compile timings are separate roadmap areas (T03, T08) and are not measured here yet.
- Cold scenarios are single samples; the full acceptance wants 30+ repetitions per warm scenario and cross-host spread (T01, T12).

Reproduce: run `WallstopStudios.DataVisualizer.Tests.Editor.PerfBaseline.RunFromCommandLine` in an interactive editor (directly or through the Unity MCP bridge), then read
`Temp/PerfBaseline/perf-baseline.json`. The output directory can be redirected with `-perfBaselineOutputDir` or the `DATAVISUALIZER_PERF_BASELINE_DIR` environment variable.
