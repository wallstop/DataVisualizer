namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;

    // The owning type index controls invalidation, suspension and reset.
    public sealed class AssetMetadataStore
    {
        public int Count => _byGuid.Count;
        public int Version { get; private set; }

        private readonly Dictionary<string, AssetMetadataRecord> _byGuid = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _guidByPath = new(StringComparer.OrdinalIgnoreCase);

        public bool TryGet(string guid, out AssetMetadataRecord record)
        {
            if (!string.IsNullOrEmpty(guid) && _byGuid.TryGetValue(guid, out AssetMetadataRecord found))
            {
                record = found;
                return true;
            }
            record = null;
            return false;
        }

        public bool Upsert(AssetMetadataRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            if (_byGuid.TryGetValue(record.Guid, out AssetMetadataRecord previous))
            {
                if (ReferenceEquals(previous, record))
                {
                    return false;
                }
                _guidByPath.Remove(previous.Path);
            }
            if (!string.IsNullOrEmpty(record.Path))
            {
                if (_guidByPath.TryGetValue(record.Path, out string previousGuid) && !string.Equals(previousGuid, record.Guid, StringComparison.Ordinal))
                {
                    _byGuid.Remove(previousGuid);
                }
                _guidByPath[record.Path] = record.Guid;
            }
            _byGuid[record.Guid] = record;
            Version++;
            return true;
        }

        public bool Remove(string guid)
        {
            if (!TryGet(guid, out AssetMetadataRecord record))
            {
                return false;
            }
            _byGuid.Remove(guid);
            _guidByPath.Remove(record.Path);
            Version++;
            return true;
        }

        public bool RemovePath(string path)
        {
            return !string.IsNullOrEmpty(path) && _guidByPath.TryGetValue(path, out string guid) && Remove(guid);
        }

        public bool Clear()
        {
            if (_byGuid.Count == 0)
            {
                return false;
            }
            _byGuid.Clear();
            _guidByPath.Clear();
            Version++;
            return true;
        }
    }
}
