namespace WallstopStudios.DataVisualizer.Editor.Styles
{
    using System;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class DataVisualizerThemeSelection
    {
        private StyleSheet _appliedSheet;
        private VisualElement _root;
        private string _appliedThemePath;
        private string _appliedSheetPath;

        public static DataVisualizerThemeSettings Resolve(string guid)
        {
            return string.IsNullOrEmpty(guid)
                ? null
                : AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(
                    AssetDatabase.GUIDToAssetPath(guid)
                );
        }

        private static string GetAssetPathOrNull(UnityEngine.Object asset)
        {
            string path = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
            return string.IsNullOrEmpty(path) ? null : path;
        }

        private static bool ContainsPath(string[] paths, string target)
        {
            if (target == null || paths == null)
            {
                return false;
            }

            foreach (string path in paths)
            {
                if (string.Equals(path, target, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public void Apply(VisualElement root, DataVisualizerThemeSettings theme)
        {
            if (_root != null && _appliedSheet != null)
            {
                _root.styleSheets.Remove(_appliedSheet);
            }

            _root = root;
            _appliedSheet = null;
            _appliedThemePath = GetAssetPathOrNull(theme);
            StyleSheet sheet = theme != null ? theme.StyleSheet : null;
            _appliedSheetPath = GetAssetPathOrNull(sheet);
            if (root != null && sheet != null && !root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
                _appliedSheet = sheet;
            }
        }

        public bool IsAffectedByAssetChanges(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedFromAssetPaths
        )
        {
            return ContainsPath(importedAssets, _appliedThemePath)
                || ContainsPath(deletedAssets, _appliedThemePath)
                || ContainsPath(movedFromAssetPaths, _appliedThemePath)
                || ContainsPath(importedAssets, _appliedSheetPath)
                || ContainsPath(deletedAssets, _appliedSheetPath)
                || ContainsPath(movedFromAssetPaths, _appliedSheetPath);
        }
    }
}
