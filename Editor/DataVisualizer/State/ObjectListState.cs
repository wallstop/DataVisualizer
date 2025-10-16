namespace WallstopStudios.DataVisualizer.Editor.State
{
    using System.Collections.Generic;
    using Services;
    using UnityEngine;

    public sealed class ObjectListState
    {
        private readonly List<ScriptableObject> _filteredObjects = new List<ScriptableObject>();
        private readonly List<DataAssetMetadata> _filteredMetadata = new List<DataAssetMetadata>();
        private readonly List<ScriptableObject> _displayedObjects = new List<ScriptableObject>();
        private readonly List<DataAssetMetadata> _displayedMetadata = new List<DataAssetMetadata>();
        private int _displayStartIndex;

        public IReadOnlyList<ScriptableObject> FilteredObjects => _filteredObjects;

        public IReadOnlyList<DataAssetMetadata> FilteredMetadata => _filteredMetadata;

        public IReadOnlyList<ScriptableObject> DisplayedObjects => _displayedObjects;

        public IReadOnlyList<DataAssetMetadata> DisplayedMetadata => _displayedMetadata;

        public int DisplayStartIndex => _displayStartIndex;

        internal List<ScriptableObject> FilteredObjectsBuffer => _filteredObjects;

        internal List<DataAssetMetadata> FilteredMetadataBuffer => _filteredMetadata;

        internal List<ScriptableObject> DisplayedObjectsBuffer => _displayedObjects;

        internal List<DataAssetMetadata> DisplayedMetadataBuffer => _displayedMetadata;

        public void ClearFiltered()
        {
            _filteredObjects.Clear();
            _filteredMetadata.Clear();
        }

        public void ClearDisplayed()
        {
            _displayedObjects.Clear();
            _displayedMetadata.Clear();
            _displayStartIndex = 0;
        }

        public void SetDisplayStartIndex(int startIndex)
        {
            if (startIndex < 0)
            {
                startIndex = 0;
            }

            _displayStartIndex = startIndex;
        }
    }
}
