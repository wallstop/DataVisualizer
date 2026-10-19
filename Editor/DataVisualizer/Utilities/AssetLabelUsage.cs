namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    // Tracks labels for the active type's loaded rows, with explicit invalidation on edits.
    public sealed class AssetLabelUsage
    {
        private readonly Dictionary<ScriptableObject, string[]> _labels = new();
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        private static bool AreEqual(string[] first, string[] second)
        {
            if (first.Length != second.Length)
            {
                return false;
            }

            for (int index = 0; index < first.Length; index++)
            {
                if (!string.Equals(first[index], second[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public void Clear()
        {
            _labels.Clear();
            _counts.Clear();
        }

        public void CopyNamesTo(List<string> destination)
        {
            destination.Clear();
            foreach (string label in _counts.Keys)
            {
                destination.Add(label);
            }
            destination.Sort(StringComparer.Ordinal);
        }

        public IReadOnlyList<string> GetOrRead(ScriptableObject asset)
        {
            if (asset == null)
            {
                return Array.Empty<string>();
            }
            if (_labels.TryGetValue(asset, out string[] labels))
            {
                return labels;
            }

            Update(asset);
            return _labels[asset];
        }

        public bool Remove(ScriptableObject asset)
        {
            // Destroyed wrappers remain valid dictionary keys until explicit removal.
            if (ReferenceEquals(asset, null))
            {
                return false;
            }
            if (!_labels.TryGetValue(asset, out string[] labels))
            {
                return false;
            }

            _labels.Remove(asset);
            foreach (string label in labels)
            {
                int count = _counts[label] - 1;
                if (count == 0)
                {
                    _counts.Remove(label);
                }
                else
                {
                    _counts[label] = count;
                }
            }

            return true;
        }

        public int RemoveDestroyedRows(List<ScriptableObject> rows)
        {
            return rows.RemoveAll(RemoveIfDestroyed);
        }

        public bool Update(ScriptableObject asset)
        {
            if (asset == null)
            {
                return false;
            }
            string[] current = AssetDatabase.GetLabels(asset);
            Array.Sort(current, StringComparer.Ordinal);
            if (_labels.TryGetValue(asset, out string[] previous) && AreEqual(previous, current))
            {
                return false;
            }

            Remove(asset);
            _labels[asset] = current;
            foreach (string label in current)
            {
                _counts.TryGetValue(label, out int count);
                _counts[label] = count + 1;
            }

            return true;
        }

        private bool RemoveIfDestroyed(ScriptableObject asset)
        {
            if (asset != null)
                return false;
            Remove(asset);
            return true;
        }
    }
}
