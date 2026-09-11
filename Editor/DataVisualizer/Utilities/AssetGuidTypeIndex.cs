namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using UnityEditor;
    using UnityEngine;

    public sealed class AssetGuidTypeIndex
    {
        private const double DefaultSliceMilliseconds = 4d;
        private const string ProjectAssetsPrefix = "Assets/";
        private const string AssetExtension = ".asset";

        public event Action IndexCompleted;

        public static AssetGuidTypeIndex Shared { get; } = new();

        public bool IsComplete => _state == AssetGuidTypeIndexState.Complete;

        private readonly Queue<string> _pendingAssetPaths = new();
        private readonly Dictionary<string, (Type Type, string Guid)> _assetsByPath = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<Type, HashSet<string>> _guidsByType = new();

        private AssetGuidTypeIndexState _state;

        public AssetGuidTypeIndex()
        {
            _state = AssetGuidTypeIndexState.Idle;
        }

        private static bool IsProjectAssetPath(string path)
        {
            // Unity AssetDatabase paths use forward slashes on every supported platform.
            return path?.StartsWith(ProjectAssetsPrefix, StringComparison.OrdinalIgnoreCase) == true
                && path.EndsWith(AssetExtension, StringComparison.OrdinalIgnoreCase);
        }

        public string[] GetKnownGuids(Type type)
        {
            if (type == null || !_guidsByType.TryGetValue(type, out HashSet<string> guids))
            {
                return Array.Empty<string>();
            }

            string[] snapshot = new string[guids.Count];
            guids.CopyTo(snapshot);
            return snapshot;
        }

        public void EnsureStarted()
        {
            if (_state != AssetGuidTypeIndexState.Idle)
            {
                return;
            }

            TransitionTo(AssetGuidTypeIndexState.WaitingForPathSnapshot);
        }

        public void Rebuild()
        {
            Cancel();
            EnsureStarted();
        }

        public void Cancel()
        {
            _pendingAssetPaths.Clear();
            _assetsByPath.Clear();
            _guidsByType.Clear();
            TransitionTo(AssetGuidTypeIndexState.Idle);
        }

        public bool ProcessPendingSlice(double budgetMilliseconds)
        {
            EnsureStarted();
            return RunStateMachine(budgetMilliseconds);
        }

        public bool ApplyAssetChanges(
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

        private bool RunStateMachine(double budgetMilliseconds)
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

        private bool CapturePathSnapshot()
        {
            CaptureAssetPaths();
            if (_pendingAssetPaths.Count == 0)
            {
                TransitionTo(AssetGuidTypeIndexState.Complete);
                return true;
            }

            TransitionTo(AssetGuidTypeIndexState.Classifying);
            return false;
        }

        private bool ProcessClassificationSlice(double budgetMilliseconds)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            do
            {
                IndexPath(_pendingAssetPaths.Dequeue());
            } while (
                0 < _pendingAssetPaths.Count
                && stopwatch.Elapsed.TotalMilliseconds < budgetMilliseconds
            );

            if (0 < _pendingAssetPaths.Count)
            {
                return false;
            }

            TransitionTo(AssetGuidTypeIndexState.Complete);
            return true;
        }

        private void ProcessEditorUpdate()
        {
            ProcessPendingSlice(DefaultSliceMilliseconds);
        }

        private void CaptureAssetPaths()
        {
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (IsProjectAssetPath(path))
                {
                    _pendingAssetPaths.Enqueue(path);
                }
            }
        }

        private bool IndexPaths(IReadOnlyList<string> paths)
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

        private bool RemovePaths(IReadOnlyList<string> paths)
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

        private bool IndexPath(string path)
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

            if (!_guidsByType.TryGetValue(type, out HashSet<string> typeGuids))
            {
                typeGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _guidsByType[type] = typeGuids;
            }

            typeGuids.Add(guid);
            _assetsByPath[path] = (type, guid);
            return true;
        }

        private bool RemovePath(string path)
        {
            if (
                string.IsNullOrWhiteSpace(path)
                || !_assetsByPath.TryGetValue(path, out (Type Type, string Guid) indexedAsset)
            )
            {
                return false;
            }

            _assetsByPath.Remove(path);
            if (_guidsByType.TryGetValue(indexedAsset.Type, out HashSet<string> typeGuids))
            {
                typeGuids.Remove(indexedAsset.Guid);
                if (typeGuids.Count == 0)
                {
                    _guidsByType.Remove(indexedAsset.Type);
                }
            }

            return true;
        }

        private void TransitionTo(AssetGuidTypeIndexState nextState)
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
