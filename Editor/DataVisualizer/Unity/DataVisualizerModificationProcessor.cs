namespace WallstopStudios.DataVisualizer.Editor.Unity
{
#if UNITY_EDITOR
    using System.Linq;
    using Data;
    using UnityEditor;
    using UnityEngine;

    public sealed class DataVisualizerModificationProcessor : AssetModificationProcessor
    {
        private static bool RefreshSignalThisSave;

        private static string[] OnWillSaveAssets(string[] paths)
        {
            bool needsRefresh = false;

            foreach (string path in paths.Where(DataVisualizerAssetProcessor.IsAsset))
            {
                System.Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);

                if (assetType == null)
                {
                    continue;
                }

                bool isScriptableObject = typeof(ScriptableObject).IsAssignableFrom(assetType);

                if (
                    !isScriptableObject
                    || typeof(DataVisualizerSettings).IsAssignableFrom(assetType)
                )
                {
                    continue;
                }

                needsRefresh = true;
                break;
            }

            if (!needsRefresh || RefreshSignalThisSave)
            {
                return paths;
            }

            EditorApplication.delayCall += DataVisualizer.SignalRefresh;
            RefreshSignalThisSave = true;
            EditorApplication.delayCall += ResetSignalFlag;
            return paths;
        }

        private static void ResetSignalFlag()
        {
            RefreshSignalThisSave = false;
        }
    }
#endif
}
