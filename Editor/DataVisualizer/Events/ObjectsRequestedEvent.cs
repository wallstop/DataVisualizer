namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;
    using System.Collections.Generic;

    internal sealed class ObjectsRequestedEvent
    {
        public ObjectsRequestedEvent(
            Type requestedType,
            IReadOnlyCollection<string> requestedGuids,
            bool forceRefresh
        )
        {
            RequestedType = requestedType;
            RequestedGuids = requestedGuids ?? Array.Empty<string>();
            ForceRefresh = forceRefresh;
        }

        public Type RequestedType { get; }

        public IReadOnlyCollection<string> RequestedGuids { get; }

        public bool ForceRefresh { get; }
    }
}
