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
        private const string ProjectAssetsPrefix = "Assets/";
        private const string AssetExtension = ".asset";

        private static readonly Queue<string> PendingAssetPaths = new();
        private static readonly Dictionary<string, (Type Type, string Guid)> AssetsByPath = new(
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly Dictionary<Type, HashSet<string>> GuidsByType = new();

        private static AssetGuidTypeIndexState _state;

        public static event Action IndexCompleted;

        public static bool IsComplete => _state == AssetGuidTypeIndexState.Complete;

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
            if (_state != AssetGuidTypeIndexState.Idle)
            {
                return;
            }

            TransitionTo(AssetGuidTypeIndexState.WaitingForPathSnapshot);
        }

        public static void Rebuild()
        {
            Cancel();
            EnsureStarted();
        }

        public static void Cancel()
        {
            PendingAssetPaths.Clear();
            AssetsByPath.Clear();
            GuidsByType.Clear();
            TransitionTo(AssetGuidTypeIndexState.Idle);
        }

        public static bool ProcessPendingSlice(double budgetMilliseconds)
        {
            EnsureStarted();
            return RunStateMachine(budgetMilliseconds);
        }

        private static bool RunStateMachine(double budgetMilliseconds)
        {
            switch (_state)
            {
                case AssetGuidTypeIndexState.WaitingForPathSnapshot:
                    return CapturePathSnapshot();
                case AssetGuidTypeIndexState.Classifying:
                    return ProcessClassificationSlice(budgetMilliseconds);
                case AssetGuidTypeIndexState.Complete:
                    return true;
                case AssetGuidTypeIndexState.Idle:
                default:
                    return false;
            }
        }

        private static bool CapturePathSnapshot()
        {
            CaptureAssetPaths();
            if (PendingAssetPaths.Count == 0)
            {
                TransitionTo(AssetGuidTypeIndexState.Complete);
                return true;
            }

            TransitionTo(AssetGuidTypeIndexState.Classifying);
            return false;
        }

        private static bool ProcessClassificationSlice(double budgetMilliseconds)
        {
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

            TransitionTo(AssetGuidTypeIndexState.Complete);
            return true;
        }

        public static bool ApplyAssetChanges(
            IReadOnlyList<string> importedAssets,
            IReadOnlyList<string> deletedAssets,
            IReadOnlyList<string> movedAssets,
            IReadOnlyList<string> movedFromAssetPaths
        )
        {
            if (_state == AssetGuidTypeIndexState.Idle)
            {
                return false;
            }

            bool deletedPathsRemoved = RemovePaths(deletedAssets);
            bool movedPathsRemoved = RemovePaths(movedFromAssetPaths);
            bool importedPathsIndexed = IndexPaths(importedAssets);
            bool movedPathsIndexed = IndexPaths(movedAssets);
            return deletedPathsRemoved
                || movedPathsRemoved
                || importedPathsIndexed
                || movedPathsIndexed;
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
                    bool pathIndexed = IndexPath(path);
                    if (pathIndexed)
                    {
                        changed = true;
                    }
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
                bool pathRemoved = RemovePath(paths[index]);
                if (pathRemoved)
                {
                    changed = true;
                }
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
            // Unity AssetDatabase paths use forward slashes on every supported platform.
            return path?.StartsWith(ProjectAssetsPrefix, StringComparison.OrdinalIgnoreCase) == true
                && path.EndsWith(AssetExtension, StringComparison.OrdinalIgnoreCase);
        }

        private static void TransitionTo(AssetGuidTypeIndexState nextState)
        {
            EditorApplication.update -= ProcessEditorUpdate;
            bool stateChanged = _state != nextState;
            _state = nextState;

            bool shouldProcess =
                nextState == AssetGuidTypeIndexState.WaitingForPathSnapshot
                || nextState == AssetGuidTypeIndexState.Classifying;
            if (shouldProcess)
            {
                EditorApplication.update += ProcessEditorUpdate;
                return;
            }

            if (stateChanged && nextState == AssetGuidTypeIndexState.Complete)
            {
                IndexCompleted?.Invoke();
            }
        }
    }
#endif
}
