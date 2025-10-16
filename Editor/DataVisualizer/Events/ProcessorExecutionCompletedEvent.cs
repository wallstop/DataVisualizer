namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;
    using WallstopStudios.DataVisualizer;

    internal sealed class ProcessorExecutionCompletedEvent
    {
        public ProcessorExecutionCompletedEvent(
            IDataProcessor processor,
            Type targetType,
            int objectCount,
            double durationSeconds,
            int pendingCount
        )
        {
            Processor = processor;
            TargetType = targetType;
            ObjectCount = objectCount;
            DurationSeconds = durationSeconds;
            PendingCount = pendingCount;
        }

        public IDataProcessor Processor { get; }

        public Type TargetType { get; }

        public int ObjectCount { get; }

        public double DurationSeconds { get; }

        public int PendingCount { get; }
    }
}
