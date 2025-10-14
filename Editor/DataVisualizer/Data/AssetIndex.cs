namespace WallstopStudios.DataVisualizer.Editor.Data
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Helper.Pooling;
    using UnityEditor;
    using UnityEngine;

    internal sealed class AssetIndex
    {
        internal sealed class AssetMetadata
        {
            public string Guid { get; set; }
            public string Path { get; set; }
            public Type AssetType { get; set; }
            public string TypeFullName { get; set; }
            public string DisplayName { get; set; }
            public string[] Labels { get; set; }
            public DateTime LastIndexedUtc { get; set; }

            public ScriptableObject LoadAsset()
            {
                return AssetDatabase.LoadAssetAtPath<ScriptableObject>(Path);
            }
        }

        private readonly Dictionary<string, AssetMetadata> _entries = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<Type, List<string>> _guidsByType = new();
        private readonly Dictionary<string, string> _guidByPath = new(
            StringComparer.OrdinalIgnoreCase
        );

        public IReadOnlyList<string> GetGuidsForType(Type type)
        {
            if (type == null)
            {
                return Array.Empty<string>();
            }

            if (_guidsByType.TryGetValue(type, out List<string> guids))
            {
                return guids;
            }

            return Array.Empty<string>();
        }

        public bool TryGetMetadata(string guid, out AssetMetadata metadata)
        {
            return _entries.TryGetValue(guid, out metadata);
        }

        public bool TryGetMetadataByPath(string path, out AssetMetadata metadata)
        {
            metadata = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (_guidByPath.TryGetValue(path, out string guid))
            {
                return _entries.TryGetValue(guid, out metadata);
            }

            return false;
        }

        public IEnumerable<AssetMetadata> AllMetadata()
        {
            return _entries.Values;
        }

        public void Rebuild(IEnumerable<Type> types)
        {
            if (types == null)
            {
                return;
            }

            HashSet<string> seenGuids = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<Type, List<string>> updatedGuidsByType = new();

            foreach (Type type in types)
            {
                if (type == null)
                {
                    continue;
                }

                string[] locatedGuids = AssetDatabase.FindAssets($"t:{type.Name}");
                if (locatedGuids == null || locatedGuids.Length == 0)
                {
                    continue;
                }

                using PooledResource<List<string>> guidListResource = Buffers<string>.GetList(
                    locatedGuids.Length,
                    out List<string> typeGuids
                );

                foreach (string guid in locatedGuids)
                {
                    if (string.IsNullOrWhiteSpace(guid))
                    {
                        continue;
                    }

                    AssetMetadata metadata = BuildMetadata(type, guid);
                    if (metadata == null)
                    {
                        continue;
                    }

                    IndexMetadata(metadata);
                    typeGuids.Add(guid);
                    seenGuids.Add(guid);
                }

                typeGuids.Sort(CompareByDisplayName);
                updatedGuidsByType[type] = new List<string>(typeGuids);
            }

            _guidsByType.Clear();
            foreach (KeyValuePair<Type, List<string>> kvp in updatedGuidsByType)
            {
                _guidsByType[kvp.Key] = kvp.Value;
            }

            using PooledResource<List<string>> recycle = Buffers<string>.GetList(
                _entries.Count,
                out List<string> toRemove
            );

            foreach (KeyValuePair<string, AssetMetadata> entry in _entries)
            {
                if (!seenGuids.Contains(entry.Key))
                {
                    toRemove.Add(entry.Key);
                }
            }

            foreach (string guid in toRemove)
            {
                if (
                    _entries.TryGetValue(guid, out AssetMetadata stale)
                    && !string.IsNullOrWhiteSpace(stale.Path)
                )
                {
                    _guidByPath.Remove(stale.Path);
                }

                _entries.Remove(guid);
            }
        }

        public void RefreshGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return;
            }

            AssetMetadata current = null;
            if (_entries.TryGetValue(guid, out AssetMetadata existing))
            {
                current = existing;
            }

            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                Remove(guid);
                return;
            }

            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (asset == null)
            {
                Remove(guid);
                return;
            }

            Type assetType = asset.GetType();
            AssetMetadata updated = CreateMetadata(assetType, guid, assetPath, asset);
            if (updated == null)
            {
                Remove(guid);
                return;
            }

            if (
                current != null
                && !string.IsNullOrWhiteSpace(current.Path)
                && !string.Equals(current.Path, updated.Path, StringComparison.OrdinalIgnoreCase)
            )
            {
                _guidByPath.Remove(current.Path);
            }

            IndexMetadata(updated);

            if (!_guidsByType.TryGetValue(assetType, out List<string> guidList))
            {
                guidList = new List<string>();
                _guidsByType[assetType] = guidList;
            }

            if (!guidList.Contains(guid))
            {
                guidList.Add(guid);
            }

            guidList.Sort(CompareByDisplayName);

            if (current != null && current.AssetType != assetType)
            {
                RemoveGuidFromTypeList(current.AssetType, guid);
            }
        }

        public void RefreshType(Type type)
        {
            if (type == null)
            {
                return;
            }

            List<string> previousGuids = null;
            if (_guidsByType.TryGetValue(type, out List<string> existingGuids))
            {
                previousGuids = new List<string>(existingGuids);
            }

            using PooledResource<List<string>> guidBuffer = Buffers<string>.GetList(
                0,
                out List<string> refreshedGuids
            );
            HashSet<string> observed = new(StringComparer.OrdinalIgnoreCase);

            string[] locatedGuids = AssetDatabase.FindAssets($"t:{type.Name}");
            foreach (string guid in locatedGuids)
            {
                if (string.IsNullOrWhiteSpace(guid))
                {
                    continue;
                }

                AssetMetadata metadata = BuildMetadata(type, guid);
                if (metadata == null)
                {
                    continue;
                }

                IndexMetadata(metadata);
                refreshedGuids.Add(guid);
                observed.Add(guid);
            }

            refreshedGuids.Sort(CompareByDisplayName);
            _guidsByType[type] = new List<string>(refreshedGuids);

            if (previousGuids == null)
            {
                return;
            }

            foreach (string guid in previousGuids)
            {
                if (!observed.Contains(guid))
                {
                    Remove(guid);
                }
            }
        }

        public void Remove(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return;
            }

            if (_entries.TryGetValue(guid, out AssetMetadata existing))
            {
                RemoveGuidFromTypeList(existing.AssetType, guid);
                _entries.Remove(guid);
                if (!string.IsNullOrWhiteSpace(existing.Path))
                {
                    _guidByPath.Remove(existing.Path);
                }
            }
        }

        private AssetMetadata BuildMetadata(Type expectedType, string guid)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (asset == null)
            {
                return null;
            }

            Type actualType = asset.GetType();
            if (expectedType != null && actualType != expectedType)
            {
                return null;
            }

            return CreateMetadata(actualType, guid, assetPath, asset);
        }

        private AssetMetadata CreateMetadata(
            Type assetType,
            string guid,
            string assetPath,
            ScriptableObject asset
        )
        {
            if (assetType == null || string.IsNullOrWhiteSpace(guid) || asset == null)
            {
                return null;
            }

            string[] labels = AssetDatabase.GetLabels(asset);

            AssetMetadata metadata = new()
            {
                Guid = guid,
                Path = assetPath,
                AssetType = assetType,
                TypeFullName = assetType.FullName,
                DisplayName = asset.name,
                Labels = labels ?? Array.Empty<string>(),
                LastIndexedUtc = DateTime.UtcNow,
            };

            return metadata;
        }

        private void IndexMetadata(AssetMetadata metadata)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.Guid))
            {
                return;
            }

            _entries[metadata.Guid] = metadata;

            if (!string.IsNullOrWhiteSpace(metadata.Path))
            {
                _guidByPath[metadata.Path] = metadata.Guid;
            }
        }

        private int CompareByDisplayName(string leftGuid, string rightGuid)
        {
            if (!_entries.TryGetValue(leftGuid, out AssetMetadata left))
            {
                return 0;
            }

            if (!_entries.TryGetValue(rightGuid, out AssetMetadata right))
            {
                return 0;
            }

            int nameComparison = string.Compare(
                left.DisplayName,
                right.DisplayName,
                StringComparison.OrdinalIgnoreCase
            );

            if (nameComparison != 0)
            {
                return nameComparison;
            }

            return string.Compare(left.Guid, right.Guid, StringComparison.OrdinalIgnoreCase);
        }

        private void RemoveGuidFromTypeList(Type type, string guid)
        {
            if (type == null || string.IsNullOrWhiteSpace(guid))
            {
                return;
            }

            if (_guidsByType.TryGetValue(type, out List<string> list))
            {
                list.Remove(guid);
            }
        }
    }
#endif
}
