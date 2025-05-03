namespace WallstopStudios.DataVisualizer.Editor.Editor.DataVisualizer.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    [Serializable]
    public sealed class DataVisualizerUserState
    {
        public string lastSelectedNamespaceKey = string.Empty;
        public string lastSelectedTypeName = string.Empty;

        public List<string> namespaceOrder = new();
        public List<NamespaceTypeOrder> typeOrders = new();

        public List<LastObjectSelectionEntry> lastObjectSelections = new();
        public List<NamespaceCollapseState> namespaceCollapseStates = new();

        public List<TypeObjectOrder> objectOrders = new();
        public List<string> managedTypeNames = new();

        public void HydrateFrom(DataVisualizerSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            lastSelectedNamespaceKey = settings.lastSelectedNamespaceKey;
            lastSelectedTypeName = settings.lastSelectedTypeName;
            namespaceOrder = settings.namespaceOrder?.ToList() ?? new List<string>();
            typeOrders =
                settings.typeOrders?.Select(order => order.Clone()).ToList()
                ?? new List<NamespaceTypeOrder>();
            lastObjectSelections =
                settings.lastObjectSelections?.Select(selection => selection.Clone()).ToList()
                ?? new List<LastObjectSelectionEntry>();
            namespaceCollapseStates =
                settings.namespaceCollapseStates?.Select(selection => selection.Clone()).ToList()
                ?? new List<NamespaceCollapseState>();
            objectOrders =
                settings.objectOrders?.Select(order => order.Clone()).ToList()
                ?? new List<TypeObjectOrder>();
            managedTypeNames = settings.managedTypeNames?.ToList() ?? new List<string>();
        }

        public List<string> GetOrCreateObjectOrderList(string typeFullName)
        {
            TypeObjectOrder entry = objectOrders.Find(o =>
                string.Equals(o.TypeFullName, typeFullName, StringComparison.Ordinal)
            );
            if (entry != null)
            {
                return entry.ObjectGuids;
            }

            entry = new TypeObjectOrder { TypeFullName = typeFullName };
            objectOrders.Add(entry);
            return entry.ObjectGuids;
        }

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

            lastObjectSelections.RemoveAll(e =>
                string.Equals(e.typeFullName, typeName, StringComparison.Ordinal)
            );
            lastObjectSelections.Add(
                new LastObjectSelectionEntry { typeFullName = typeName, objectGuid = guid }
            );
        }

        public string GetLastObjectForType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            return lastObjectSelections
                .Find(e => string.Equals(e.typeFullName, typeName, StringComparison.Ordinal))
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

        public bool HasCollapseState(string namespaceKey)
        {
            NamespaceCollapseState entry = namespaceCollapseStates.Find(o =>
                string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
            );
            return entry != null;
        }

        public NamespaceCollapseState GetOrCreateCollapseState(string namespaceKey)
        {
            NamespaceCollapseState entry = namespaceCollapseStates.Find(o =>
                string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
            );
            if (entry != null)
            {
                return entry;
            }

            entry = new NamespaceCollapseState { namespaceKey = namespaceKey, isCollapsed = false };
            namespaceCollapseStates.Add(entry);
            return entry;
        }
    }
}
