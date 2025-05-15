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
            if (deletedAssets.Length <= 0)
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
            if (deletedAssets.Any(asset => IsRelevantAsset(relevantTypes, asset)))
            {
                EditorApplication.delayCall += DataVisualizer.SignalRefresh;
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
