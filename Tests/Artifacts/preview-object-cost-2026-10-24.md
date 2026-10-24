# Quick-preview object cost observations

This evidence advances #306, #309 and #310. Their broader acceptance remains open.

## Source and method

The [emitted report](performance-preview-object-cost-1k-10k-2026-10-24.json) is retained without changing its bytes. Measured source
[add794a](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/add794a66da76ce9f81ec508750e2c00c1da7351) includes metadata-only quick previews and separate
result-load/reference counters. The later evidence commit changes no C# source. [Preflight and postflight](preview-object-cost-2026-10-24-provenance.json) verify compiler
checksums, loaded modules and assembly bytes for the driver, browser and namespace controller. All 205 package C# files under Editor/Runtime/Tests match the host before and after
the run. The sorted path/NUL/file-SHA-256/newline manifest SHA-256 is `52cd93e73ca133d33a860e2f13672a36fa1d1145f8d495fc4bed5fa41aad7b28`.

Unity 6000.5.2f1 Windows 11 runs on a Snapdragon X Elite host with 12 logical processors and 65,011 MiB reported system memory. This is one sequential warm editor session, not a
controlled before/after comparison or fresh-process experiment. The declared watchdog is 30 minutes; staging uses a 64 ms/100-item profile. Both 1,000 and 10,000 uniquely owned
real-asset datasets complete. Each splits evenly between ordinary `OtherEditorOnlyCreationData` ScriptableObjects and `PlayModeDataObject` BaseDataObjects.

Each dataset contains 280 query observations, 40 ordinary readiness observations and 105 interactions. Every query condition/mode has five warmups and 30 measured samples;
readiness has five cold-window observations in the same process, five warmups and 30 warm samples. Every measured scroll moves. The controlled partial warm rebuild pauses
extraction after selected rows seed it: actual available counts are 500/5,000, rather than the requested 10% threshold. Complete indexes include 14 pre-existing managed records.
Rare GUIDs are absent from partial input; common queries alternate zero/25 partial matches. Partial and full result populations therefore differ.

## Preview costs and latency

`resultObjectLoads` counts cache resolution loads since input, excluding index extraction. `previewAssetReferences` counts direct ScriptableObject wrapper references in quick-row
`userData` after matching publication paint, including destroyed wrappers. It excludes closures, selected-type rows and native memory. A live/destroyed-wrapper positive control
passes. Zero references and loads do not establish zero allocation or bounded selected-row retention. Missing observations retain `-1`.

All 560 observations, including 80 warmups, report zero result-resolution loads and zero direct preview asset references. p95 uses nearest rank, sample 29 of 30. Publication
includes scripted field input and editor observation delay. Paint is a later visible-popover `generateVisualContent` callback for the same current query generation, not GPU
presentation or physical input latency. Both clocks remain separate.

| Fixtures | Mode           | Query       | Available records | Publication p95 (ms) | Paint-generation p95 (ms) |
| -------- | -------------- | ----------- | ----------------- | -------------------- | ------------------------- |
| 1,000    | complete       | common-name | 1,014             | 38.489               | 39.564                    |
| 1,000    | complete       | no-match    | 1,014             | 21.688               | 24.668                    |
| 1,000    | complete       | rare-field  | 1,014             | 25.963               | 31.863                    |
| 1,000    | complete       | rare-guid   | 1,014             | 22.794               | 25.240                    |
| 1,000    | paused-partial | common-name | 500               | 51.622               | 52.789                    |
| 1,000    | paused-partial | no-match    | 500               | 22.148               | 26.047                    |
| 1,000    | paused-partial | rare-field  | 500               | 30.749               | 37.884                    |
| 1,000    | paused-partial | rare-guid   | 500               | 20.943               | 25.052                    |
| 10,000   | complete       | common-name | 10,014            | 68.147               | 86.460                    |
| 10,000   | complete       | no-match    | 10,014            | 31.033               | 35.654                    |
| 10,000   | complete       | rare-field  | 10,014            | 56.090               | 62.269                    |
| 10,000   | complete       | rare-guid   | 10,014            | 54.481               | 60.903                    |
| 10,000   | paused-partial | common-name | 5,000             | 50.476               | 51.863                    |
| 10,000   | paused-partial | no-match    | 5,000             | 25.793               | 29.507                    |
| 10,000   | paused-partial | rare-field  | 5,000             | 32.493               | 35.238                    |
| 10,000   | paused-partial | rare-guid   | 5,000             | 25.626               | 28.888                    |

These paused/complete search p95 values are below 100 ms in this observation. They do not establish the live-indexing target or a controlled speedup.

| Fixtures | Warm usable/inspector p95 (ms) | Full index p95 (ms) | Loaded selected rows | Maximum realized rows | Staging (ms) |
| -------- | ------------------------------ | ------------------- | -------------------- | --------------------- | ------------ |
| 1,000    | 532.898                        | 2430.474            | 500                  | 18                    | 19837.934    |
| 10,000   | 918.671                        | 11888.424           | 5,000                | 18                    | 249432.596   |

Warm opening still misses 100 ms. The global search cache retains zero assets, but selected-type retention grows from 500 to 5,000 objects. This does not satisfy #309. The maximum
reported window query slice is 1.9979/4.1471 ms at 1k/10k; query overrun totals are 0/472 including warmups. Readiness records report maximum index slices of 59.9254/37.3206 ms.
There is no hard 4 ms processing guarantee. At 10k the longest driver tick is 8,726.241 ms and the longest observed editor-update gap is 1,476.035 ms; these have different observer
boundaries and do not establish or attribute the longest whole-editor stall.

## Validation and cleanup

The measured C# tree passes 862 EditMode and 15 actual PlayMode cases, including metadata preview identity, moved/deleted activation, cold/repeated documentation captures and
counter controls. Three new preview regressions fail against the earlier object-backed renderer and pass with metadata rows. The full local gate and exact 261-file npm archive
audit pass; tests and artifacts are excluded from the package.

Independent preflight/postflight checks match: 72 `.asset` paths, seven editor windows, zero browser windows, one native host, zero shared metadata/pending paths, no owned fixture
roots or window-state files, and no Play/compile/import/suspension state. The environment field `preExistingScriptableObjectAssets=228` counts GUID discovery candidates; it is not
the same counter as the independent 72 `.asset` paths. Source, compiler and module evidence matches before and after measurement.

Unmeasured scope remains in #306/#309/#310: 50k/100k and many-type distributions, representative live indexing and cancellation, real change-operation matrices, matched Play-entry
and compile controls, fresh processes, native memory and wider platform evidence. No release or support expansion follows from these observations.
