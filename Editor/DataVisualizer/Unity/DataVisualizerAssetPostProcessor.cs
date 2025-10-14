namespace WallstopStudios.DataVisualizer.Editor.Unity
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Data;
    using UnityEditor;
    using UnityEngine;

    public sealed class DataVisualizerAssetProcessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            DataVisualizer window = DataVisualizer.Instance;
            if (window == null)
            {
                return;
            }

            HashSet<Type> relevantTypes = window
                ._scriptableObjectTypes.SelectMany(x => x.Value)
                .ToHashSet();

            IReadOnlyList<string> filteredImported = Filter(importedAssets);
            IReadOnlyList<string> filteredDeleted = Filter(deletedAssets);
            IReadOnlyList<string> filteredMoved = Filter(movedAssets);
            IReadOnlyList<string> filteredMovedFrom = Filter(movedFromAssetPaths);

            if (
                filteredImported.Count == 0
                && filteredDeleted.Count == 0
                && filteredMoved.Count == 0
                && filteredMovedFrom.Count == 0
            )
            {
                return;
            }

            EditorApplication.delayCall += Forward;
            return;

            void Forward()
            {
                window.HandleAssetsChanged(
                    filteredImported,
                    filteredDeleted,
                    filteredMoved,
                    filteredMovedFrom
                );
            }

            IReadOnlyList<string> Filter(IReadOnlyList<string> source)
            {
                if (source == null || source.Count == 0)
                {
                    return Array.Empty<string>();
                }

                List<string> relevant = null;
                foreach (string path in source)
                {
                    if (IsPathRelevant(path))
                    {
                        relevant ??= new List<string>();
                        relevant.Add(path);
                    }
                }

                if (relevant != null)
                {
                    return relevant;
                }

                return Array.Empty<string>();
            }

            bool IsPathRelevant(string path)
            {
                if (IsRelevantAsset(relevantTypes, path))
                {
                    return true;
                }

                return window.TryGetMetadataForPath(path, out _);
            }
        }

        internal static bool IsRelevantAsset(HashSet<Type> relevantTypes, string path)
        {
            bool isAsset = path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
            if (!isAsset)
            {
                return false;
            }

            ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (
                so != null
                && (relevantTypes.Contains(so.GetType()) || (so is DataVisualizerSettings))
            )
            {
                return true;
            }

            return false;
        }
    }
#endif
}
