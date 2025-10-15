namespace WallstopStudios.DataVisualizer.Editor.Diagnostics
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;

    internal sealed class PerformanceMetricsRecorder
    {
        private readonly List<PerformanceMetricSnapshot> _snapshots =
            new List<PerformanceMetricSnapshot>();

        public IReadOnlyList<PerformanceMetricSnapshot> Snapshots => _snapshots;

        public PerformanceMetricSnapshot RecordScenario(string scenarioName, Action scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            if (string.IsNullOrWhiteSpace(scenarioName))
            {
                throw new ArgumentException(
                    "Scenario name must be provided.",
                    nameof(scenarioName)
                );
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long beforeBytes = GC.GetTotalMemory(true);
            Stopwatch stopwatch = Stopwatch.StartNew();
            scenario();
            stopwatch.Stop();
            long afterBytes = GC.GetTotalMemory(false);
            long allocatedBytes = Math.Max(0, afterBytes - beforeBytes);

            PerformanceMetricSnapshot snapshot = new PerformanceMetricSnapshot(
                scenarioName,
                stopwatch.ElapsedMilliseconds,
                allocatedBytes
            );
            _snapshots.Add(snapshot);
            return snapshot;
        }

        public void SaveSnapshots(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path must be provided.", nameof(filePath));
            }

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using StreamWriter writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
            writer.WriteLine("{\"scenarios\":[");
            for (int index = 0; index < _snapshots.Count; index++)
            {
                PerformanceMetricSnapshot snapshot = _snapshots[index];
                writer.Write(
                    "{0}{1}\"name\":\"{2}\",\"elapsedMilliseconds\":{3},\"bytesAllocated\":{4}{5}",
                    "  ",
                    '{',
                    Escape(snapshot.ScenarioName),
                    snapshot.ElapsedMilliseconds,
                    snapshot.BytesAllocated,
                    '}'
                );
                if (index < _snapshots.Count - 1)
                {
                    writer.Write(',');
                }

                writer.WriteLine();
            }

            writer.WriteLine("]}");
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
