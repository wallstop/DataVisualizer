namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;

    internal sealed class TypeRemovalRequestedEvent
    {
        public TypeRemovalRequestedEvent(Type type)
        {
            Type = type;
        }

        public Type Type { get; }
    }
}
