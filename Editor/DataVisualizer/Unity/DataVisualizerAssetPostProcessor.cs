namespace WallstopStudios.Editor.DataVisualizer.Unity
{
#if UNITY_EDITOR
    using System.Linq;
    using Data;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer;

    public sealed class DataVisualizerAssetProcessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            bool needsRefresh = deletedAssets.Where(IsAsset).Any();
            if (!needsRefresh)
            {
                needsRefresh = importedAssets
                    .Where(IsAsset)
                    .Select(AssetDatabase.LoadAssetAtPath<ScriptableObject>)
                    .Where(so => so != null)
                    .Any(asset => asset is not DataVisualizerSettings);
            }

            if (!needsRefresh)
            {
                for (int i = 0; i < movedAssets.Length && i < movedFromAssetPaths.Length; i++)
                {
                    string newPath = movedAssets[i];
                    string oldPath = movedFromAssetPaths[i];

                    if (IsAsset(newPath) || IsAsset(oldPath))
                    {
                        needsRefresh = true;
                        break;
                    }
                }
            }

            if (needsRefresh)
            {
                EditorApplication.delayCall += DataVisualizer.SignalRefresh;
            }
        }

        internal static bool IsAsset(string path) =>
            path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase);
    }
#endif
}
