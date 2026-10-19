namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using UnityEditor;

    // One window owns the service and disposes its editor-update subscription.
    public sealed class AssetChangeRefreshService : IDisposable
    {
        public event Action SliceCompleted;

        private static (string Guid, string Path, Type Type) Resolve(string path, string knownGuid)
        {
            // Resolve retained identity first: the old side of a move is already gone.
            string currentPath = string.IsNullOrEmpty(knownGuid) ? path : AssetDatabase.GUIDToAssetPath(knownGuid);
            if (!string.IsNullOrEmpty(currentPath))
            {
                string guid = AssetDatabase.AssetPathToGUID(currentPath, AssetPathToGUIDOptions.OnlyExistingAssets);
                if (!string.IsNullOrEmpty(guid))
                {
                    return (guid, currentPath, AssetDatabase.GetMainAssetTypeAtPath(currentPath));
                }
            }

            string replacementGuid = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
            return string.IsNullOrEmpty(replacementGuid) ? (null, null, null) : (replacementGuid, path, AssetDatabase.GetMainAssetTypeAtPath(path));
        }

        public int PendingCount => _pending.Count;
        public int ProcessedPathCount { get; private set; }
        public double LongestSliceMilliseconds { get; private set; }

        private readonly Dictionary<string, string> _guidByPath = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _pathByGuid = new(StringComparer.Ordinal);
        private readonly Queue<string> _pending = new();
        private readonly HashSet<string> _pendingPaths = new(StringComparer.Ordinal);
        private readonly Func<Type, bool> _isRelevant;
        private readonly Action<string, string, Type> _publish;
        private readonly Func<string, string, (string Guid, string Path, Type Type)> _resolve;
        private bool _suspended;
        private bool _disposed;

        public AssetChangeRefreshService(
            Func<Type, bool> isRelevant,
            Action<string, string, Type> publish,
            Func<string, string, (string Guid, string Path, Type Type)> resolve = null
        )
        {
            _isRelevant = isRelevant ?? throw new ArgumentNullException(nameof(isRelevant));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _resolve = resolve ?? Resolve;
        }

        public bool Track(string path, string guid)
        {
            if (_disposed || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(guid))
            {
                return false;
            }

            if (_guidByPath.TryGetValue(path, out string previous) && string.Equals(previous, guid, StringComparison.Ordinal))
            {
                return false;
            }

            if (_pathByGuid.TryGetValue(guid, out string oldPath))
            {
                _guidByPath.Remove(oldPath);
            }

            if (previous != null)
            {
                _pathByGuid.Remove(previous);
            }

            _guidByPath[path] = guid;
            _pathByGuid[guid] = path;
            return true;
        }

        public int Enqueue(IReadOnlyList<string> paths)
        {
            if (_disposed || paths == null)
            {
                return 0;
            }

            int added = 0;
            for (int index = 0; index < paths.Count; index++)
            {
                string path = paths[index];
                if (path?.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) == true && _pendingPaths.Add(path))
                {
                    _pending.Enqueue(path);
                    added++;
                }
            }

            Transition();
            return added;
        }

        public int ProcessSlice(double budgetMilliseconds = 4)
        {
            if (_disposed || _suspended || _pending.Count == 0)
            {
                return 0;
            }

            Stopwatch clock = Stopwatch.StartNew();
            int count = 0;
            HashSet<string> published = new(StringComparer.Ordinal);
            do
            {
                string path = _pending.Dequeue();
                _pendingPaths.Remove(path);
                _guidByPath.TryGetValue(path, out string knownGuid);
                (string guid, string currentPath, Type type) = _resolve(path, knownGuid);
                bool relevant = type != null && _isRelevant(type);
                if (knownGuid != null && (!relevant || !string.Equals(guid, knownGuid, StringComparison.Ordinal)))
                {
                    _guidByPath.Remove(path);
                    _pathByGuid.Remove(knownGuid);
                    if (published.Add(knownGuid))
                    {
                        _publish(knownGuid, string.Equals(guid, knownGuid, StringComparison.Ordinal) ? currentPath : null, null);
                    }
                }

                if (relevant)
                {
                    Track(currentPath, guid);
                    if (!string.Equals(currentPath, path, StringComparison.Ordinal))
                    {
                        // The old path can now contain a different newly imported asset.
                        Enqueue(new[] { path });
                    }

                    if (published.Add(guid))
                    {
                        _publish(guid, currentPath, type);
                    }
                }

                count++;
                ProcessedPathCount++;
            } while (0 < _pending.Count && clock.Elapsed.TotalMilliseconds < budgetMilliseconds);

            SliceCompleted?.Invoke();
            LongestSliceMilliseconds = Math.Max(LongestSliceMilliseconds, clock.Elapsed.TotalMilliseconds);
            Transition();
            return count;
        }

        public void SetSuspended(bool suspended)
        {
            _suspended = suspended;
            Transition();
        }

        public void Dispose()
        {
            _disposed = true;
            _pending.Clear();
            _pendingPaths.Clear();
            _guidByPath.Clear();
            _pathByGuid.Clear();
            Transition();
        }

        private void ProcessEditorUpdate()
        {
            if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                ProcessSlice();
            }
        }

        private void Transition()
        {
            EditorApplication.update -= ProcessEditorUpdate;
            if (!_disposed && !_suspended && 0 < _pending.Count)
            {
                EditorApplication.update += ProcessEditorUpdate;
            }
        }
    }
}
