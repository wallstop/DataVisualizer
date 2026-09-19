namespace WallstopStudios.DataVisualizer.Editor.Styles
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class DataVisualizerThemeSelection
    {
        /*
            Sheets this class added, per root, so a later Apply through any
            selection instance can adopt (and remove) a theme entry it finds
            already applied. Static is what makes the #121 fix work: instance
            fields cannot recognize an entry another instance added, and the
            production window plus the theme dropdown popup each own their own
            selection instance. The table is keyed by root, so entries die with
            their window instead of pinning it, and every removal path
            unregisters. Owners reset their selection on teardown - the
            window's Cleanup, the popup's OnClose - so nothing outlives it.
            Sheets that predate Apply, such as the base stylesheet a theme
            references, are never registered, so reset preserves them.
        */
        private static readonly ConditionalWeakTable<
            VisualElement,
            HashSet<StyleSheet>
        > OwnedSheetsByRoot = new();

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

        private static void UnregisterOwnedSheet(VisualElement root, StyleSheet sheet)
        {
            if (OwnedSheetsByRoot.TryGetValue(root, out HashSet<StyleSheet> owned))
            {
                owned.Remove(sheet);
            }
        }

        public void Apply(VisualElement root, DataVisualizerThemeSettings theme)
        {
            RemoveAppliedSheet();

            _root = root;
            _appliedSheet = null;
            _appliedThemePath = GetAssetPathOrNull(theme);
            StyleSheet sheet = theme != null ? theme.StyleSheet : null;
            _appliedSheetPath = GetAssetPathOrNull(sheet);
            if (root == null || sheet == null)
            {
                return;
            }

            if (root.styleSheets.Contains(sheet))
            {
                /*
                    Adopt an entry this class added, so a later Apply or reset
                    through this instance can remove it. Sheets that predate
                    Apply keep their previous behavior: never tracked, never
                    removed.
                */
                if (
                    OwnedSheetsByRoot.TryGetValue(root, out HashSet<StyleSheet> owned)
                    && owned.Contains(sheet)
                )
                {
                    _appliedSheet = sheet;
                }

                return;
            }

            root.styleSheets.Add(sheet);
            if (!OwnedSheetsByRoot.TryGetValue(root, out HashSet<StyleSheet> ownedSheets))
            {
                ownedSheets = new HashSet<StyleSheet>();
                OwnedSheetsByRoot.Add(root, ownedSheets);
            }

            ownedSheets.Add(sheet);
            _appliedSheet = sheet;
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
                UnregisterOwnedSheet(_root, _appliedSheet);
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

            UnregisterOwnedSheet(_root, _appliedSheet);
        }
    }
}
