# Performance

DxVisualizer fills the object list and search index gradually. You can browse available rows while loading continues. Search can return available matches during indexing; its
results may update as more assets become available. See [Search and filtering](search-and-filter.md).

## What affects responsiveness

The browser keeps copied search text and asset labels in memory for reuse when the window rebuilds. Building this data still reads tracked assets. The selected type's list keeps
its loaded objects, so a large type can still use substantial memory.

Large projects take longer to index. Serialized field size, custom inspectors, Unity imports, storage and other editor work also affect responsiveness. Asset creation and import
can block the editor independently of DxVisualizer. A cooperative processing budget cannot interrupt an individual Unity API call or garbage collection.

Routine asset changes update affected rows. Import callbacks capture changed paths for later processing, so the queue can continue after the callback returns. Very large bursts
still require array copies and processing time. Queue counts can include duplicate paths until coalescing finishes.

Selection and custom ordering are preserved as rows arrive. If an asset is temporarily unavailable during compilation or import, allow the editor to settle before changing saved
arrangements. See [Troubleshooting](troubleshooting.md).

## Recorded performance

A run on 14 October 2026 at [ec085b1](https://github.com/Ambiguous-Interactive/DxVisualizer/commit/ec085b1596775eac6be5071b23f5da9870097973), using Unity 6000.5.2f1, Windows 11,
Snapdragon X Elite and Adreno X1-85, measured these times. Each warm value is the median of five observations. The editor was already running; these are not fresh-process startup
measurements. The results describe that source revision and host, not a guarantee for the current version or your project.

| Assets | Warm window open to initial search cache (ms) | Search first results (ms) | Selection (ms) |
| ------ | --------------------------------------------- | ------------------------- | -------------- |
| 100    | 580.0                                         | 71.3                      | 41.1           |
| 1,000  | 1,390.7                                       | 192.2                     | 39.5           |
| 10,000 | 7,771.4                                       | 368.2                     | 20.7           |

The search list is capped at 25 results. Initial cache readiness does not mean all shared indexing or deferred refresh work has finished. Fixture creation and import are excluded
from these timings. Five observations do not establish p95 latency, worst-case behavior or a cross-platform performance guarantee.

## Scale and support limits

The browser supports exact ScriptableObject main assets. Subassets and prefab components are not supported sources. Synthetic metadata experiments do not establish support for a
million real Unity assets. Large-project performance, Play Mode entry overhead and compile timings still need broader measurement. Unity 2021.3 is the package floor; these
observations were executed on Unity 6000.5.2f1 only.

For loading behavior and recovery steps, see [Troubleshooting](troubleshooting.md). Report reproducible slowdowns in the
[issue tracker](https://github.com/Ambiguous-Interactive/DxVisualizer/issues), including Unity version, asset count, affected action and whether indexing or import was active.
