namespace WallstopStudios.DataVisualizer.Editor.Events
{
    internal enum TypeNavigationDirection
    {
        Previous = 0,
        Next = 1,
    }

    internal sealed class TypeNavigationRequestedEvent
    {
        public TypeNavigationRequestedEvent(TypeNavigationDirection direction)
        {
            Direction = direction;
        }

        public TypeNavigationDirection Direction { get; }
    }
}
