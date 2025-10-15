# Performance Metrics Usage

`PerformanceMetricsRecorder` is a lightweight helper for capturing timing and allocation snapshots for editor workflows. Use it in manual profiling sessions or editor tests to compare scenarios before and after refactors.

## Typical Workflow

```csharp
PerformanceMetricsRecorder recorder = new PerformanceMetricsRecorder();
recorder.RecordScenario("BuildObjectsView", () =>
{
    window.BuildObjectsView();
});
recorder.SaveSnapshots("Documentation/Metrics/build-objects-view.json");
```

The resulting file is a simple JSON payload that can be checked into `Documentation/Metrics/` for future comparison.
