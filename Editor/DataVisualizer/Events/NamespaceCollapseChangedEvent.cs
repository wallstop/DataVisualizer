namespace WallstopStudios.DataVisualizer.Editor.Events
{
    internal sealed class NamespaceCollapseChangedEvent
    {
        public NamespaceCollapseChangedEvent(string namespaceKey, bool isCollapsed)
        {
            NamespaceKey = namespaceKey;
            IsCollapsed = isCollapsed;
        }

        public string NamespaceKey { get; }

        public bool IsCollapsed { get; }
    }
}
