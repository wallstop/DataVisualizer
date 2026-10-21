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
            return first.AsSpan().SequenceEqual(second);
        }

        public void Clear()
        {
            _labels.Clear();
            _counts.Clear();
        }

        public void CopyNamesTo(List<string> destination)
        {
            destination.Clear();
            destination.AddRange(_counts.Keys);
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
            if (!_labels.Remove(asset, out string[] labels))
            {
                return false;
            }

            foreach (string label in labels)
            {
                int count = _counts[label] - 1;
                if (count <= 0)
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
            return RemoveDestroyedRows(rows, null);
        }

        public int RemoveDestroyedRows(List<ScriptableObject> rows, Dictionary<ScriptableObject, int> rowOrderIndex)
        {
            int originalCount = rows.Count;
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < originalCount; readIndex++)
            {
                ScriptableObject asset = rows[readIndex];
                if (asset == null)
                {
                    Remove(asset);
                    if (!ReferenceEquals(asset, null))
                    {
                        rowOrderIndex?.Remove(asset);
                    }
                }
                else
                {
                    if (writeIndex != readIndex)
                    {
                        rows[writeIndex] = asset;
                    }
                    writeIndex++;
                }
            }

            int removedCount = originalCount - writeIndex;
            if (0 < removedCount)
            {
                rows.RemoveRange(writeIndex, removedCount);
            }
            return removedCount;
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
                _counts[label] = _counts.TryGetValue(label, out int count) ? count + 1 : 1;
            }

            return true;
        }
    }
}
