namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;
    using Helper;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;
    using Utilities;
    using WallstopStudios.DataVisualizer;

    [CreateAssetMenu(
        fileName = "DataVisualizerSettings",
        menuName = "Wallstop Studios/DataVisualizer/Data Visualizer Settings",
        order = 1
    )]
    public sealed class DataVisualizerSettings : ScriptableObject
    {
        public const string DefaultDataFolderPath = "Assets/Data";

        public string DataFolderPath => _dataFolderPath;

        [Tooltip(
            "If true, window state (selection, order, collapse) is saved in a special ScriptableObject. If false, state is saved within this settings asset file."
        )]
        public bool persistStateInSettingsAsset;

        [Tooltip("If true, when selecting an Object, it will be selected in the Inspector.")]
        public bool selectActiveObject;

        [Tooltip(
            "Path relative to the project root (e.g., Assets/Data) where DataObject assets might be located or created."
        )]
        [SerializeField]
        internal string _dataFolderPath = DefaultDataFolderPath;

        [Header("Saved State (Internal - Use only if EditorPrefs is disabled)")]
        [SerializeField]
        [ReadOnly]
        internal string lastSelectedNamespaceKey;

        [SerializeField]
        [ReadOnly]
        [FormerlySerializedAs("lastSelectedTypeName")]
        internal string lastSelectedTypeFullName;

        [SerializeField]
        [ReadOnly]
        internal List<LastObjectSelectionEntry> lastObjectSelections = new();

        [SerializeField]
        [ReadOnly]
        internal List<string> namespaceOrder = new();

        [FormerlySerializedAs("typeOrder")]
        [SerializeField]
        [ReadOnly]
        internal List<NamespaceTypeOrder> typeOrders = new();

        [SerializeField]
        [ReadOnly]
        internal List<TypeObjectOrder> objectOrders = new();

        [SerializeField]
        [ReadOnly]
        internal List<NamespaceCollapseState> namespaceCollapseStates = new();

        [SerializeField]
        [ReadOnly]
        internal List<string> managedTypeNames = new();

        [SerializeField]
        [ReadOnly]
        internal List<TypeLabelFilterConfig> labelFilterConfigs = new();

        [SerializeField]
        [ReadOnly]
        internal List<ProcessorState> processorStates = new();

        public void MarkDirty()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        public bool SetSelectActiveObject(bool value)
        {
            if (selectActiveObject == value)
            {
                return false;
            }

            selectActiveObject = value;
            MarkDirty();
            return true;
        }

        public void HydrateFrom(DataVisualizerUserState userState)
        {
            if (userState == null)
            {
                return;
            }

            lastSelectedNamespaceKey = userState.lastSelectedNamespaceKey;
            lastSelectedTypeFullName = userState.lastSelectedTypeFullName;
            namespaceOrder = PersistedStateCopy.CloneStrings(userState.namespaceOrder);
            typeOrders = PersistedStateCopy.CloneNamespaceTypeOrders(userState.typeOrders);
            lastObjectSelections = PersistedStateCopy.CloneLastObjectSelections(
                userState.lastObjectSelections
            );
            namespaceCollapseStates = PersistedStateCopy.CloneNamespaceCollapseStates(
                userState.namespaceCollapseStates
            );
            objectOrders = PersistedStateCopy.CloneTypeObjectOrders(userState.objectOrders);
            managedTypeNames = PersistedStateCopy.CloneStrings(userState.managedTypeNames);
            labelFilterConfigs = PersistedStateCopy.CloneTypeLabelFilterConfigs(
                userState.labelFilterConfigs
            );
            processorStates = PersistedStateCopy.CloneProcessorStates(userState.processorStates);
            MarkDirty();
        }

        public bool SetNamespaceCollapsed(string namespaceKey, bool isCollapsed)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return false;
            }

            namespaceCollapseStates ??= new List<NamespaceCollapseState>();
            bool changed = NamespaceCollapseState.SetCollapsed(
                namespaceCollapseStates,
                namespaceKey,
                isCollapsed
            );
            if (changed)
            {
                MarkDirty();
            }

            return changed;
        }

        public bool RemoveNamespaceCollapseState(string namespaceKey)
        {
            bool changed = NamespaceCollapseState.Remove(namespaceCollapseStates, namespaceKey);
            if (changed)
            {
                MarkDirty();
            }

            return changed;
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

        internal bool SetLastObjectForType(string typeFullName, string guid)
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
                MarkDirty();
                return true;
            }

            if (
                0 <= existingIndex
                && string.Equals(
                    lastObjectSelections[existingIndex].objectGuid,
                    guid,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }

            if (0 <= existingIndex)
            {
                lastObjectSelections[existingIndex].objectGuid = guid;
            }
            else
            {
                lastObjectSelections.Add(
                    new LastObjectSelectionEntry { typeFullName = typeFullName, objectGuid = guid }
                );
            }

            MarkDirty();
            return true;
        }

        internal string GetLastObjectForType(string typeFullName)
        {
            if (string.IsNullOrWhiteSpace(typeFullName))
            {
                return null;
            }

            return lastObjectSelections
                ?.Find(e => string.Equals(e.typeFullName, typeFullName, StringComparison.Ordinal))
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
    }
}
