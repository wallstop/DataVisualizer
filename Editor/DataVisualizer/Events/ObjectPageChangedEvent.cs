namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;

    internal sealed class ObjectPageChangedEvent
    {
        public ObjectPageChangedEvent(Type objectType, int pageIndex)
        {
            ObjectType = objectType;
            PageIndex = pageIndex;
        }

        public Type ObjectType { get; }

        public int PageIndex { get; }
    }
}
