namespace WallstopStudios.DataVisualizer.Editor.Diagnostics
{
    using System;

    [Serializable]
    internal struct PerformanceMetricSnapshot
    {
        public PerformanceMetricSnapshot(
            string scenarioName,
            long elapsedMilliseconds,
            long bytesAllocated
        )
        {
            ScenarioName = scenarioName;
            ElapsedMilliseconds = elapsedMilliseconds;
            BytesAllocated = bytesAllocated;
        }

        public string ScenarioName { get; }

        public long ElapsedMilliseconds { get; }

        public long BytesAllocated { get; }
    }
}
