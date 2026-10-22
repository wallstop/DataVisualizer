namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Search;

    // Immutable copied values; this record never owns a live asset reference.
    public sealed class AssetMetadataRecord
    {
        public string Guid => Document.Guid;
        public string Path { get; }
        public Type ExactType => Document.ExactType;
        public string DisplayTitle { get; }
        public IReadOnlyList<string> Labels { get; }
        public AssetSearchDocument Document { get; }

        public AssetMetadataRecord(string guid, ScriptableObject asset)
            : this(
                new AssetSearchDocument(guid, asset.name, asset.GetType(), asset),
                asset.GetType(),
                AssetDatabase.GetAssetPath(asset),
                asset is IDisplayable displayable ? displayable.Title : asset.name,
                AssetDatabase.GetLabels(asset)
            ) { }

        public AssetMetadataRecord(AssetSearchDocument document, Type exactType, string path, string displayTitle, IReadOnlyList<string> labels)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            if (exactType == null)
            {
                throw new ArgumentNullException(nameof(exactType));
            }
            if (string.IsNullOrEmpty(document.Guid) || !ReferenceEquals(document.ExactType, exactType))
            {
                throw new ArgumentException("Metadata requires a GUID and matching exact type.", nameof(document));
            }
            Path = path ?? string.Empty;
            DisplayTitle = displayTitle ?? document.Name;
            string[] copiedLabels = new string[labels?.Count ?? 0];
            for (int index = 0; index < copiedLabels.Length; index++)
            {
                copiedLabels[index] = labels[index];
            }
            Labels = Array.AsReadOnly(copiedLabels);
        }
    }
}
