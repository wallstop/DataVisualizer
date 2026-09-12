namespace WallstopStudios.DataVisualizer.Editor.Unity
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using Data;
    using UnityEditor;
    using UnityEngine;
    using Utilities;

    public sealed class DataVisualizerAssetProcessor : AssetPostprocessor
    {
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

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            AssetGuidTypeIndex.Shared.ApplyAssetChanges(
                importedAssets,
                deletedAssets,
                movedAssets,
                movedFromAssetPaths
            );

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

            HashSet<Type> relevantTypes = CollectRelevantTypes(window._scriptableObjectTypes);
            if (
                ContainsRelevantDeletedAssetPath(deletedAssets)
                || ContainsRelevantAsset(relevantTypes, importedAssets)
                || ContainsRelevantAsset(relevantTypes, movedAssets)
                || ContainsRelevantDeletedAssetPath(movedFromAssetPaths)
            )
            {
                EditorApplication.delayCall += DataVisualizer.SignalRefresh;
            }
        }

        private static HashSet<Type> CollectRelevantTypes(
            Dictionary<string, List<Type>> managedTypes
        )
        {
            HashSet<Type> relevantTypes = new();
            foreach (List<Type> types in managedTypes.Values)
            {
                foreach (Type type in types)
                {
                    relevantTypes.Add(type);
                }
            }
            return relevantTypes;
        }

        private static bool ContainsRelevantDeletedAssetPath(string[] paths)
        {
            foreach (string path in paths)
            {
                if (IsDeletedAssetPathRelevant(path))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsRelevantAsset(HashSet<Type> relevantTypes, string[] paths)
        {
            foreach (string path in paths)
            {
                if (IsRelevantAsset(relevantTypes, path))
                {
                    return true;
                }
            }
            return false;
        }
    }
#endif
}
