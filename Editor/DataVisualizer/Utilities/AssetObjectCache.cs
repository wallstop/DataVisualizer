namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    // Owns mutations of the search list; the window retains its legacy read handle.
    public sealed class AssetObjectCache
    {
        public int Count => _byGuid.Count;

        private readonly List<ScriptableObject> _ordered;
        private readonly Dictionary<string, ScriptableObject> _byGuid = new(StringComparer.Ordinal);
        private readonly Dictionary<ScriptableObject, (string Name, string Type)> _keys = new();

        public AssetObjectCache(List<ScriptableObject> ordered)
        {
            _ordered = ordered ?? throw new ArgumentNullException(nameof(ordered));
            _ordered.Clear();
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
            _ordered.Clear();
            _byGuid.Clear();
            _keys.Clear();
        }

        public int ResetRetainingRows(IReadOnlyList<ScriptableObject> rows, Func<Type, bool> isRelevant)
        {
            HashSet<ScriptableObject> retained = new(rows);
            List<(string Guid, ScriptableObject Value, (string Name, string Type) Key)> entries = new();
            foreach (KeyValuePair<string, ScriptableObject> entry in _byGuid)
            {
                if (retained.Contains(entry.Value) && isRelevant(entry.Value.GetType()))
                    entries.Add((entry.Key, entry.Value, _keys[entry.Value]));
            }
            Clear();
            foreach (var entry in entries)
            {
                _byGuid.Add(entry.Guid, entry.Value);
                _ordered.Add(entry.Value);
                _keys.Add(entry.Value, entry.Key);
            }
            return entries.Count;
        }

        public bool Remove(string guid)
        {
            if (string.IsNullOrEmpty(guid) || !_byGuid.TryGetValue(guid, out ScriptableObject previous))
            {
                return false;
            }

            // A destroyed Unity wrapper still occupies managed collections.
            _ordered.Remove(previous);
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
            bool removed = Remove(guid);
            if (value == null)
            {
                return removed;
            }

            _byGuid[guid] = value;
            _keys[value] = (value.name, value.GetType().FullName);
            if (!sorted)
            {
                _ordered.Add(value);
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
            return true;
        }

        public void Sort()
        {
            _ordered.Sort(Compare);
        }

        private int Compare(ScriptableObject first, ScriptableObject second)
        {
            // Snapshot keys keep order valid while other imported objects await reconciliation.
            (string Name, string Type) left = _keys[first];
            (string Name, string Type) right = _keys[second];
            int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            return name != 0 ? name : string.Compare(left.Type, right.Type, StringComparison.Ordinal);
        }
    }
}
