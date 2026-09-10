namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;

    public static class AssetGuidDiscovery
    {
        public static string[] FindCandidates(Type type, string dataFolderPath)
        {
            if (type == null)
            {
                return Array.Empty<string>();
            }

            HashSet<string> guids = new(
                AssetDatabase.FindAssets($"t:{type.Name}"),
                StringComparer.OrdinalIgnoreCase
            );

            if (string.IsNullOrWhiteSpace(dataFolderPath))
            {
                return new List<string>(guids).ToArray();
            }

            string typeFolder = Path.Combine(
                    dataFolderPath,
                    (type.FullName ?? type.Name).Replace('.', '/')
                )
                .Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(typeFolder))
            {
                // Unity's type filter can omit editor-only ScriptableObjects when their class does
                // not have its own matching MonoScript asset. Assets created by this window still
                // live in the deterministic per-type folder, so query that narrow location too.
                guids.UnionWith(AssetDatabase.FindAssets(string.Empty, new[] { typeFolder }));
            }

            return new List<string>(guids).ToArray();
        }
    }
#endif
}
