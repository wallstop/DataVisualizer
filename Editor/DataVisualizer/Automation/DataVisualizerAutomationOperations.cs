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
        Rename = 0,
        Move = 1,
        SetLabels = 2,
        Delete = 3,
        Create = 4,
        Clone = 5,
    }

    [Serializable]
    public sealed class DataVisualizerAssetOperationRequest
    {
        public DataVisualizerAssetOperationKind operation;
        public string[] guids = Array.Empty<string>();
        public string value = string.Empty;
        public string[] labels = Array.Empty<string>();
        public string assemblyQualifiedTypeName;
        public string destinationFolder;
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
        private sealed class PreparedAssetOperation
        {
            public string guid;
            public string path;
            public DataVisualizerAssetOperationItemResult item;
        }

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

            if (
                request.operation != DataVisualizerAssetOperationKind.Create
                && (request.guids == null || request.guids.Length == 0)
            )
            {
                return FailOperation(result, "At least one asset GUID is required.");
            }

            if (!Enum.IsDefined(typeof(DataVisualizerAssetOperationKind), request.operation))
            {
                return FailOperation(result, "The requested asset operation is unsupported.");
            }

            if (
                request.operation == DataVisualizerAssetOperationKind.Create
                || request.operation == DataVisualizerAssetOperationKind.Clone
            )
            {
                return ExecuteAssetCreationOperation(request, preview);
            }

            if (
                request.operation == DataVisualizerAssetOperationKind.SetLabels
                && request.labels == null
            )
            {
                return FailOperation(result, "labels is required for SetLabels operations.");
            }

            if (!preview && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return FailOperation(
                    result,
                    "Asset mutations cannot run while entering or in Play Mode."
                );
            }

            bool mutationSucceeded = false;
            bool preflightFailed = false;
            HashSet<string> sourcePaths = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenGuids = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> plannedDestinations = new(StringComparer.OrdinalIgnoreCase);
            List<PreparedAssetOperation> prepared = new();
            foreach (string guid in request.guids)
            {
                DataVisualizerAssetOperationItemResult item = new() { guid = guid };
                result.items.Add(item);
                if (!seenGuids.Add(guid ?? string.Empty))
                {
                    item.diagnostic = "The same asset GUID appears more than once in this request.";
                    preflightFailed = true;
                    continue;
                }

                try
                {
                    if (!TryResolvePath(guid, out string path, out string diagnostic))
                    {
                        item.diagnostic = diagnostic;
                        preflightFailed = true;
                        continue;
                    }

                    item.originalPath = path;
                    sourcePaths.Add(path);
                    if (!TryPrepareOperation(request, path, item, out diagnostic))
                    {
                        item.diagnostic = diagnostic;
                        preflightFailed = true;
                        continue;
                    }

                    if (
                        !TryReserveDestination(
                            request.operation,
                            item.resultingPath,
                            plannedDestinations,
                            out diagnostic
                        )
                    )
                    {
                        item.diagnostic = diagnostic;
                        preflightFailed = true;
                        continue;
                    }

                    prepared.Add(
                        new PreparedAssetOperation
                        {
                            guid = guid,
                            path = path,
                            item = item,
                        }
                    );
                }
                catch (Exception exception)
                {
                    item.diagnostic = $"Asset operation validation failed: {exception.Message}";
                    preflightFailed = true;
                }
            }

            foreach (PreparedAssetOperation operation in prepared)
            {
                if (
                    !string.IsNullOrWhiteSpace(operation.item.resultingPath)
                    && sourcePaths.Contains(operation.item.resultingPath)
                    && !string.Equals(
                        operation.path,
                        operation.item.resultingPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    operation.item.diagnostic =
                        "Batch destination conflicts with another source path; no items were changed.";
                    preflightFailed = true;
                }
            }

            if (preflightFailed)
            {
                return CompleteOperation(result);
            }

            foreach (PreparedAssetOperation operation in prepared)
            {
                if (preview)
                {
                    operation.item.succeeded = true;
                    continue;
                }

                try
                {
                    if (
                        ApplyOperation(
                            request,
                            operation.guid,
                            operation.path,
                            operation.item,
                            out string diagnostic
                        )
                    )
                    {
                        operation.item.succeeded = true;
                        mutationSucceeded = true;
                    }
                    else
                    {
                        operation.item.diagnostic = diagnostic;
                    }
                }
                catch (Exception exception)
                {
                    operation.item.diagnostic = $"Asset operation failed: {exception.Message}";
                    mutationSucceeded |=
                        request.operation == DataVisualizerAssetOperationKind.Rename
                        && string.Equals(
                            AssetDatabase.AssetPathToGUID(operation.item.resultingPath),
                            operation.guid,
                            StringComparison.OrdinalIgnoreCase
                        );
                }
            }

            if (!preview && mutationSucceeded)
            {
                try
                {
                    AssetDatabase.SaveAssets();
                    if (request.operation == DataVisualizerAssetOperationKind.Delete)
                    {
                        AssetDatabase.Refresh();
                    }
                }
                catch (Exception exception)
                {
                    result.diagnostic =
                        $"Asset operation finalization failed after applying item changes: {exception.Message}";
                }
            }

            return CompleteOperation(result);
        }

        private static DataVisualizerAssetOperationResult CompleteOperation(
            DataVisualizerAssetOperationResult result
        )
        {
            result.complete = true;
            result.succeeded =
                string.IsNullOrWhiteSpace(result.diagnostic)
                && result.items.Count > 0
                && result.items.All(item => item.succeeded);
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

        private static bool TryReserveDestination(
            DataVisualizerAssetOperationKind operation,
            string resultingPath,
            HashSet<string> plannedDestinations,
            out string diagnostic
        )
        {
            if (
                operation != DataVisualizerAssetOperationKind.Rename
                && operation != DataVisualizerAssetOperationKind.Move
            )
            {
                diagnostic = string.Empty;
                return true;
            }

            if (plannedDestinations.Add(resultingPath))
            {
                diagnostic = string.Empty;
                return true;
            }

            diagnostic = "Another item in this request uses the same destination path.";
            return false;
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
