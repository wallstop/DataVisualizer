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
            DataVisualizer openWindow = null;

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
                }
                else if (isSettingsAsset)
                {
                    DataVisualizerSettings settingsInstance =
                        AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(path);
                    if (settingsInstance != null && !settingsInstance.persistStateInSettingsAsset)
                    {
                        needsRefresh = true;
                    }
                }

                if (needsRefresh)
                {
                    openWindow = EditorWindow.GetWindow<DataVisualizer>(false, null, false);
                    break;
                }
            }

            if (needsRefresh && openWindow != null && !RefreshSignalThisSave)
            {
                EditorApplication.delayCall += DataVisualizer.SignalRefresh;
                RefreshSignalThisSave = true;
                EditorApplication.delayCall += ResetSignalFlag;
            }
            return paths;
        }

        private static void ResetSignalFlag()
        {
            RefreshSignalThisSave = false;
        }
    }
#endif
}
