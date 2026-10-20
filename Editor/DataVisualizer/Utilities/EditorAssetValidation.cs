namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Read-only checks of Unity serialized references and authored IDs within an explicit GUID scope.</summary>
    public sealed class EditorAssetValidation : IDisposable
    {
        public bool IsComplete => _position == _guids.Count;
        public bool IsCancelled { get; private set; }
        public int ProcessedCount => _position;
        public int TotalCount => _guids.Count;
        public IReadOnlyList<AssetValidationFinding> Findings { get; }
        public double LongestSliceMilliseconds { get; private set; }
        public int SliceOverrunCount { get; private set; }

        private readonly List<string> _guids = new();
        private readonly List<AssetValidationFinding> _findings = new();
        private readonly Dictionary<string, (string Guid, string Path)> _firstById = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reportedIds = new(StringComparer.Ordinal);
        private int _position;

        public EditorAssetValidation(IReadOnlyList<string> guids)
        {
            if (guids == null || guids.Count == 0)
            {
                throw new ArgumentException("Specify at least one main-asset GUID.", nameof(guids));
            }
            HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < guids.Count; i++)
            {
                string guid = guids[i];
                if (!IsValidGuid(guid))
                {
                    throw new ArgumentException("Scope contains an invalid asset GUID.", nameof(guids));
                }
                if (unique.Add(guid))
                {
                    _guids.Add(guid.ToLowerInvariant());
                }
            }
            Findings = _findings.AsReadOnly();
        }

        public static bool IsValidGuid(string value)
        {
            if (value == null || value.Length != 32)
            {
                return false;
            }
            foreach (char c in value)
            {
                if (!(('0' <= c && c <= '9') || ('a' <= c && c <= 'f') || ('A' <= c && c <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasReferenceIdentity(SerializedProperty property)
        {
#if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(property.objectReferenceEntityIdValue) != 0;
#else
            return property.objectReferenceInstanceIDValue != 0;
#endif
        }

        public void Advance(double budgetMilliseconds = 4)
        {
            if (double.IsNaN(budgetMilliseconds) || double.IsInfinity(budgetMilliseconds) || budgetMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            }
            if (IsComplete || IsCancelled || EditorApplication.isPlayingOrWillChangePlaymode || AssetGuidDiscovery.IsAssetDatabaseBusy())
            {
                return;
            }
            Stopwatch clock = Stopwatch.StartNew();
            do
            {
                ValidateAsset(_guids[_position++]);
            } while (_position < _guids.Count && clock.Elapsed.TotalMilliseconds < budgetMilliseconds);
            double elapsed = clock.Elapsed.TotalMilliseconds;
            LongestSliceMilliseconds = Math.Max(LongestSliceMilliseconds, elapsed);
            if (budgetMilliseconds < elapsed)
            {
                SliceOverrunCount++;
            }
        }

        public void Dispose()
        {
            IsCancelled = !IsComplete;
            _firstById.Clear();
            _reportedIds.Clear();
        }

        private void ValidateAsset(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            try
            {
                ScriptableObject asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                if (asset == null)
                {
                    _findings.Add(new AssetValidationFinding("asset_unavailable", guid, path, null, "Expected a readable ScriptableObject main asset."));
                    return;
                }
                using (SerializedObject serialized = new(asset))
                {
                    using SerializedProperty property = serialized.GetIterator();
                    HashSet<long> visited = new();
                    bool enterChildren = true;
                    while (property.Next(enterChildren))
                    {
                        enterChildren = property.propertyType != SerializedPropertyType.String;
                        if (property.propertyType == SerializedPropertyType.ManagedReference)
                        {
                            enterChildren = visited.Add(property.managedReferenceId);
                        }
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && HasReferenceIdentity(property))
                        {
                            _findings.Add(new AssetValidationFinding("missing_reference", guid, path, property.propertyPath, "Serialized reference target is missing."));
                        }
                    }
                }
                if (asset is BaseDataObject data)
                {
                    string id = data.Id;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        if (_firstById.TryGetValue(id, out var first))
                        {
                            if (_reportedIds.Add(id))
                            {
                                _findings.Add(new AssetValidationFinding("duplicate_authored_id", first.Guid, first.Path, null, id));
                            }
                            _findings.Add(new AssetValidationFinding("duplicate_authored_id", guid, path, null, id));
                        }
                        else
                        {
                            _firstById.Add(id, (guid, path));
                        }
                    }
                }
            }
            catch (Exception error)
            {
                _findings.Add(new AssetValidationFinding("read_failed", guid, path, null, error.Message));
            }
        }
    }
}
