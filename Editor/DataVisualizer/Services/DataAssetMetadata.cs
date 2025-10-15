namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    internal sealed class DataAssetMetadata
    {
        public DataAssetMetadata(
            string guid,
            string path,
            Type assetType,
            string typeFullName,
            string displayName,
            IReadOnlyList<string> labels,
            DateTime lastIndexedUtc
        )
        {
            Guid = guid;
            Path = path;
            AssetType = assetType;
            TypeFullName = typeFullName;
            DisplayName = displayName;
            Labels = labels;
            LastIndexedUtc = lastIndexedUtc;
        }

        public string Guid { get; }

        public string Path { get; }

        public Type AssetType { get; }

        public string TypeFullName { get; }

        public string DisplayName { get; }

        public IReadOnlyList<string> Labels { get; }

        public DateTime LastIndexedUtc { get; }

        public ScriptableObject LoadAsset()
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return null;
            }

            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Path);
            return asset;
        }
    }
}
