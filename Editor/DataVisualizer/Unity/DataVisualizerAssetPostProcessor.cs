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
            if (
                importedAssets.Length <= 0
                && deletedAssets.Length <= 0
                && movedAssets.Length <= 0
                && movedFromAssetPaths.Length <= 0
            )
            {
                return;
            }

            DataVisualizer window = DataVisualizer.Instance;
            if (window == null)
            {
                return;
            }

            HashSet<Type> relevantTypes = window
                ._scriptableObjectTypes.SelectMany(x => x.Value)
                .ToHashSet();
            if (
                deletedAssets.Any(IsDeletedAssetPathRelevant)
                || importedAssets.Any(asset => IsRelevantAsset(relevantTypes, asset))
                || movedAssets.Any(asset => IsRelevantAsset(relevantTypes, asset))
                || movedFromAssetPaths.Any(IsDeletedAssetPathRelevant)
            )
            {
                EditorApplication.delayCall += DataVisualizer.SignalRefresh;
            }
        }

        public static bool IsDeletedAssetPathRelevant(string path)
        {
            return path?.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) == true;
        }

        internal static bool IsRelevantAsset(HashSet<Type> relevantTypes, string path)
        {
            if (!IsDeletedAssetPathRelevant(path))
            {
                return false;
            }

            ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (
                so != null
                && (
                    relevantTypes.Contains(so.GetType())
                    || typeof(DataVisualizerSettings).IsAssignableFrom(so.GetType())
                )
            )
            {
                return true;
            }

            return false;
        }
    }
#endif
}
