namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;
    using WallstopStudios.DataVisualizer;

    internal sealed class ProcessorExecutionFailedEvent
    {
        public ProcessorExecutionFailedEvent(
            IDataProcessor processor,
            Type targetType,
            int objectCount,
            int pendingCount,
            Exception exception,
            double durationSeconds,
            long allocatedBytes
        )
        {
            Processor = processor;
            TargetType = targetType;
            ObjectCount = objectCount;
            PendingCount = pendingCount;
            Exception = exception;
            DurationSeconds = durationSeconds;
            AllocatedBytes = allocatedBytes;
        }

        public IDataProcessor Processor { get; }

        public Type TargetType { get; }

        public int ObjectCount { get; }

        public int PendingCount { get; }

        public Exception Exception { get; }

        public double DurationSeconds { get; }

        public long AllocatedBytes { get; }
    }
}
