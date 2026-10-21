namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Search;

    // Owns mutations of the search list; the window retains its legacy read handle.
    public sealed class AssetObjectCache
    {
        public int Count => _byGuid.Count;
        public int Version { get; private set; }
        public IReadOnlyList<AssetSearchDocument> Documents => _documents;

        private readonly List<ScriptableObject> _ordered;
        private readonly List<AssetSearchDocument> _documents = new();
        private readonly Dictionary<string, ScriptableObject> _byGuid = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _documentIndexByGuid = new(StringComparer.Ordinal);
        private readonly Dictionary<ScriptableObject, AssetSearchDocument> _keys = new();
        private bool _isSorted = true;

        public AssetObjectCache(List<ScriptableObject> ordered)
        {
            _ordered = ordered ?? throw new ArgumentNullException(nameof(ordered));
            _ordered.Clear();
        }

        private static int CompareDocuments(AssetSearchDocument left, AssetSearchDocument right)
        {
            int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            if (name != 0)
            {
                return name;
            }
            int type = string.Compare(left.TypeFullName, right.TypeFullName, StringComparison.Ordinal);
            return type != 0 ? type : string.Compare(left.Guid, right.Guid, StringComparison.Ordinal);
        }

        public bool Contains(string guid)
        {
            return !string.IsNullOrEmpty(guid) && _byGuid.TryGetValue(guid, out ScriptableObject value) && value != null;
        }

        public bool TryGet(string guid, out ScriptableObject value)
        {
            if (!string.IsNullOrEmpty(guid) && _byGuid.TryGetValue(guid, out ScriptableObject found))
            {
                value = found;
                return true;
            }

            value = null;
            return false;
        }

        public void Clear()
        {
            Version++;
            _ordered.Clear();
            _documents.Clear();
            _byGuid.Clear();
            _documentIndexByGuid.Clear();
            _keys.Clear();
            _isSorted = true;
        }

        public int ResetRetainingRows(IReadOnlyList<ScriptableObject> rows, Func<Type, bool> isRelevant)
        {
            HashSet<ScriptableObject> retained = new(rows);
            List<(string Guid, ScriptableObject Value, AssetSearchDocument Key)> entries = new();
            foreach (KeyValuePair<string, ScriptableObject> entry in _byGuid)
            {
                if (retained.Contains(entry.Value) && isRelevant(entry.Value.GetType()))
                {
                    AssetSearchDocument document =
                        entry.Value != null ? new AssetSearchDocument(entry.Key, entry.Value.name, entry.Value.GetType(), entry.Value) : _keys[entry.Value];
                    entries.Add((entry.Key, entry.Value, document));
                }
            }
            Clear();
            foreach (var entry in entries)
            {
                _byGuid.Add(entry.Guid, entry.Value);
                _ordered.Add(entry.Value);
                _keys.Add(entry.Value, entry.Key);
                _documentIndexByGuid.Add(entry.Guid, _documents.Count);
                _documents.Add(entry.Key);
            }
            _isSorted = entries.Count < 2;
            return entries.Count;
        }

        public bool Remove(string guid)
        {
            if (string.IsNullOrEmpty(guid) || !_byGuid.TryGetValue(guid, out ScriptableObject previous))
            {
                return false;
            }

            // A destroyed Unity wrapper still occupies managed collections.
            Version++;
            int index = _documentIndexByGuid[guid];
            _ordered.RemoveAt(index);
            _documents.RemoveAt(index);
            _documentIndexByGuid.Remove(guid);
            IndexDocumentsFrom(index);
            _keys.Remove(previous);
            _byGuid.Remove(guid);
            return true;
        }

        public bool Upsert(string guid, ScriptableObject value, bool sorted)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }
            AssetSearchDocument replacement = value != null ? new AssetSearchDocument(guid, value.name, value.GetType(), value) : null;
            if (value != null && _byGuid.TryGetValue(guid, out ScriptableObject previous) && ReferenceEquals(previous, value) && CompareDocuments(_keys[value], replacement) == 0)
            {
                if (sorted)
                {
                    Sort();
                }
                Version++;
                _keys[value] = replacement;
                _documents[_documentIndexByGuid[guid]] = replacement;
                return true;
            }
            bool removed = Remove(guid);
            if (value == null)
            {
                return removed;
            }

            if (sorted)
            {
                Sort();
            }
            Version++;
            _byGuid[guid] = value;
            _keys[value] = replacement;
            if (!sorted)
            {
                _ordered.Add(value);
                _documentIndexByGuid[guid] = _documents.Count;
                _documents.Add(_keys[value]);
                _isSorted = _documents.Count < 2;
                return true;
            }

            int low = 0;
            int high = _ordered.Count;
            while (low < high)
            {
                int middle = (low + high) >> 1;
                if (Compare(_ordered[middle], value) <= 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            _ordered.Insert(low, value);
            _documents.Insert(low, _keys[value]);
            IndexDocumentsFrom(low);
            return true;
        }

        public string GetGuidOrNull(ScriptableObject asset)
        {
            return asset != null && _keys.TryGetValue(asset, out AssetSearchDocument document) ? document.Guid : null;
        }

        public SearchResultMatchInfo Match(ScriptableObject asset, string[] terms)
        {
            return asset != null && _keys.TryGetValue(asset, out AssetSearchDocument document) ? document.Match(terms) : new SearchResultMatchInfo();
        }

        public void Sort()
        {
            if (_isSorted)
            {
                return;
            }
            Version++;
            _documents.Sort(CompareDocuments);
            _ordered.Clear();
            foreach (AssetSearchDocument document in _documents)
            {
                _documentIndexByGuid[document.Guid] = _ordered.Count;
                _ordered.Add(_byGuid[document.Guid]);
            }
            _isSorted = true;
        }

        private int Compare(ScriptableObject first, ScriptableObject second)
        {
            // Snapshot keys keep order valid while other imported objects await reconciliation.
            AssetSearchDocument left = _keys[first];
            AssetSearchDocument right = _keys[second];
            return CompareDocuments(left, right);
        }

        private void IndexDocumentsFrom(int start)
        {
            for (int index = start; index < _documents.Count; index++)
            {
                _documentIndexByGuid[_documents[index].Guid] = index;
            }
        }
    }
}
