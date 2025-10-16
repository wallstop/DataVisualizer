namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;
    using WallstopStudios.DataVisualizer;

    internal sealed class ProcessorExecutionStartedEvent
    {
        public ProcessorExecutionStartedEvent(
            IDataProcessor processor,
            Type targetType,
            int objectCount,
            int pendingCount
        )
        {
            Processor = processor;
            TargetType = targetType;
            ObjectCount = objectCount;
            PendingCount = pendingCount;
        }

        public IDataProcessor Processor { get; }

        public Type TargetType { get; }

        public int ObjectCount { get; }

        public int PendingCount { get; }
    }
}
