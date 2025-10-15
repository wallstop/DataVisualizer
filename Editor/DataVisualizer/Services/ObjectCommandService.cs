namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.IO;
    using Extensions;
    using Helper;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class ObjectCommandService
    {
        private readonly DataVisualizer _dataVisualizer;

        public ObjectCommandService(DataVisualizer dataVisualizer)
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
        }

        public void MoveToTop(Button button)
        {
            if (button == null)
            {
                return;
            }

            ScriptableObject dataObject = button.userData as ScriptableObject;
            if (dataObject == null)
            {
                return;
            }

            _dataVisualizer._filteredObjects.Remove(dataObject);
            _dataVisualizer._filteredObjects.Insert(0, dataObject);
            _dataVisualizer._selectedObjects.Remove(dataObject);
            _dataVisualizer._selectedObjects.Insert(0, dataObject);
            _dataVisualizer.UpdateAndSaveObjectOrderList(
                dataObject.GetType(),
                _dataVisualizer._selectedObjects
            );
            _dataVisualizer.BuildObjectsView();
        }

        public void MoveToBottom(Button button)
        {
            if (button == null)
            {
                return;
            }

            ScriptableObject dataObject = button.userData as ScriptableObject;
            if (dataObject == null)
            {
                return;
            }

            _dataVisualizer._selectedObjects.Remove(dataObject);
            _dataVisualizer._selectedObjects.Add(dataObject);
            _dataVisualizer._filteredObjects.Remove(dataObject);
            _dataVisualizer._filteredObjects.Add(dataObject);
            _dataVisualizer.UpdateAndSaveObjectOrderList(
                dataObject.GetType(),
                _dataVisualizer._selectedObjects
            );
            _dataVisualizer.BuildObjectsView();
        }

        public void Clone(Button button)
        {
            if (button == null)
            {
                return;
            }

            ScriptableObject dataObject = button.userData as ScriptableObject;
            if (dataObject == null)
            {
                return;
            }

            _dataVisualizer.CloneObject(dataObject);
        }

        public void Rename(Button button)
        {
            if (button == null)
            {
                return;
            }

            ScriptableObject dataObject = button.userData as ScriptableObject;
            if (dataObject == null)
            {
                return;
            }

            object property = button.GetProperty(DataVisualizer.RowComponentsProperty);
            if (property is not DataVisualizer.ObjectRowComponents components)
            {
                return;
            }

            _dataVisualizer.OpenRenamePopover(components.TitleLabel, button, dataObject);
        }

        public void Move(Button button)
        {
            if (button == null)
            {
                return;
            }

            ScriptableObject dataObject = button.userData as ScriptableObject;
            if (dataObject == null)
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(dataObject);
            string startDirectory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrWhiteSpace(startDirectory))
            {
                startDirectory = string.Empty;
            }

            string selectedAbsolutePath = EditorUtility.OpenFolderPanel(
                "Select New Location (Must be inside Assets)",
                startDirectory,
                string.Empty
            );

            if (string.IsNullOrWhiteSpace(selectedAbsolutePath))
            {
                return;
            }

            selectedAbsolutePath = Path.GetFullPath(selectedAbsolutePath).SanitizePath();
            string projectAssetsPath = Path.GetFullPath(Application.dataPath).SanitizePath();

            if (
                !selectedAbsolutePath.StartsWith(
                    projectAssetsPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                Debug.LogError("Selected folder must be inside the project's Assets folder.");
                EditorUtility.DisplayDialog(
                    "Invalid Folder",
                    "The selected folder must be inside the project's 'Assets' directory.",
                    "OK"
                );
                return;
            }

            string relativePath = string.Equals(
                selectedAbsolutePath,
                projectAssetsPath,
                StringComparison.OrdinalIgnoreCase
            )
                ? "Assets"
                : "Assets" + selectedAbsolutePath.Substring(projectAssetsPath.Length);
            relativePath = relativePath.Replace("//", "/");

            string targetPath = relativePath + "/" + dataObject.name + ".asset";
            if (string.Equals(assetPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string errorMessage = AssetDatabase.MoveAsset(assetPath, targetPath);
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Debug.LogError(
                    "Error moving asset "
                        + dataObject.name
                        + " from '"
                        + assetPath
                        + "' to '"
                        + targetPath
                        + "': "
                        + errorMessage
                );
                EditorUtility.DisplayDialog("Invalid Move Operation", errorMessage, "OK");
                return;
            }

            _dataVisualizer.RefreshMetadataForObject(dataObject);
        }

        public void Delete(Button button)
        {
            if (button == null)
            {
                return;
            }

            ScriptableObject dataObject = button.userData as ScriptableObject;
            if (dataObject == null)
            {
                return;
            }

            _dataVisualizer.OpenConfirmDeletePopover(button, dataObject);
        }
    }
}
