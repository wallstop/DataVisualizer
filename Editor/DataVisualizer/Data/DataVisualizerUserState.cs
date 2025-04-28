namespace WallstopStudios.Editor.DataVisualizer.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;

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
                settings.typeOrder?.Select(order => order.Clone()).ToList()
                ?? new List<NamespaceTypeOrder>();
            LastObjectSelections =
                settings.lastObjectSelections?.Select(selection => selection.Clone()).ToList()
                ?? new List<LastObjectSelectionEntry>();
            NamespaceCollapseStates =
                settings.namespaceCollapseStates?.Select(selection => selection.Clone()).ToList()
                ?? new List<NamespaceCollapseState>();

#if UNITY_EDITOR
            EditorUtility.SetDirty(settings);
#endif
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
