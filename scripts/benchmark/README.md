# Local benchmark driver

`benchmark_driver.py` runs a disposable benchmark against a real Unity host.
It generates the fixture in the host project under `Assets/`, validates the
asset count and content before timing, emits raw Unity-side samples as JSON,
and removes the fixture and generated bootstrap by default. Nothing generated
by the driver belongs in the UPM/npm package.

The driver supports two execution modes:

```bash
# Direct host Unity execution
python3 scripts/benchmark/benchmark_driver.py \
  --mode direct \
  --host-project /path/to/DataVisualizerHost \
  --unity-version 6000.4.6f1 \
  --fixture-size 10000 \
  --suite all \
  --output-dir /tmp/data-visualizer-results

# Existing official Unity MCP bridge
python3 scripts/benchmark/benchmark_driver.py \
  --mode mcp \
  --host-project /Users/wallstop/Code/DataVisualizer \
  --unity-version 6000.4.6f1 \
  --fixture-size 100 \
  --suite play-entry \
  --output-dir /tmp/data-visualizer-results
```

`--host-project` is the path visible to the Unity process. When the driver and
host use different path namespaces, pass `--path-map
LOCAL_PREFIX=HOST_PREFIX`; the mapping is validated before execution. MCP mode
uses the existing `UNITY_MCP_URL`/`UNITY_MCP_TOKEN` configuration and never
assumes that the host path is visible inside the container.

Supported fixture sizes are 100, 1,000, 10,000, and 50,000. Fixtures contain
ordinary ScriptableObjects, `BaseDataObject` assets, nested lists, shared
references, labels, deterministic ordering, and two distinct `Data` types with
the same short name. A report records the package revision, requested and
actual Unity versions, OS/CPU/memory/storage-adjacent editor metadata,
graphics/DPI/theme/window information, reload settings, fixture timings, raw
Play-entry samples, warm-up counts, medians, and p95 values.

The Unity-side measurements use `System.Diagnostics.Stopwatch` and named
`ProfilerMarker`s. MCP requests and Python polling are outside the measured
Play-entry interval. Play-entry runs use five warm-ups and 30 retained samples
for each of three matched alternating cases: the window open and idle, the
window open while object/search indexing is in flight, and the window closed.
The legacy `playEntryOpen` JSON field aliases the open/idle result; the
independent `playEntryOpenIdle` and `playEntryOpenIndexing` fields are
authoritative.

The current driver intentionally reports unsupported scenarios in
`unavailableMetrics` instead of treating them as passes. In particular,
package-absent controls, cold/warm restart measurements, indexed-search
latency, retained-reference memory, and the full compatibility/player matrix
still require the work tracked by issue #21. A report's
`playEntryTargetMet` must be true before the 20 ms acceptance target can be
considered satisfied; measured misses remain misses.

## Play Mode suspension scenario

Use `playmode_suspension_driver.py` for the lifecycle matrix tracked by issue
#20:

```bash
python3 scripts/benchmark/playmode_suspension_driver.py \
  --host-project /Users/wallstop/Code/DataVisualizer \
  --unity-version 6000.4.6f1 \
  --output /tmp/data-visualizer-playmode-suspension.json
```

It runs two in-flight open-window cycles and one first-enable-during-Play cycle
for each domain-reload/scene-reload combination. The report captures suspended
state, pending object work, search-cache work, queued invalidation, paused
indicator text, mutation-control availability, resume state, and cleanup. Each
configuration also imports, moves, and deletes a fixture asset during Play Mode
through `AssetDatabase`, proving that the real asset-postprocessor path queues
one coalesced invalidation without interrupting in-flight work. The scenario
uses a fixture of at least 201 settings assets so the async queue is
observable, temporarily adds that fixture type to the window's in-memory managed
catalog for search-cache coverage, uses reflection only for diagnostics, and
does not add an automation API or a background process to the package. It also
creates and recompiles a disposable editor script during Play Mode, then closes
and reopens the window during Play Mode to verify script-reload and callback
cleanup. The probe and fixture are removed in the final cleanup path.
