namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;

    [Serializable]
    public sealed class NamespaceTypeOrder
    {
        public string namespaceKey = string.Empty;
        public List<string> typeNames = new();

        public NamespaceTypeOrder Clone()
        {
            return new NamespaceTypeOrder
            {
                namespaceKey = namespaceKey,
                typeNames = new(typeNames),
            };
        }
    }
}
