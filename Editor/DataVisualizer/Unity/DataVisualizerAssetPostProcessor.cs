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

        /*
            The relevance decision answers "did an import of a type this window tracks
            land", which needs the asset's type identity only. Resolving that identity
            without loading the object keeps a large import burst from paying one
            synchronous load per path inside OnPostprocessAllAssets (#226): a full
            LoadAssetAtPath deserializes the whole object on the main thread, while
            GetMainAssetTypeAtPath reads the same main-asset identity the index pump
            records, so the decision's result is unchanged. Public because the editor
            test assembly must exercise it (InternalsVisibleTo is not honored for that
            pair in Unity's compilation).
        */
        public static bool IsRelevantAsset(HashSet<Type> relevantTypes, string path)
        {
            if (!IsDeletedAssetPathRelevant(path))
            {
                return false;
            }

            Type mainAssetType = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (mainAssetType == null)
            {
                return false;
            }

            return typeof(DataVisualizerSettings).IsAssignableFrom(mainAssetType) || relevantTypes.Contains(mainAssetType);
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            AssetGuidTypeIndex.Shared.EnqueueAssetChanges(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);

            if (importedAssets.Length <= 0 && deletedAssets.Length <= 0 && movedAssets.Length <= 0 && movedFromAssetPaths.Length <= 0)
            {
                return;
            }

            DataVisualizer window = DataVisualizer.Instance;
            if (window == null)
            {
                return;
            }

            window.RefreshThemeIfAffected(importedAssets, deletedAssets, movedFromAssetPaths);

            window.QueueAssetChanges(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
        }
    }
#endif
}
