namespace WallstopStudios.Editor.DataVisualizer.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Helper;
    using UnityEngine;

    [CreateAssetMenu(
        fileName = "DataVisualizerSettings",
        menuName = "DataVisualizer/Data Visualizer Settings",
        order = 1
    )]
    public sealed class DataVisualizerSettings : ScriptableObject
    {
        public const string DefaultDataFolderPath = "Assets/Data";

        public string DataFolderPath => _dataFolderPath;

        [Tooltip(
            "Path relative to the project root (e.g., Assets/Data) where DataObject assets might be located or created."
        )]
        [SerializeField]
        internal string _dataFolderPath = DefaultDataFolderPath;

        [Tooltip(
            "If true, window state (selection, order, collapse) is saved globally in EditorPrefs. If false, state is saved within this settings asset file."
        )]
        public bool persistStateInSettingsAsset;

        [
            Header("Saved State (Internal - Use only if EditorPrefs is disabled)"),
            SerializeField,
            HideInInspector
        ]
        internal string lastSelectedNamespaceKey;

        [SerializeField]
        [HideInInspector]
        internal string lastSelectedTypeName;

        [SerializeField, HideInInspector]
        internal List<LastObjectSelectionEntry> lastObjectSelections = new();

        [SerializeField]
        [HideInInspector]
        internal List<string> namespaceOrder = new();

        [SerializeField]
        [HideInInspector]
        internal List<NamespaceTypeOrder> typeOrder = new();

        [SerializeField]
        [HideInInspector]
        internal List<NamespaceCollapseState> namespaceCollapseStates = new();

        private void OnValidate()
        {
            if (Application.isEditor && !Application.isPlaying)
            {
                if (string.IsNullOrWhiteSpace(_dataFolderPath))
                {
                    _dataFolderPath = DefaultDataFolderPath;
                }

                _dataFolderPath = _dataFolderPath.SanitizePath();
            }
        }

        public void HydrateFrom(DataVisualizerUserState userState)
        {
            if (userState == null)
            {
                return;
            }
            lastSelectedNamespaceKey = userState.lastSelectedNamespaceKey;
            lastSelectedTypeName = userState.lastSelectedTypeName;
            namespaceOrder = userState.namespaceOrder?.ToList() ?? new List<string>();
            typeOrder =
                userState.typeOrders?.Select(order => order.Clone()).ToList()
                ?? new List<NamespaceTypeOrder>();
            lastObjectSelections =
                userState.LastObjectSelections?.Select(selection => selection.Clone()).ToList()
                ?? new List<LastObjectSelectionEntry>();
            ;
            namespaceCollapseStates =
                userState.NamespaceCollapseStates?.Select(state => state.Clone()).ToList()
                ?? new List<NamespaceCollapseState>();
        }

        internal void SetLastObjectForType(string typeName, string guid)
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
                string.Equals(e.typeName, typeName, StringComparison.Ordinal)
            );
            lastObjectSelections.Add(
                new LastObjectSelectionEntry { typeName = typeName, objectGuid = guid }
            );
        }

        internal string GetLastObjectForType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            return lastObjectSelections
                .Find(e => string.Equals(e.typeName, typeName, StringComparison.Ordinal))
                ?.objectGuid;
        }

        internal List<string> GetOrCreateTypeOrderList(string namespaceKey)
        {
            NamespaceTypeOrder entry = typeOrder.Find(o =>
                string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
            );
            if (entry != null)
            {
                return entry.typeNames;
            }

            entry = new NamespaceTypeOrder() { namespaceKey = namespaceKey };
            typeOrder.Add(entry);
            return entry.typeNames;
        }

        internal NamespaceCollapseState GetOrCreateCollapseState(string namespaceKey)
        {
            NamespaceCollapseState entry = namespaceCollapseStates.Find(o =>
                string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
            );
            if (entry != null)
            {
                return entry;
            }

            entry = new NamespaceCollapseState { namespaceKey = namespaceKey, isCollapsed = false }; // Default expanded
            namespaceCollapseStates.Add(entry);
            return entry;
        }
    }
}
