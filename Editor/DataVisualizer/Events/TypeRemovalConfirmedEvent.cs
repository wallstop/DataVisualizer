namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;

    internal sealed class TypeRemovalConfirmedEvent
    {
        public TypeRemovalConfirmedEvent(Type type)
        {
            Type = type;
        }

        public Type Type { get; }
    }
}
