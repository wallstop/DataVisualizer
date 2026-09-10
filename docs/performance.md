# Performance evidence

Performance measurements are local diagnostics for a specific fixture and editor,
not universal guarantees. The benchmark driver is in
`scripts/benchmark/benchmark_driver.py`; it keeps fixtures and reports outside the
package and excludes Python/MCP transport from Unity timing boundaries.

## Current measured result

The confirmed local host is an Apple M3 Max with 64 GB RAM, Metal graphics, and
Unity `6000.4.6f1`. A 100-asset fixture was measured with five warm-ups and 30
matched samples for each Play-entry case:

| Case | p95 | Target |
| --- | ---: | ---: |
| Window open, idle | 229.9496 ms | ≤20 ms |
| Window open, indexing | 245.4825 ms | ≤20 ms |
| Window closed | 234.8239 ms | ≤20 ms |

The Play-entry target is an explicit miss and has not been relaxed. The benchmark
also verified deterministic 100, 1,000, and 10,000-asset fixtures with ordinary
`ScriptableObject` and `BaseDataObject` controls, labels, nested values, shared
references, and duplicate short type names. Cooperative 500-asset batches and
50,000-asset fixture validation are implemented; replacement live 50,000 evidence
is still pending.

## Run locally

```bash
python3 scripts/benchmark/benchmark_driver.py \
  --mode mcp \
  --host-project /Users/wallstop/Code/DataVisualizer \
  --unity-version 6000.4.6f1 \
  --fixture-size 100 \
  --suite play-entry \
  --output-dir /tmp/data-visualizer-results
```

Use `--mode direct` for a host-local Unity executable or `benchmark_matrix.py`
for multiple explicitly installed editor versions. The matrix records unavailable
metrics rather than presenting missing cold/warm, search, memory, rendering, or
player data as passes.

## Interpretation

- Fixture generation/import is reported separately from shell construction and
  Play-entry timing.
- Unity uses monotonic `Stopwatch` measurements and focused profiler markers.
- A warm interaction result is complete only when its required samples are present.
- Unity object loads, retained references, and native calls may exceed one
  cooperative slice; those limits are recorded instead of being claimed preemptible.
