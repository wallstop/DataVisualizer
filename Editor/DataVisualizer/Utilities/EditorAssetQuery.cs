namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Data;
    using Search;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Read-only, main-thread query over exact ScriptableObject main-asset types.</summary>
    public sealed class EditorAssetQuery : IDisposable
    {
        public bool IsComplete { get; private set; }
        public bool IsCancelled { get; private set; }
        public bool IsWaitingForEditor => EditorApplication.isPlayingOrWillChangePlaymode || AssetGuidDiscovery.IsAssetDatabaseBusy();
        public int ScannedCount => _seen.Count;
        public int ResultCount => _results.Count;
        public double LongestSliceMilliseconds { get; private set; }
        public int SliceOverrunCount { get; private set; }

        private readonly AssetGuidTypeIndex _index = new(automaticUpdates: false);
        private readonly Type[] _types;
        private readonly string[] _terms;
        private readonly TypeLabelFilterConfig _labels;
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly List<EditorAssetRecord> _results = new();
        private readonly Queue<(string Guid, Type Type)> _pending = new();
        private readonly HashSet<string> _queued = new(StringComparer.Ordinal);

        public EditorAssetQuery(IReadOnlyList<Type> types, string text = null, TypeLabelFilterConfig labels = null)
        {
            if (types == null || types.Count == 0)
            {
                throw new ArgumentException("Specify at least one exact ScriptableObject type.", nameof(types));
            }
            _types = new Type[types.Count];
            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];
                if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
                {
                    throw new ArgumentException("Scope contains an unsupported type.", nameof(types));
                }
                _types[i] = type;
            }
            _terms = string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            _labels = labels?.Clone();
        }

        /// <summary>Returns a copied page of currently available results. Incomplete pages are not totals.</summary>
        public EditorAssetRecord[] GetPage(int offset, int count)
        {
            if (offset < 0 || count < 0 || 1000 < count)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Use a nonnegative offset and page size from 0 to 1000.");
            }
            int length = Math.Min(count, Math.Max(0, _results.Count - offset));
            EditorAssetRecord[] page = new EditorAssetRecord[length];
            if (0 < length)
            {
                _results.CopyTo(offset, page, 0, length);
            }
            return page;
        }

        /// <summary>Advances discovery and extraction. Indivisible Unity calls can exceed the budget.</summary>
        public void Advance(double budgetMilliseconds = 4)
        {
            if (double.IsNaN(budgetMilliseconds) || double.IsInfinity(budgetMilliseconds) || budgetMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            }
            if (IsComplete || IsCancelled || IsWaitingForEditor)
            {
                return;
            }
            Stopwatch clock = Stopwatch.StartNew();
            bool indexComplete = _index.ProcessPendingSlice(budgetMilliseconds);
            foreach (Type type in _types)
            {
                foreach (string guid in _index.GetKnownGuids(type))
                {
                    if (_queued.Add(guid))
                    {
                        _pending.Enqueue((guid, type));
                    }
                }
            }
            while (0 < _pending.Count && (clock.Elapsed.TotalMilliseconds < budgetMilliseconds || budgetMilliseconds == 0))
            {
                var candidate = _pending.Dequeue();
                _seen.Add(candidate.Guid);
                ReadCandidate(candidate.Guid, candidate.Type);
                if (budgetMilliseconds == 0)
                {
                    break;
                }
            }
            IsComplete = indexComplete && _pending.Count == 0;
            double elapsed = clock.Elapsed.TotalMilliseconds;
            LongestSliceMilliseconds = Math.Max(LongestSliceMilliseconds, elapsed);
            if (budgetMilliseconds < elapsed)
            {
                SliceOverrunCount++;
            }
        }

        public void Dispose()
        {
            if (IsCancelled)
            {
                return;
            }
            IsCancelled = !IsComplete;
            _index.Cancel();
            _pending.Clear();
        }

        private void ReadCandidate(string guid, Type type)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            try
            {
                ScriptableObject asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                if (asset == null || asset.GetType() != type)
                {
                    _results.Add(new EditorAssetRecord(guid, path, null, type.AssemblyQualifiedName, null, "asset_unavailable"));
                    return;
                }
                string[] labels = AssetDatabase.GetLabels(asset);
                if (!LabelFilterEvaluator.Matches(labels, _labels))
                {
                    return;
                }
                if (0 < _terms.Length && new AssetSearchDocument(guid, asset.name, type, asset).MatchOrNull(_terms) == null)
                {
                    return;
                }
                _results.Add(new EditorAssetRecord(guid, path, asset.name, type.AssemblyQualifiedName, labels));
            }
            catch (Exception error)
            {
                _results.Add(new EditorAssetRecord(guid, path, null, type.AssemblyQualifiedName, null, "read_failed: " + error.Message));
            }
        }
    }
}
