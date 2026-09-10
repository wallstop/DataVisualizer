namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using UnityEditor;
    using UnityEngine;

    public static class AssetGuidTypeIndex
    {
        private const double DefaultSliceMilliseconds = 4d;
        private const int IdleState = 0;
        private const int WaitingForPathSnapshotState = 1;
        private const int ClassifyingState = 2;
        private const int CompleteState = 3;

        private static readonly Queue<string> PendingAssetPaths = new();
        private static readonly Dictionary<string, (Type Type, string Guid)> AssetsByPath = new(
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly Dictionary<Type, HashSet<string>> GuidsByType = new();

        private static int _state;

        public static event Action IndexCompleted;

        public static bool IsComplete => _state == CompleteState;

        public static string[] GetKnownGuids(Type type)
        {
            if (type == null || !GuidsByType.TryGetValue(type, out HashSet<string> guids))
            {
                return Array.Empty<string>();
            }

            string[] snapshot = new string[guids.Count];
            guids.CopyTo(snapshot);
            return snapshot;
        }

        public static void EnsureStarted()
        {
            if (_state != IdleState)
            {
                return;
            }

            _state = WaitingForPathSnapshotState;
            EditorApplication.update -= ProcessEditorUpdate;
            EditorApplication.update += ProcessEditorUpdate;
        }

        public static void Rebuild()
        {
            Cancel();
            EnsureStarted();
        }

        public static void Cancel()
        {
            EditorApplication.update -= ProcessEditorUpdate;
            PendingAssetPaths.Clear();
            AssetsByPath.Clear();
            GuidsByType.Clear();
            _state = IdleState;
        }

        public static bool ProcessPendingSlice(double budgetMilliseconds)
        {
            EnsureStarted();
            if (_state == WaitingForPathSnapshotState)
            {
                CaptureAssetPaths();
                return false;
            }

            if (_state == CompleteState)
            {
                return true;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            do
            {
                IndexPath(PendingAssetPaths.Dequeue());
            } while (
                0 < PendingAssetPaths.Count
                && stopwatch.Elapsed.TotalMilliseconds < budgetMilliseconds
            );

            if (0 < PendingAssetPaths.Count)
            {
                return false;
            }

            CompleteIndex();
            return true;
        }

        public static bool ApplyAssetChanges(
            IReadOnlyList<string> importedAssets,
            IReadOnlyList<string> deletedAssets,
            IReadOnlyList<string> movedAssets,
            IReadOnlyList<string> movedFromAssetPaths
        )
        {
            if (_state == IdleState)
            {
                return false;
            }

            bool changed = RemovePaths(deletedAssets) | RemovePaths(movedFromAssetPaths);
            changed |= IndexPaths(importedAssets);
            changed |= IndexPaths(movedAssets);
            return changed;
        }

        private static void ProcessEditorUpdate()
        {
            ProcessPendingSlice(DefaultSliceMilliseconds);
        }

        private static void CaptureAssetPaths()
        {
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (IsProjectAssetPath(path))
                {
                    PendingAssetPaths.Enqueue(path);
                }
            }

            _state = ClassifyingState;
            if (PendingAssetPaths.Count == 0)
            {
                CompleteIndex();
            }
        }

        private static bool IndexPaths(IReadOnlyList<string> paths)
        {
            if (paths == null)
            {
                return false;
            }

            bool changed = false;
            for (int index = 0; index < paths.Count; index++)
            {
                string path = paths[index];
                if (IsProjectAssetPath(path))
                {
                    changed |= IndexPath(path);
                }
            }

            return changed;
        }

        private static bool RemovePaths(IReadOnlyList<string> paths)
        {
            if (paths == null)
            {
                return false;
            }

            bool changed = false;
            for (int index = 0; index < paths.Count; index++)
            {
                changed |= RemovePath(paths[index]);
            }

            return changed;
        }

        private static bool IndexPath(string path)
        {
            bool changed = RemovePath(path);
            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type))
            {
                return changed;
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrWhiteSpace(guid))
            {
                return changed;
            }

            if (!GuidsByType.TryGetValue(type, out HashSet<string> typeGuids))
            {
                typeGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                GuidsByType[type] = typeGuids;
            }

            typeGuids.Add(guid);
            AssetsByPath[path] = (type, guid);
            return true;
        }

        private static bool RemovePath(string path)
        {
            if (
                string.IsNullOrWhiteSpace(path)
                || !AssetsByPath.TryGetValue(path, out (Type Type, string Guid) indexedAsset)
            )
            {
                return false;
            }

            AssetsByPath.Remove(path);
            if (GuidsByType.TryGetValue(indexedAsset.Type, out HashSet<string> typeGuids))
            {
                typeGuids.Remove(indexedAsset.Guid);
                if (typeGuids.Count == 0)
                {
                    GuidsByType.Remove(indexedAsset.Type);
                }
            }

            return true;
        }

        private static bool IsProjectAssetPath(string path)
        {
            return path?.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) == true
                && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
        }

        private static void CompleteIndex()
        {
            _state = CompleteState;
            EditorApplication.update -= ProcessEditorUpdate;
            IndexCompleted?.Invoke();
        }
    }
#endif
}
