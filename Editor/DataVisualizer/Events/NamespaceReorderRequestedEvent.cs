namespace WallstopStudios.DataVisualizer.Editor.Events
{
    internal sealed class NamespaceReorderRequestedEvent
    {
        public NamespaceReorderRequestedEvent(string namespaceKey, int targetIndex)
        {
            NamespaceKey = namespaceKey;
            TargetIndex = targetIndex;
        }

        public string NamespaceKey { get; }

        public int TargetIndex { get; }
    }
}
