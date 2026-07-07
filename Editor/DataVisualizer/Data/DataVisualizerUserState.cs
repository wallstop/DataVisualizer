namespace WallstopStudios.DataVisualizer.Editor.Data
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
        public List<TypeLabelFilterConfig> labelFilterConfigs = new();
        public List<ProcessorState> processorStates = new();

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
            labelFilterConfigs =
                settings.labelFilterConfigs?.Select(config => config.Clone()).ToList()
                ?? new List<TypeLabelFilterConfig>();
            processorStates =
                settings.processorStates?.Select(state => state.Clone()).ToList()
                ?? new List<ProcessorState>();
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

        public bool SetLastObjectForType(string typeFullName, string guid)
        {
            if (string.IsNullOrWhiteSpace(typeFullName))
            {
                return false;
            }

            lastObjectSelections ??= new List<LastObjectSelectionEntry>();
            int existingIndex = lastObjectSelections.FindIndex(e =>
                string.Equals(e.typeFullName, typeFullName, StringComparison.Ordinal)
            );
            if (string.IsNullOrWhiteSpace(guid))
            {
                if (existingIndex < 0)
                {
                    return false;
                }

                lastObjectSelections.RemoveAt(existingIndex);
                return true;
            }

            if (
                existingIndex >= 0
                && string.Equals(
                    lastObjectSelections[existingIndex].objectGuid,
                    guid,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }

            if (existingIndex >= 0)
            {
                lastObjectSelections[existingIndex].objectGuid = guid;
            }
            else
            {
                lastObjectSelections.Add(
                    new LastObjectSelectionEntry { typeFullName = typeFullName, objectGuid = guid }
                );
            }

            return true;
        }

        public string GetLastObjectForType(string typeFullName)
        {
            if (string.IsNullOrWhiteSpace(typeFullName))
            {
                return null;
            }

            return lastObjectSelections
                ?.Find(e => string.Equals(e.typeFullName, typeFullName, StringComparison.Ordinal))
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

        public bool SetNamespaceCollapsed(string namespaceKey, bool isCollapsed)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return false;
            }

            namespaceCollapseStates ??= new List<NamespaceCollapseState>();
            return NamespaceCollapseState.SetCollapsed(
                namespaceCollapseStates,
                namespaceKey,
                isCollapsed
            );
        }

        public bool RemoveNamespaceCollapseState(string namespaceKey)
        {
            return NamespaceCollapseState.Remove(namespaceCollapseStates, namespaceKey);
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
