namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Data;

    internal sealed class DataAssetService : IDataAssetService
    {
        private readonly AssetIndex _assetIndex = new AssetIndex();
        private readonly Dictionary<string, DataAssetMetadata> _metadataByGuid = new Dictionary<
            string,
            DataAssetMetadata
        >(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _guidByPath = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly HashSet<Type> _trackedTypes = new HashSet<Type>();
        private bool _indexDirty = true;

        public event Action<DataAssetChangeEventArgs> AssetsChanged;

        public void ConfigureTrackedTypes(IEnumerable<Type> types)
        {
            _trackedTypes.Clear();
            if (types != null)
            {
                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    _trackedTypes.Add(type);
                }
            }

            _indexDirty = true;
        }

        public void MarkDirty()
        {
            _indexDirty = true;
        }

        public void ForceRebuild()
        {
            EnsureIndex(true);
        }

        public int GetAssetCount(Type type)
        {
            EnsureIndex(false);
            if (type == null)
            {
                return 0;
            }

            IReadOnlyList<string> guids = _assetIndex.GetGuidsForType(type);
            if (guids == null)
            {
                return 0;
            }

            return guids.Count;
        }

        public DataAssetPage GetAssetsPage(Type type, int offset, int count)
        {
            EnsureIndex(false);
            if (type == null || count <= 0)
            {
                return new DataAssetPage(type, Array.Empty<DataAssetMetadata>(), 0, 0);
            }

            IReadOnlyList<string> guids = _assetIndex.GetGuidsForType(type);
            if (guids == null || guids.Count == 0)
            {
                return new DataAssetPage(type, Array.Empty<DataAssetMetadata>(), 0, 0);
            }

            int clampedOffset = offset < 0 ? 0 : offset;
            if (clampedOffset >= guids.Count)
            {
                return new DataAssetPage(type, Array.Empty<DataAssetMetadata>(), guids.Count, clampedOffset);
            }

            int available = guids.Count - clampedOffset;
            int clampedCount = count > available ? available : count;

            List<DataAssetMetadata> pageItems = new List<DataAssetMetadata>(clampedCount);
            for (int index = 0; index < clampedCount; index++)
            {
                string guid = guids[clampedOffset + index];
                if (string.IsNullOrWhiteSpace(guid))
                {
                    continue;
                }

                DataAssetMetadata snapshot = GetOrCreateMetadataFromIndex(guid);
                if (snapshot != null)
                {
                    pageItems.Add(snapshot);
                }
            }

            return new DataAssetPage(type, pageItems, guids.Count, clampedOffset);
        }

        public IReadOnlyList<DataAssetMetadata> GetAssetsForType(Type type)
        {
            EnsureIndex(false);
            if (type == null)
            {
                return Array.Empty<DataAssetMetadata>();
            }

            IReadOnlyList<string> guids = _assetIndex.GetGuidsForType(type);
            if (guids == null || guids.Count == 0)
            {
                return Array.Empty<DataAssetMetadata>();
            }

            List<DataAssetMetadata> results = new List<DataAssetMetadata>(guids.Count);
            for (int index = 0; index < guids.Count; index++)
            {
                string guid = guids[index];
                if (string.IsNullOrWhiteSpace(guid))
                {
                    continue;
                }

                DataAssetMetadata metadata = GetOrCreateMetadataFromIndex(guid);
                if (metadata != null)
                {
                    results.Add(metadata);
                }
            }

            return results;
        }

        public IReadOnlyList<string> GetGuidsForType(Type type)
        {
            EnsureIndex(false);
            if (type == null)
            {
                return Array.Empty<string>();
            }

            IReadOnlyList<string> guids = _assetIndex.GetGuidsForType(type);
            if (guids == null || guids.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> copy = new List<string>(guids.Count);
            for (int index = 0; index < guids.Count; index++)
            {
                copy.Add(guids[index]);
            }

            return copy;
        }

        public IEnumerable<DataAssetMetadata> GetAllAssets()
        {
            EnsureIndex(false);
            return _metadataByGuid.Values.ToList();
        }

        public bool TryGetAssetByGuid(string guid, out DataAssetMetadata metadata)
        {
            EnsureIndex(false);
            metadata = null;
            if (string.IsNullOrWhiteSpace(guid))
            {
                return false;
            }

            DataAssetMetadata existing = GetOrCreateMetadataFromIndex(guid);
            if (existing != null)
            {
                metadata = existing;
                return true;
            }

            return false;
        }

        public bool TryGetAssetByPath(string path, out DataAssetMetadata metadata)
        {
            EnsureIndex(false);
            metadata = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalizedPath = path;
            if (
                _guidByPath.TryGetValue(normalizedPath, out string existingGuid)
                && TryGetAssetByGuid(existingGuid, out DataAssetMetadata cached)
            )
            {
                metadata = cached;
                return true;
            }

            if (
                _assetIndex.TryGetMetadataByPath(
                    normalizedPath,
                    out AssetIndex.AssetMetadata indexMetadata
                )
            )
            {
                DataAssetMetadata snapshot = CreateSnapshot(indexMetadata);
                if (snapshot != null)
                {
                    StoreMetadata(snapshot);
                    metadata = snapshot;
                    return true;
                }
            }

            return false;
        }

        public void RefreshAsset(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return;
            }

            _assetIndex.RefreshGuid(guid);
            if (_assetIndex.TryGetMetadata(guid, out AssetIndex.AssetMetadata metadata))
            {
                DataAssetMetadata snapshot = CreateSnapshot(metadata);
                if (snapshot != null)
                {
                    StoreMetadata(snapshot);
                    RaiseAssetsChanged(
                        new DataAssetChangeEventArgs(
                            false,
                            new[] { snapshot.Guid },
                            Array.Empty<string>(),
                            metadata.AssetType
                        )
                    );
                }
            }
            else
            {
                RemoveCachedEntry(guid);
                RaiseAssetsChanged(
                    new DataAssetChangeEventArgs(false, Array.Empty<string>(), new[] { guid }, null)
                );
            }
        }

        public void RefreshType(Type type)
        {
            if (type == null)
            {
                return;
            }

            _assetIndex.RefreshType(type);
            IReadOnlyList<string> guids = _assetIndex.GetGuidsForType(type);
            HashSet<string> refreshedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (guids != null)
            {
                for (int index = 0; index < guids.Count; index++)
                {
                    string guid = guids[index];
                    if (string.IsNullOrWhiteSpace(guid))
                    {
                        continue;
                    }

                    refreshedGuids.Add(guid);
                    DataAssetMetadata metadata = GetOrCreateMetadataFromIndex(guid);
                    if (metadata != null)
                    {
                        StoreMetadata(metadata);
                    }
                }
            }

            List<string> removed = new List<string>();
            List<string> updated = refreshedGuids.ToList();
            List<string> cachedGuids = new List<string>(_metadataByGuid.Keys);
            for (int index = 0; index < cachedGuids.Count; index++)
            {
                string cachedGuid = cachedGuids[index];
                if (string.IsNullOrWhiteSpace(cachedGuid))
                {
                    continue;
                }

                if (
                    _metadataByGuid.TryGetValue(cachedGuid, out DataAssetMetadata cachedMetadata)
                    && cachedMetadata.AssetType == type
                    && !refreshedGuids.Contains(cachedGuid)
                )
                {
                    removed.Add(cachedGuid);
                    RemoveCachedEntry(cachedGuid);
                }
            }

            RaiseAssetsChanged(new DataAssetChangeEventArgs(false, updated, removed, type));
        }

        public void RemoveAsset(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return;
            }

            _assetIndex.Remove(guid);
            RemoveCachedEntry(guid);
            RaiseAssetsChanged(
                new DataAssetChangeEventArgs(false, Array.Empty<string>(), new[] { guid }, null)
            );
        }

        public IReadOnlyCollection<string> EnumerateLabels(Type type)
        {
            IReadOnlyList<DataAssetMetadata> assets = GetAssetsForType(type);
            HashSet<string> labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < assets.Count; index++)
            {
                DataAssetMetadata metadata = assets[index];
                if (metadata == null || metadata.Labels == null)
                {
                    continue;
                }

                for (int labelIndex = 0; labelIndex < metadata.Labels.Count; labelIndex++)
                {
                    string label = metadata.Labels[labelIndex];
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        labels.Add(label);
                    }
                }
            }

            return labels.ToArray();
        }

        private void EnsureIndex(bool forceRebuild)
        {
            if (!_indexDirty && !forceRebuild)
            {
                return;
            }

            if (_trackedTypes.Count == 0)
            {
                _metadataByGuid.Clear();
                _guidByPath.Clear();
                _indexDirty = false;
                return;
            }

            _assetIndex.Rebuild(_trackedTypes);
            RebuildCacheFromIndex();
            _indexDirty = false;
            RaiseAssetsChanged(
                new DataAssetChangeEventArgs(
                    true,
                    _metadataByGuid.Keys.ToArray(),
                    Array.Empty<string>(),
                    null
                )
            );
        }

        private void RebuildCacheFromIndex()
        {
            _metadataByGuid.Clear();
            _guidByPath.Clear();

            foreach (AssetIndex.AssetMetadata metadata in _assetIndex.AllMetadata())
            {
                DataAssetMetadata snapshot = CreateSnapshot(metadata);
                if (snapshot == null)
                {
                    continue;
                }

                StoreMetadata(snapshot);
            }
        }

        private void StoreMetadata(DataAssetMetadata metadata)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.Guid))
            {
                return;
            }

            _metadataByGuid[metadata.Guid] = metadata;
            if (!string.IsNullOrWhiteSpace(metadata.Path))
            {
                _guidByPath[metadata.Path] = metadata.Guid;
            }
        }

        private void RemoveCachedEntry(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return;
            }

            if (_metadataByGuid.TryGetValue(guid, out DataAssetMetadata metadata))
            {
                if (!string.IsNullOrWhiteSpace(metadata.Path))
                {
                    _guidByPath.Remove(metadata.Path);
                }
            }

            _metadataByGuid.Remove(guid);
        }

        private DataAssetMetadata GetOrCreateMetadataFromIndex(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            if (_metadataByGuid.TryGetValue(guid, out DataAssetMetadata existing))
            {
                return existing;
            }

            if (_assetIndex.TryGetMetadata(guid, out AssetIndex.AssetMetadata metadata))
            {
                DataAssetMetadata snapshot = CreateSnapshot(metadata);
                if (snapshot != null)
                {
                    StoreMetadata(snapshot);
                    return snapshot;
                }
            }

            return null;
        }

        private static DataAssetMetadata CreateSnapshot(AssetIndex.AssetMetadata metadata)
        {
            if (metadata == null)
            {
                return null;
            }

            string[] labels =
                metadata.Labels != null ? metadata.Labels.ToArray() : Array.Empty<string>();

            DataAssetMetadata snapshot = new DataAssetMetadata(
                metadata.Guid,
                metadata.Path,
                metadata.AssetType,
                metadata.TypeFullName,
                metadata.DisplayName,
                labels,
                metadata.LastIndexedUtc
            );
            return snapshot;
        }

        private void RaiseAssetsChanged(DataAssetChangeEventArgs args)
        {
            Action<DataAssetChangeEventArgs> handler = AssetsChanged;
            if (handler != null)
            {
                handler.Invoke(args);
            }
        }
    }
}
