namespace WallstopStudios.Editor.DataVisualizer.Data
{
    using System;
    using System.Collections.Generic;

    [Serializable]
    public sealed class DataVisualizerUserState
    {
        public int version = 1;
        public string lastSelectedNamespaceKey = string.Empty;
        public string lastSelectedTypeName = string.Empty;

        public List<string> namespaceOrder = new();
        public List<NamespaceTypeOrder> typeOrders = new();

        public List<LastObjectSelectionEntry> LastObjectSelections = new();
        public List<NamespaceCollapseState> NamespaceCollapseStates = new();

        public void SetLastObjectForType(string typeName, string guid)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(guid))
            {
                return;
            }

            LastObjectSelections.RemoveAll(e =>
                string.Equals(e.typeName, typeName, StringComparison.Ordinal)
            );
            LastObjectSelections.Add(
                new LastObjectSelectionEntry { typeName = typeName, objectGuid = guid }
            );
        }

        public string GetLastObjectForType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            return LastObjectSelections
                .Find(e => string.Equals(e.typeName, typeName, StringComparison.Ordinal))
                ?.objectGuid;
        }

        public List<string> GetOrCreateTypeOrderList(string namespaceKey)
        {
            NamespaceTypeOrder entry = typeOrders.Find(o =>
                string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
            );
            if (entry != null)
            {
                return entry.typeNames;
            }

            entry = new NamespaceTypeOrder { namespaceKey = namespaceKey };
            typeOrders.Add(entry);
            return entry.typeNames;
        }

        public NamespaceCollapseState GetOrCreateCollapseState(string namespaceKey)
        {
            NamespaceCollapseState entry = NamespaceCollapseStates.Find(o =>
                string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
            );
            if (entry != null)
            {
                return entry;
            }

            entry = new NamespaceCollapseState { namespaceKey = namespaceKey, isCollapsed = false };
            NamespaceCollapseStates.Add(entry);
            return entry;
        }
    }
}
