namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class LabelFilterResult
    {
        public LabelFilterResult(
            IReadOnlyList<ScriptableObject> filteredObjects,
            IReadOnlyList<DataAssetMetadata> filteredMetadata,
            IReadOnlyCollection<string> uniqueLabels,
            int totalCount,
            int matchedCount,
            string statusMessage
        )
        {
            FilteredObjects = filteredObjects;
            FilteredMetadata = filteredMetadata;
            UniqueLabels = uniqueLabels;
            TotalCount = totalCount;
            MatchedCount = matchedCount;
            StatusMessage = statusMessage;
        }

        public IReadOnlyList<ScriptableObject> FilteredObjects { get; }

        public IReadOnlyList<DataAssetMetadata> FilteredMetadata { get; }

        public IReadOnlyCollection<string> UniqueLabels { get; }

        public int TotalCount { get; }

        public int MatchedCount { get; }

        public string StatusMessage { get; }
    }
}
