namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Search;

    // Search owns copied documents; loaded objects are only weak lookup hints.
    public sealed class AssetObjectCache
    {
        public int Count => _documents.Count;
        public bool IsSorted => _isSorted;
        public int Version { get; private set; }
        public int ObjectLoadCount { get; private set; }
        public int RetainedObjectCount => _ordered?.Count ?? 0;
        public IReadOnlyList<AssetSearchDocument> Documents => _documents;

        private readonly List<ScriptableObject> _ordered;
        private readonly AssetMetadataStore _metadata;
        private readonly List<AssetSearchDocument> _documents = new();
        private readonly Dictionary<string, WeakReference<ScriptableObject>> _byGuid = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _documentIndexByGuid = new(StringComparer.Ordinal);
        private ConditionalWeakTable<ScriptableObject, AssetSearchDocument> _keys = new();
        private bool _isSorted = true;

        // Retained for callers that explicitly request a strongly owned object list.
        public AssetObjectCache(List<ScriptableObject> ordered)
            : this(ordered, null) { }

        public AssetObjectCache(List<ScriptableObject> ordered, AssetMetadataStore metadata)
        {
            _metadata = metadata;
            _ordered = ordered ?? throw new ArgumentNullException(nameof(ordered));
            _ordered.Clear();
        }

        public AssetObjectCache(AssetMetadataStore metadata)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        public bool Contains(string guid)
        {
            return !string.IsNullOrEmpty(guid) && _documentIndexByGuid.ContainsKey(guid) && (_ordered == null || (TryGetLoaded(guid, out ScriptableObject value) && value != null));
        }

        public bool TryGetLoaded(string guid, out ScriptableObject value)
        {
            if (!string.IsNullOrEmpty(guid) && _byGuid.TryGetValue(guid, out WeakReference<ScriptableObject> reference) && reference.TryGetTarget(out ScriptableObject found))
            {
                value = found;
                return true;
            }
            value = null;
            return false;
        }

        // Resolve only for visible search results or an explicit asset operation.
        public bool TryGet(string guid, out ScriptableObject value)
        {
            if (TryGetLoaded(guid, out ScriptableObject loaded) && (loaded != null || _ordered != null))
            {
                value = loaded;
                return true;
            }
            if (Contains(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    ObjectLoadCount++;
                    ScriptableObject asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                    if (asset != null && _metadata.TryGet(guid, out AssetMetadataRecord record) && asset.GetType() == record.ExactType)
                    {
                        Remember(guid, asset, record.Document);
                        value = asset;
                        return true;
                    }
                }
            }
            value = null;
            return false;
        }

        public bool UseMetadata(string guid, ISet<Type> managedTypes)
        {
            if (_ordered != null || !_metadata.TryGet(guid, out AssetMetadataRecord record) || !managedTypes.Contains(record.ExactType))
            {
                return false;
            }
            PutDocument(record.Document, false);
            return true;
        }

        public bool RememberLoaded(string guid, ScriptableObject asset)
        {
            if (asset == null || !_documentIndexByGuid.TryGetValue(guid, out int index))
            {
                return false;
            }
            Remember(guid, asset, _documents[index]);
            return true;
        }

        public void Clear()
        {
            Version++;
            _ordered?.Clear();
            _documents.Clear();
            _byGuid.Clear();
            _documentIndexByGuid.Clear();
            _keys = new ConditionalWeakTable<ScriptableObject, AssetSearchDocument>();
            _isSorted = true;
        }

        public int ResetRetainingRows(IReadOnlyList<ScriptableObject> rows, Func<Type, bool> isRelevant)
        {
            List<(string Guid, ScriptableObject Value, AssetSearchDocument Document)> entries = new();
            HashSet<string> retained = new(StringComparer.Ordinal);
            for (int index = 0; index < rows.Count; index++)
            {
                ScriptableObject row = rows[index];
                if (!ReferenceEquals(row, null) && _keys.TryGetValue(row, out AssetSearchDocument previous) && isRelevant(row.GetType()) && retained.Add(previous.Guid))
                {
                    AssetSearchDocument document = row != null ? Capture(previous.Guid, row) : previous;
                    entries.Add((previous.Guid, row, document));
                }
            }
            Clear();
            foreach (var entry in entries)
            {
                Remember(entry.Guid, entry.Value, entry.Document);
                _ordered?.Add(entry.Value);
                _documentIndexByGuid.Add(entry.Guid, _documents.Count);
                _documents.Add(entry.Document);
            }
            _isSorted = entries.Count < 2;
            return entries.Count;
        }

        public bool Remove(string guid)
        {
            if (string.IsNullOrEmpty(guid) || !_documentIndexByGuid.TryGetValue(guid, out int index))
            {
                return false;
            }
            Version++;
            _ordered?.RemoveAt(index);
            _documents.RemoveAt(index);
            _documentIndexByGuid.Remove(guid);
            IndexDocumentsFrom(index);
            if (TryGetLoaded(guid, out ScriptableObject previous))
            {
                _keys.Remove(previous);
            }
            _byGuid.Remove(guid);
            return true;
        }

        public bool Upsert(string guid, ScriptableObject value, bool sorted)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }
            if (value == null)
            {
                _metadata?.Remove(guid);
                return Remove(guid);
            }
            AssetSearchDocument document = Capture(guid, value);
            if (sorted)
            {
                Sort();
            }
            if (_documentIndexByGuid.TryGetValue(guid, out int index) && AssetSearchDocument.CompareIdentity(_documents[index], document) == 0)
            {
                Version++;
                _documents[index] = document;
                if (_ordered != null)
                {
                    _ordered[index] = value;
                }
                Remember(guid, value, document);
                return true;
            }
            Remove(guid);
            Remember(guid, value, document);
            PutDocument(document, sorted);
            return true;
        }

        public string GetGuidOrNull(ScriptableObject asset)
        {
            return asset != null && _keys.TryGetValue(asset, out AssetSearchDocument document) ? document.Guid : null;
        }

        public SearchResultMatchInfo Match(ScriptableObject asset, string[] terms)
        {
            return asset != null && _keys.TryGetValue(asset, out AssetSearchDocument document) && _documentIndexByGuid.TryGetValue(document.Guid, out int index)
                ? _documents[index].Match(terms)
                : new SearchResultMatchInfo();
        }

        public void Sort()
        {
            if (_isSorted)
            {
                return;
            }
            Version++;
            _documents.Sort(AssetSearchDocument.CompareIdentity);
            _ordered?.Clear();
            for (int index = 0; index < _documents.Count; index++)
            {
                AssetSearchDocument document = _documents[index];
                _documentIndexByGuid[document.Guid] = index;
                if (_ordered != null && TryGetLoaded(document.Guid, out ScriptableObject asset))
                {
                    _ordered.Add(asset);
                }
            }
            _isSorted = true;
        }

        private AssetSearchDocument Capture(string guid, ScriptableObject value)
        {
            if (_metadata == null)
            {
                return new AssetSearchDocument(guid, value.name, value.GetType(), value);
            }
            AssetMetadataRecord record = new(guid, value);
            _metadata.Upsert(record);
            return record.Document;
        }

        private void PutDocument(AssetSearchDocument document, bool sorted)
        {
            if (_documentIndexByGuid.TryGetValue(document.Guid, out int existing))
            {
                _documents[existing] = document;
                Version++;
                _isSorted = _documents.Count < 2;
                return;
            }
            int low = _documents.Count;
            if (sorted)
            {
                low = 0;
                int high = _documents.Count;
                while (low < high)
                {
                    int middle = (low + high) >> 1;
                    if (AssetSearchDocument.CompareIdentity(_documents[middle], document) <= 0)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }
            }
            _documents.Insert(low, document);
            if (_ordered != null && TryGetLoaded(document.Guid, out ScriptableObject asset))
            {
                _ordered.Insert(low, asset);
            }
            IndexDocumentsFrom(low);
            _isSorted = sorted || _documents.Count < 2;
            Version++;
        }

        private void Remember(string guid, ScriptableObject asset, AssetSearchDocument document)
        {
            if (TryGetLoaded(guid, out ScriptableObject previous) && !ReferenceEquals(previous, asset))
            {
                _keys.Remove(previous);
            }
            _byGuid[guid] = new WeakReference<ScriptableObject>(asset);
            _keys.Remove(asset);
            _keys.Add(asset, document);
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
