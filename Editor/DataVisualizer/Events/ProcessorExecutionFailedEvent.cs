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
            Exception exception
        )
        {
            Processor = processor;
            TargetType = targetType;
            ObjectCount = objectCount;
            PendingCount = pendingCount;
            Exception = exception;
        }

        public IDataProcessor Processor { get; }

        public Type TargetType { get; }

        public int ObjectCount { get; }

        public int PendingCount { get; }

        public Exception Exception { get; }
    }
}
