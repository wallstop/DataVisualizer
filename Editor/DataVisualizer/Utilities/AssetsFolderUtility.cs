namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Helper;

    public static class AssetsFolderUtility
    {
        public static bool TryGetAssetsRelativePath(
            string selectedAbsolutePath,
            string projectAssetsPath,
            out string relativePath
        )
        {
            if (
                string.IsNullOrEmpty(selectedAbsolutePath)
                || string.IsNullOrEmpty(projectAssetsPath)
            )
            {
                relativePath = null;
                return false;
            }

            if (selectedAbsolutePath.Equals(projectAssetsPath, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = "Assets";
                return true;
            }

            if (
                !selectedAbsolutePath.StartsWith(
                    projectAssetsPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                relativePath = null;
                return false;
            }

            relativePath = "Assets" + selectedAbsolutePath.Substring(projectAssetsPath.Length);
            relativePath = relativePath.Replace("//", "/");
            return true;
        }

        public static bool TrySelectAssetsFolder(string dialogTitle, out string relativePath)
        {
            string selectedAbsolutePath = EditorUtility.OpenFolderPanel(
                title: dialogTitle,
                folder: "Assets",
                defaultName: ""
            );
            if (string.IsNullOrWhiteSpace(selectedAbsolutePath))
            {
                relativePath = null;
                return false;
            }

            selectedAbsolutePath = Path.GetFullPath(selectedAbsolutePath).SanitizePath();
            string projectAssetsPath = Path.GetFullPath(Application.dataPath).SanitizePath();
            if (
                !TryGetAssetsRelativePath(selectedAbsolutePath, projectAssetsPath, out relativePath)
            )
            {
                Debug.LogError("Selected folder must be inside the project's Assets folder.");
                EditorUtility.DisplayDialog(
                    "Invalid Folder",
                    "The selected folder must be inside the project's 'Assets' directory.",
                    "OK"
                );
                return false;
            }

            return true;
        }
    }
#endif
}
