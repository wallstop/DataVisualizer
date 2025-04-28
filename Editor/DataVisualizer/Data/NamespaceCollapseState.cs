namespace WallstopStudios.Editor.DataVisualizer.Data
{
    using System;

    [Serializable]
    public sealed class NamespaceCollapseState
    {
        public string namespaceKey = string.Empty;
        public bool isCollapsed;
    }
}
