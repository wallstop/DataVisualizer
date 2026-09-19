namespace WallstopStudios.DataVisualizer.Editor.Styles
{
    using System;
    using System.Collections.Generic;
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

            return Array.Exists(
                paths,
                path => string.Equals(path, target, StringComparison.Ordinal)
            );
        }

        public void Apply(VisualElement root, DataVisualizerThemeSettings theme)
        {
            RemoveAppliedSheet();

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

        private void RemoveAppliedSheet()
        {
            if (_root == null || ReferenceEquals(_appliedSheet, null))
            {
                return;
            }

            if (_appliedSheet != null)
            {
                _root.styleSheets.Remove(_appliedSheet);
                return;
            }

            /*
                The wrapper is a destroyed StyleSheet (fake null), so
                styleSheets.Remove throws instead of removing it. Rebuild the
                list without the dead entry, preserving the order of every
                sheet this class does not own.
            */
            List<StyleSheet> keep = new(_root.styleSheets.count);
            for (int index = 0; index < _root.styleSheets.count; index++)
            {
                StyleSheet sheet = _root.styleSheets[index];
                if (!ReferenceEquals(sheet, _appliedSheet))
                {
                    keep.Add(sheet);
                }
            }

            _root.styleSheets.Clear();
            foreach (StyleSheet sheet in keep)
            {
                _root.styleSheets.Add(sheet);
            }
        }
    }
}
