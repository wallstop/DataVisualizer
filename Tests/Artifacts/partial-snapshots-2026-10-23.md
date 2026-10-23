# Partial search snapshot preparation

Measured on 23 October 2026 at [9e874a2](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/9e874a2). All 205 tracked C# files matched the Unity host checkout before
validation. Explicit recompilation completed before the final driver ran. The emitted schema, partial-view labels and source hash agree with the loaded driver version.

The exact [schema-5 report](performance-partial-snapshots-2026-10-23.json) has SHA-256 `a4e8a7175d33664e6f9d637e06e9cb8e183f281d2d8eb0b75b03e762516c4cb4`. Driver source SHA-256 is
`41219b04363cc771fd54f27e991e5bd4258d62d4db23d6994637eaed74a85fbb`.

The Windows 11 Unity 6000.5.2f1 host uses a Snapdragon X Elite, Adreno X1-85, 65,011 MB reported RAM, one display and 96 DPI. The clean opt-in fixture passes both cases and all 18
conditions in 396.57 seconds. Each condition has five warmups and 30 measured queries; the existing 30-minute NUnit limit and 2 GiB construction-heap guard remain. No Unity assets
or native asset loads are created by this experiment.

## What changed

Cache queries share immutable available records, including unsorted partial views. Their first merge reads those records directly; ordering buffers are allocated during Advance
rather than construction. Final cache ordering uses the same ownership. Later edits detach the cache list, preserving earlier query records. Public constructors still copy mutable
caller inputs.

The regression first failed with 5,210,112 retained bytes for 32 unadvanced unsorted queries at 10k. Both sorted and partial cases now pass a 128 KiB bound. Mutation cases cover
remove, replace, add, clear and synchronous/cooperative sorting before queries finish.

## Construction and retained heap

Each partial view holds 10% of its dataset and remains unsorted. Both paths use the production cache query factory. There are six ownership comparisons: one partial and one full
view at each dataset size. Each comparison retains 32 copied controls while adding 32 shared queries, then clears both batches. Controls use the same sorting choice.

Heap deltas follow forced collections and measure the whole process. Zero or negative values mean no net increase at that resolution, not zero allocation. Construction p95 ranges
span the three query terms; p95 uses nearest rank over 30 samples.

| Dataset   | Available | View    | Copied controls (MiB) | Added shared queries (MiB) | Construction p95 range (ms) |
| --------- | --------- | ------- | --------------------- | -------------------------- | --------------------------- |
| 10,000    | 1,000     | Partial | 0.2461                | 0.0000                     | 0.0350–0.0841               |
| 10,000    | 10,000    | Full    | 2.5039                | 0.0039                     | 0.0144–0.0436               |
| 100,000   | 10,000    | Partial | 2.5039                | -0.0039                    | 0.0480–0.0832               |
| 100,000   | 100,000   | Full    | 24.5273               | -0.0039                    | 0.0106–0.0404               |
| 1,000,000 | 100,000   | Partial | 24.5117               | 0.0000                     | 0.0341–0.0517               |
| 1,000,000 | 1,000,000 | Full    | 244.2305              | 0.0000                     | 0.0164–0.0559               |

Advancing partial queries still allocates ordering arrays. These measurements do not bound memory for many actively sorting queries. Query matching and final ordering still take
time: full 1m absent/rare active p95 is 1,729.40/1,736.42 ms; wall p95 is 1,963.12/1,969.18 ms. The largest query slice is 8.2595 ms and the largest final-sort call is 11.1475 ms.
Allocation, comparisons and GC remain indivisible; 4 ms is a cooperative target.

## Cost shifted to the first edit

These are single warm observations, not distributions. The timed unit includes UseMetadata and its assertion; replacement-record construction is outside the timer. Heap deltas
include replacement records and the still-live older query. Another edit before a new shared query reuses the detached list.

| Available | View    | First edit (ms) | Second edit (ms) | Added retained heap (bytes) |
| --------- | ------- | --------------- | ---------------- | --------------------------- |
| 1,000     | Partial | 0.1753          | 0.0083           | 8,192                       |
| 10,000    | Full    | 0.0693          | 0.0062           | 81,920                      |
| 10,000    | Partial | 0.0582          | 0.0093           | 81,920                      |
| 100,000   | Full    | 0.1225          | 0.0081           | 802,816                     |
| 100,000   | Partial | 0.1324          | 0.0080           | 794,624                     |
| 1,000,000 | Full    | 0.8169          | 0.0088           | 8,011,776                   |

The full/partial views and tracker retain 1,505,087,488 bytes at 1m. Schema 5 adds a separate 10% partial view, so this footprint differs from earlier single-view reports.
Construction units reach 155.034 ms; a forced collection reaches 1,181.3133 ms. These are managed component observations, not whole-editor stall bounds or native-memory slopes.

## Validation and limits

The same C# tree passes 829 EditMode and 15 PlayMode cases. Two earlier local attempts are excluded from the passing evidence: one executed an older measurement assembly while
hashing newer source (#410), and one completed its observations but failed NUnit because an interleaved eval diagnostic emitted a Pipeline timeout error. The final passing
experiment used no interleaved evals.

This is one warm editor process, without controlled fresh-process comparisons. Synthetic partial ordering excludes input, debounce, rendering, real extraction and true
input-to-cancellation. The raw abandoned-query fields measure one explicit component step and lack of later advancement, not user cancellation latency. Native/project costs,
selected-object retention, persistent-cache benefit/recovery, rendered partial/full latency and other platforms remain in #309, #310 and #323. No million-asset support or hard
frame-time guarantee is established.
