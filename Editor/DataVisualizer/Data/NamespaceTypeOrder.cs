namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    [Serializable]
    public sealed class NamespaceTypeOrder
    {
        public string namespaceKey = string.Empty;
        public List<string> typeNames = new();

        public NamespaceTypeOrder Clone()
        {
            return new NamespaceTypeOrder
            {
                namespaceKey = namespaceKey ?? string.Empty,
                typeNames = typeNames?.ToList() ?? new List<string>(),
            };
        }
    }
}
