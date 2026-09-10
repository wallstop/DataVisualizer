namespace WallstopStudios.DataVisualizer.Editor.Automation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer;

    public enum DataVisualizerAssetOperationKind
    {
        Rename,
        Move,
        SetLabels,
        Delete,
    }

    [Serializable]
    public sealed class DataVisualizerAssetOperationRequest
    {
        public DataVisualizerAssetOperationKind operation;
        public string[] guids = Array.Empty<string>();
        public string value = string.Empty;
        public string[] labels = Array.Empty<string>();
    }

    [Serializable]
    public sealed class DataVisualizerAssetOperationItemResult
    {
        public string guid;
        public string originalPath;
        public string resultingPath;
        public bool succeeded;
        public string diagnostic;
    }

    [Serializable]
    public sealed class DataVisualizerAssetOperationResult
    {
        public bool preview;
        public bool succeeded;
        public bool complete;
        public string diagnostic;
        public List<DataVisualizerAssetOperationItemResult> items = new();
    }

    public static partial class DataVisualizerAutomation
    {
        public static DataVisualizerAssetOperationResult PreviewAssetOperation(
            DataVisualizerAssetOperationRequest request
        )
        {
            return ExecuteAssetOperation(request, preview: true);
        }

        public static DataVisualizerAssetOperationResult ApplyAssetOperation(
            DataVisualizerAssetOperationRequest request
        )
        {
            return ExecuteAssetOperation(request, preview: false);
        }

        private static DataVisualizerAssetOperationResult ExecuteAssetOperation(
            DataVisualizerAssetOperationRequest request,
            bool preview
        )
        {
            DataVisualizerAssetOperationResult result = new() { preview = preview };
            if (request == null)
            {
                return FailOperation(result, "An asset operation request is required.");
            }

            if (request.guids == null || request.guids.Length == 0)
            {
                return FailOperation(result, "At least one asset GUID is required.");
            }

            if (!preview && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return FailOperation(
                    result,
                    "Asset mutations cannot run while entering or in Play Mode."
                );
            }

            bool mutationSucceeded = false;
            foreach (string guid in request.guids)
            {
                DataVisualizerAssetOperationItemResult item = new() { guid = guid };
                result.items.Add(item);
                if (!TryResolvePath(guid, out string path, out string diagnostic))
                {
                    item.diagnostic = diagnostic;
                    continue;
                }

                item.originalPath = path;
                if (!TryPrepareOperation(request, path, item, out diagnostic))
                {
                    item.diagnostic = diagnostic;
                    continue;
                }

                if (preview)
                {
                    item.succeeded = true;
                    continue;
                }

                try
                {
                    if (ApplyOperation(request, guid, path, item, out diagnostic))
                    {
                        item.succeeded = true;
                        mutationSucceeded = true;
                    }
                    else
                    {
                        item.diagnostic = diagnostic;
                    }
                }
                catch (Exception exception)
                {
                    item.diagnostic = $"Asset operation failed: {exception.Message}";
                }
            }

            if (!preview && mutationSucceeded)
            {
                AssetDatabase.SaveAssets();
                if (request.operation == DataVisualizerAssetOperationKind.Delete)
                {
                    AssetDatabase.Refresh();
                }
            }

            result.complete = true;
            result.succeeded = result.items.Count > 0 && result.items.All(item => item.succeeded);
            if (!result.succeeded && string.IsNullOrWhiteSpace(result.diagnostic))
            {
                result.diagnostic = "One or more asset operations failed.";
            }
            return result;
        }

        private static bool TryResolvePath(string guid, out string path, out string diagnostic)
        {
            path = string.IsNullOrWhiteSpace(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path))
            {
                diagnostic = "The requested asset GUID was not found.";
                return false;
            }

            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                diagnostic = "Asset operations are limited to assets inside the Assets folder.";
                return false;
            }

            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type))
            {
                diagnostic = "The requested asset is not a ScriptableObject main asset.";
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }

        private static bool TryPrepareOperation(
            DataVisualizerAssetOperationRequest request,
            string path,
            DataVisualizerAssetOperationItemResult item,
            out string diagnostic
        )
        {
            diagnostic = string.Empty;
            switch (request.operation)
            {
                case DataVisualizerAssetOperationKind.Rename:
                    if (!TryValidateName(request.value, out diagnostic))
                    {
                        return false;
                    }

                    string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
                    string targetPath = string.IsNullOrWhiteSpace(directory)
                        ? null
                        : $"{directory}/{request.value}{Path.GetExtension(path)}";
                    if (!TryValidateMove(path, targetPath, out diagnostic))
                    {
                        return false;
                    }

                    item.resultingPath = targetPath;
                    return true;

                case DataVisualizerAssetOperationKind.Move:
                    if (!TryValidateFolder(request.value, out string targetFolder, out diagnostic))
                    {
                        return false;
                    }

                    targetPath = $"{targetFolder}/{Path.GetFileName(path)}";
                    if (!TryValidateMove(path, targetPath, out diagnostic))
                    {
                        return false;
                    }

                    item.resultingPath = targetPath;
                    return true;

                case DataVisualizerAssetOperationKind.SetLabels:
                    item.resultingPath = path;
                    return true;

                case DataVisualizerAssetOperationKind.Delete:
                    item.resultingPath = string.Empty;
                    return true;

                default:
                    diagnostic = "The requested asset operation is unsupported.";
                    return false;
            }
        }

        private static bool ApplyOperation(
            DataVisualizerAssetOperationRequest request,
            string guid,
            string path,
            DataVisualizerAssetOperationItemResult item,
            out string diagnostic
        )
        {
            diagnostic = string.Empty;
            switch (request.operation)
            {
                case DataVisualizerAssetOperationKind.Rename:
                    ScriptableObject original = LoadAsset(guid, out diagnostic);
                    if (original == null)
                    {
                        return false;
                    }

                    if (original is IRenamable renamable)
                    {
                        renamable.BeforeRename(request.value);
                    }

                    diagnostic = AssetDatabase.RenameAsset(path, request.value);
                    if (!string.IsNullOrWhiteSpace(diagnostic))
                    {
                        return false;
                    }

                    if (original is IRenamable renamed)
                    {
                        renamed.AfterRename(request.value);
                    }
                    return true;

                case DataVisualizerAssetOperationKind.Move:
                    diagnostic = AssetDatabase.MoveAsset(path, item.resultingPath);
                    return string.IsNullOrWhiteSpace(diagnostic);

                case DataVisualizerAssetOperationKind.SetLabels:
                    ScriptableObject asset = LoadAsset(guid, out diagnostic);
                    if (asset == null)
                    {
                        return false;
                    }

                    AssetDatabase.SetLabels(
                        asset,
                        (request.labels ?? Array.Empty<string>())
                            .Where(label => !string.IsNullOrWhiteSpace(label))
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(label => label, StringComparer.Ordinal)
                            .ToArray()
                    );
                    return true;

                case DataVisualizerAssetOperationKind.Delete:
                    if (!AssetDatabase.DeleteAsset(path))
                    {
                        diagnostic = "Unity could not delete the requested asset.";
                        return false;
                    }
                    return true;

                default:
                    diagnostic = "The requested asset operation is unsupported.";
                    return false;
            }
        }

        private static bool TryValidateName(string value, out string diagnostic)
        {
            if (
                string.IsNullOrWhiteSpace(value)
                || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || value.Contains('/')
                || value.Contains('\\')
            )
            {
                diagnostic = "Asset name is invalid.";
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }

        private static bool TryValidateFolder(
            string value,
            out string folder,
            out string diagnostic
        )
        {
            folder = value;
            DataVisualizerConfiguration configuration = new() { dataFolderPath = value };
            if (!configuration.TryValidate(out diagnostic))
            {
                return false;
            }

            folder = configuration.dataFolderPath;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                diagnostic = "Move target must be an existing Assets folder.";
                return false;
            }

            return true;
        }

        private static bool TryValidateMove(
            string sourcePath,
            string targetPath,
            out string diagnostic
        )
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                diagnostic = "Asset target path is invalid.";
                return false;
            }

            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "Asset target path is unchanged.";
                return false;
            }

            diagnostic = AssetDatabase.ValidateMoveAsset(sourcePath, targetPath);
            return string.IsNullOrWhiteSpace(diagnostic);
        }

        private static DataVisualizerAssetOperationResult FailOperation(
            DataVisualizerAssetOperationResult result,
            string diagnostic
        )
        {
            result.complete = true;
            result.succeeded = false;
            result.diagnostic = diagnostic;
            return result;
        }
    }
}
