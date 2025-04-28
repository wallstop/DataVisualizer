namespace WallstopStudios.Editor.DataVisualizer.Data
{
    using System;
    using System.Collections.Generic;

    [Serializable]
    public sealed class NamespaceTypeOrder
    {
        public string namespaceKey = string.Empty;
        public List<string> typeNames = new();
    }
}
