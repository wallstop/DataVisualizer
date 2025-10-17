namespace WallstopStudios.DataVisualizer.Editor.State
{
    using System;
    using System.Collections.Generic;

    public sealed class DiagnosticsState
    {
        private readonly List<ProcessorExecutionTelemetry> _processorExecutions =
            new List<ProcessorExecutionTelemetry>();

        private int _maxProcessorExecutionSamples = 20;
        private bool _processorTelemetryEnabled;

        public bool ProcessorTelemetryEnabled
        {
            get { return _processorTelemetryEnabled; }
        }

        public IReadOnlyList<ProcessorExecutionTelemetry> ProcessorExecutions
        {
            get { return _processorExecutions; }
        }

        public int MaxProcessorExecutionSamples
        {
            get { return _maxProcessorExecutionSamples; }
            set
            {
                if (value < 1)
                {
                    value = 1;
                }

                if (_maxProcessorExecutionSamples == value)
                {
                    return;
                }

                _maxProcessorExecutionSamples = value;
                TrimProcessorSamples();
            }
        }

        public bool SetProcessorTelemetryEnabled(bool enabled)
        {
            if (_processorTelemetryEnabled == enabled)
            {
                return false;
            }

            _processorTelemetryEnabled = enabled;
            if (!enabled)
            {
                _processorExecutions.Clear();
            }

            return true;
        }

        public void RecordProcessorExecution(ProcessorExecutionTelemetry telemetry)
        {
            if (!_processorTelemetryEnabled || telemetry == null)
            {
                return;
            }

            _processorExecutions.Add(telemetry);
            TrimProcessorSamples();
        }

        public void Clear()
        {
            _processorExecutions.Clear();
        }

        private void TrimProcessorSamples()
        {
            int excess = _processorExecutions.Count - _maxProcessorExecutionSamples;
            if (excess <= 0)
            {
                return;
            }

            _processorExecutions.RemoveRange(0, excess);
        }
    }

    public sealed class ProcessorExecutionTelemetry
    {
        public ProcessorExecutionTelemetry(
            string processorName,
            string targetTypeFullName,
            int objectCount,
            bool succeeded,
            double durationSeconds,
            long allocatedBytes,
            DateTime timestampUtc
        )
        {
            ProcessorName = processorName ?? string.Empty;
            TargetTypeFullName = targetTypeFullName ?? string.Empty;
            ObjectCount = objectCount < 0 ? 0 : objectCount;
            Succeeded = succeeded;
            DurationSeconds = durationSeconds >= 0 ? durationSeconds : 0d;
            AllocatedBytes = allocatedBytes < 0 ? 0L : allocatedBytes;
            TimestampUtc = timestampUtc;
        }

        public string ProcessorName { get; }

        public string TargetTypeFullName { get; }

        public int ObjectCount { get; }

        public bool Succeeded { get; }

        public double DurationSeconds { get; }

        public long AllocatedBytes { get; }

        public DateTime TimestampUtc { get; }
    }
}
