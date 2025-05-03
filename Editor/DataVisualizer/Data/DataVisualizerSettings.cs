namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Helper;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;

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
            "If true, window state (selection, order, collapse) is saved in a special ScriptableObject. If false, state is saved within this settings asset file."
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

        [SerializeField]
        [HideInInspector]
        internal List<LastObjectSelectionEntry> lastObjectSelections = new();

        [SerializeField]
        [HideInInspector]
        internal List<string> namespaceOrder = new();

        [FormerlySerializedAs("typeOrder")]
        [SerializeField]
        [HideInInspector]
        internal List<NamespaceTypeOrder> typeOrders = new();

        [SerializeField]
        [HideInInspector]
        internal List<TypeObjectOrder> objectOrders = new();

        [SerializeField]
        [HideInInspector]
        internal List<NamespaceCollapseState> namespaceCollapseStates = new();

        [SerializeField]
        [HideInInspector]
        internal List<string> managedTypeNames = new();

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

        public void MarkDirty()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
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
            typeOrders =
                userState.typeOrders?.Select(order => order.Clone()).ToList()
                ?? new List<NamespaceTypeOrder>();
            lastObjectSelections =
                userState.lastObjectSelections?.Select(selection => selection.Clone()).ToList()
                ?? new List<LastObjectSelectionEntry>();

            namespaceCollapseStates =
                userState.namespaceCollapseStates?.Select(state => state.Clone()).ToList()
                ?? new List<NamespaceCollapseState>();
            objectOrders =
                userState.objectOrders?.Select(order => order.Clone()).ToList()
                ?? new List<TypeObjectOrder>();
            managedTypeNames = userState.managedTypeNames?.ToList() ?? new List<string>();
            MarkDirty();
        }

        internal List<string> GetOrCreateObjectOrderList(string typeFullName)
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
                string.Equals(e.typeFullName, typeName, StringComparison.Ordinal)
            );
            lastObjectSelections.Add(
                new LastObjectSelectionEntry { typeFullName = typeName, objectGuid = guid }
            );
        }

        internal string GetLastObjectForType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            return lastObjectSelections
                .Find(e => string.Equals(e.typeFullName, typeName, StringComparison.Ordinal))
                ?.objectGuid;
        }

        internal List<string> GetOrCreateTypeOrderList(string namespaceKey)
        {
            NamespaceTypeOrder entry = typeOrders.Find(o =>
                string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
            );
            if (entry != null)
            {
                return entry.typeNames;
            }

            entry = new NamespaceTypeOrder() { namespaceKey = namespaceKey };
            typeOrders.Add(entry);
            return entry.typeNames;
        }

        internal bool HasCollapseState(string namespaceKey)
        {
            NamespaceCollapseState entry = namespaceCollapseStates.Find(o =>
                string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
            );
            return entry != null;
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
