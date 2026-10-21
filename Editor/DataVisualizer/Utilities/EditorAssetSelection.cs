namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Session selection by GUID. Hidden and deleted targets remain explicit until cleared.</summary>
    [Serializable]
    public sealed class EditorAssetSelection
    {
        public int Count => assetGuids.Count;
        public string TypeIdentity => typeIdentity;
        public bool HasSelectionIntent => hasSelectionIntent || 0 < assetGuids.Count;

        [SerializeField]
        private string typeIdentity;

        [SerializeField]
        private List<string> assetGuids = new();

        [SerializeField]
        private bool hasSelectionIntent;

        [NonSerialized]
        private HashSet<string> _lookup;

        private static string GuidOrEmpty(ScriptableObject asset, AssetObjectCache cache)
        {
            return asset == null ? string.Empty : cache?.GetGuidOrNull(asset) ?? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
        }

        /// <summary>Replaces explicit intent without resolving assets. Returns false when type, ordered GUIDs and intent are unchanged.</summary>
        public bool ReplaceGuids(Type type, IReadOnlyList<string> guids)
        {
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
            {
                throw new ArgumentException("Specify a concrete exact ScriptableObject type.", nameof(type));
            }
            if (guids == null)
            {
                throw new ArgumentNullException(nameof(guids));
            }
            List<string> ordered = new(guids.Count);
            HashSet<string> lookup = new(StringComparer.Ordinal);
            for (int i = 0; i < guids.Count; i++)
            {
                string guid = guids[i];
                if (guid == null || guid.Length != 32 || !Guid.TryParseExact(guid, "N", out _))
                {
                    throw new ArgumentException("Selection GUIDs must be 32 hex digits.", nameof(guids));
                }
                string canonical = guid.ToLowerInvariant();
                if (lookup.Add(canonical))
                {
                    ordered.Add(canonical);
                }
            }
            string identity = type.AssemblyQualifiedName;
            bool changed = !hasSelectionIntent || !string.Equals(typeIdentity, identity, StringComparison.Ordinal) || assetGuids.Count != ordered.Count;
            if (!changed)
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (!string.Equals(assetGuids[i], ordered[i], StringComparison.Ordinal))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (!changed)
            {
                return false;
            }
            typeIdentity = identity;
            assetGuids = ordered;
            hasSelectionIntent = true;
            _lookup = lookup;
            return true;
        }

        /// <summary>Copies all explicit GUIDs, including hidden or deleted targets, without resolving assets.</summary>
        public string[] CopyGuids()
        {
            return assetGuids.ToArray();
        }

        public void SetType(Type type)
        {
            string identity = type?.AssemblyQualifiedName;
            if (!string.Equals(typeIdentity, identity, StringComparison.Ordinal))
            {
                typeIdentity = identity;
                Clear();
                hasSelectionIntent = false;
            }
        }

        public void Clear()
        {
            hasSelectionIntent = true;
            assetGuids.Clear();
            _lookup?.Clear();
        }

        public bool Contains(ScriptableObject asset, AssetObjectCache cache = null)
        {
            EnsureLookup();
            return _lookup.Contains(GuidOrEmpty(asset, cache));
        }

        public void SetSingle(ScriptableObject asset, AssetObjectCache cache = null)
        {
            SetType(asset == null ? null : asset.GetType());
            Clear();
            string guid = GuidOrEmpty(asset, cache);
            if (!string.IsNullOrEmpty(guid))
            {
                assetGuids.Add(guid);
                _lookup?.Add(guid);
            }
        }

        public void ReplaceVisible(IReadOnlyList<ScriptableObject> visible, IEnumerable<object> selected, AssetObjectCache cache = null)
        {
            hasSelectionIntent = true;
            EnsureLookup();
            for (int i = 0; i < visible.Count; i++)
            {
                _lookup.Remove(GuidOrEmpty(visible[i], cache));
            }
            foreach (object item in selected)
            {
                if (item is ScriptableObject asset && asset != null && string.Equals(asset.GetType().AssemblyQualifiedName, typeIdentity, StringComparison.Ordinal))
                {
                    string guid = GuidOrEmpty(asset, cache);
                    if (!string.IsNullOrEmpty(guid))
                    {
                        _lookup.Add(guid);
                    }
                }
            }
            assetGuids.Clear();
            assetGuids.AddRange(_lookup);
            assetGuids.Sort(StringComparer.Ordinal);
        }

        public int[] GetVisibleIndices(IReadOnlyList<ScriptableObject> visible, AssetObjectCache cache = null)
        {
            List<int> indices = new();
            for (int i = 0; i < visible.Count; i++)
            {
                if (Contains(visible[i], cache))
                {
                    indices.Add(i);
                }
            }
            return indices.ToArray();
        }

        public string[] CopyGuids(IReadOnlyList<ScriptableObject> canonicalOrder, AssetObjectCache cache = null)
        {
            HashSet<string> remaining = new(assetGuids, StringComparer.Ordinal);
            List<string> ordered = new();
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                string guid = GuidOrEmpty(canonicalOrder[i], cache);
                if (remaining.Remove(guid))
                {
                    ordered.Add(guid);
                }
            }
            foreach (string guid in assetGuids)
            {
                if (remaining.Remove(guid))
                {
                    ordered.Add(guid);
                }
            }
            return ordered.ToArray();
        }

        private void EnsureLookup()
        {
            if (_lookup == null)
            {
                _lookup = new HashSet<string>(assetGuids, StringComparer.Ordinal);
            }
        }
    }
}
