namespace WallstopStudios.Editor.DataVisualizer.Unity
{
#if UNITY_EDITOR
    using System.Linq;
    using Data;
    using UnityEditor;
    using WallstopStudios.DataVisualizer;

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

                bool isSettingsAsset = typeof(DataVisualizerSettings).IsAssignableFrom(assetType);
                bool isBaseDatObject = typeof(BaseDataObject).IsAssignableFrom(assetType);

                if (isBaseDatObject)
                {
                    needsRefresh = true;
                    break;
                }

                if (!isSettingsAsset)
                {
                    continue;
                }

                DataVisualizerSettings settingsInstance =
                    AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(path);
                if (settingsInstance == null || settingsInstance.persistStateInSettingsAsset)
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
