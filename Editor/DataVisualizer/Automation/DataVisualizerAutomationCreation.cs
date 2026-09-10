namespace WallstopStudios.DataVisualizer.Editor.Automation
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer;

    public static partial class DataVisualizerAutomation
    {
        private static DataVisualizerAssetOperationResult ExecuteAssetCreationOperation(
            DataVisualizerAssetOperationRequest request,
            bool preview
        )
        {
            DataVisualizerAssetOperationResult result = new() { preview = preview };
            if (request.operation == DataVisualizerAssetOperationKind.Create)
            {
                return ExecuteCreateOperation(request, result, preview);
            }

            if (request.guids == null || request.guids.Length != 1)
            {
                return FailOperation(
                    result,
                    "Clone operations require exactly one source asset GUID."
                );
            }
            if (!TryResolvePath(request.guids[0], out string sourcePath, out string diagnostic))
            {
                result.items.Add(
                    new DataVisualizerAssetOperationItemResult
                    {
                        guid = request.guids[0],
                        diagnostic = diagnostic,
                    }
                );
                return CompleteCreationOperation(result);
            }

            ScriptableObject source =
                AssetDatabase.LoadMainAssetAtPath(sourcePath) as ScriptableObject;
            if (source == null)
            {
                return FailOperation(
                    result,
                    "The requested clone source is not a ScriptableObject."
                );
            }
            string cloneName = string.IsNullOrWhiteSpace(request.value)
                ? BuildCloneName(sourcePath)
                : request.value;
            if (!TryValidateName(cloneName, out diagnostic))
            {
                return FailOperation(result, diagnostic);
            }
            string directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(directory))
            {
                return FailOperation(result, "Clone source directory is invalid.");
            }
            string targetPath = $"{directory}/{cloneName}{Path.GetExtension(sourcePath)}";
            if (
                !string.Equals(
                    AssetDatabase.GenerateUniqueAssetPath(targetPath),
                    targetPath,
                    StringComparison.Ordinal
                )
            )
            {
                return FailOperation(result, "An asset already exists at the requested path.");
            }
            if (!TryValidateMove(sourcePath, targetPath, out diagnostic))
            {
                return FailOperation(result, diagnostic);
            }

            DataVisualizerAssetOperationItemResult item = new()
            {
                guid = request.guids[0],
                originalPath = sourcePath,
                resultingPath = targetPath,
            };
            result.items.Add(item);
            if (preview)
            {
                item.succeeded = true;
                return CompleteCreationOperation(result);
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return FailOperation(
                    result,
                    "Asset mutations cannot run while entering or in Play Mode."
                );
            }

            ScriptableObject clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(source);
                if (clone == null)
                {
                    item.diagnostic = "Unity could not instantiate the clone.";
                    return CompleteCreationOperation(result);
                }
                if (clone is IDuplicable duplicable)
                {
                    duplicable.BeforeClone(source);
                }
                AssetDatabase.CreateAsset(clone, targetPath);
                item.mutationApplied = true;
                AssetDatabase.SaveAssets();
                ScriptableObject cloneAsset =
                    AssetDatabase.LoadMainAssetAtPath(targetPath) as ScriptableObject;
                if (cloneAsset == null)
                {
                    item.diagnostic = "Unity did not create the cloned asset.";
                    return CompleteCreationOperation(result);
                }
                if (cloneAsset is IDuplicable cloneDataObject)
                {
                    cloneDataObject.AfterClone(source);
                }
                AssetDatabase.SaveAssets();
                item.resultingGuid = AssetDatabase.AssetPathToGUID(targetPath);
                item.succeeded = !string.IsNullOrWhiteSpace(item.resultingGuid);
                if (!item.succeeded)
                {
                    item.diagnostic = "Unity did not assign a GUID to the cloned asset.";
                }
            }
            catch (Exception exception)
            {
                item.diagnostic = item.mutationApplied
                    ? $"Clone asset was created, but its lifecycle hook failed: {exception.Message}"
                    : $"Clone operation failed: {exception.Message}";
            }
            finally
            {
                if (clone != null && string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(clone)))
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
            return CompleteCreationOperation(result);
        }

        private static DataVisualizerAssetOperationResult ExecuteCreateOperation(
            DataVisualizerAssetOperationRequest request,
            DataVisualizerAssetOperationResult result,
            bool preview
        )
        {
            if (string.IsNullOrWhiteSpace(request.assemblyQualifiedTypeName))
            {
                return FailOperation(
                    result,
                    "assemblyQualifiedTypeName is required for Create operations."
                );
            }
            if (
                !TryResolveManagedType(
                    request.assemblyQualifiedTypeName,
                    out Type type,
                    out string diagnostic
                )
            )
            {
                return FailOperation(result, diagnostic);
            }
            if (!TryValidateName(request.value, out diagnostic))
            {
                return FailOperation(result, diagnostic);
            }
            if (!TryResolveCreateFolder(request, type, out string folder, out diagnostic))
            {
                return FailOperation(result, diagnostic);
            }
            string targetPath = $"{folder}/{request.value}.asset";
            string absolutePath = Path.Combine(
                Application.dataPath,
                targetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)
            );
            if (File.Exists(absolutePath))
            {
                return FailOperation(result, "An asset already exists at the requested path.");
            }
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
            if (!string.Equals(uniquePath, targetPath, StringComparison.Ordinal))
            {
                return FailOperation(result, "An asset already exists at the requested path.");
            }

            DataVisualizerAssetOperationItemResult item = new()
            {
                originalPath = string.Empty,
                resultingPath = targetPath,
                diagnostic = string.Empty,
            };
            result.items.Add(item);
            if (preview)
            {
                item.succeeded = true;
                return CompleteCreationOperation(result);
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return FailOperation(
                    result,
                    "Asset mutations cannot run while entering or in Play Mode."
                );
            }

            ScriptableObject instance = null;
            try
            {
                EnsureAssetFolder(folder);
                instance = ScriptableObject.CreateInstance(type);
                if (instance is ICreatable creatable)
                {
                    creatable.BeforeCreate();
                }
                AssetDatabase.CreateAsset(instance, targetPath);
                item.mutationApplied = true;
                AssetDatabase.SaveAssets();
                ScriptableObject createdAsset =
                    AssetDatabase.LoadMainAssetAtPath(targetPath) as ScriptableObject;
                if (createdAsset == null)
                {
                    item.diagnostic = "Unity did not create the requested asset.";
                    return CompleteCreationOperation(result);
                }
                if (createdAsset is ICreatable created)
                {
                    created.AfterCreate();
                }
                AssetDatabase.SaveAssets();
                item.resultingGuid = AssetDatabase.AssetPathToGUID(targetPath);
                item.succeeded = !string.IsNullOrWhiteSpace(item.resultingGuid);
                if (!item.succeeded)
                {
                    item.diagnostic = "Unity did not assign a GUID to the created asset.";
                }
            }
            catch (Exception exception)
            {
                item.diagnostic = item.mutationApplied
                    ? $"Asset was created, but its lifecycle hook failed: {exception.Message}"
                    : $"Create operation failed: {exception.Message}";
            }
            finally
            {
                if (
                    instance != null
                    && string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(instance))
                )
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
            return CompleteCreationOperation(result);
        }

        private static bool TryResolveManagedType(
            string assemblyQualifiedTypeName,
            out Type type,
            out string diagnostic
        )
        {
            type = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
            if (type == null || !IsManagedType(type))
            {
                diagnostic =
                    "assemblyQualifiedTypeName must identify a discovered managed ScriptableObject type.";
                return false;
            }
            diagnostic = string.Empty;
            return true;
        }

        private static bool TryResolveCreateFolder(
            DataVisualizerAssetOperationRequest request,
            Type type,
            out string folder,
            out string diagnostic
        )
        {
            folder = request.destinationFolder;
            if (string.IsNullOrWhiteSpace(folder))
            {
                string baseFolder = ReadConfiguration().dataFolderPath;
                string typeFolder = (type.FullName ?? type.Name).Replace('.', '/');
                folder = $"{baseFolder.TrimEnd('/')}/{typeFolder}";
            }
            DataVisualizerConfiguration configuration = new() { dataFolderPath = folder };
            if (!configuration.TryValidate(out diagnostic))
            {
                return false;
            }
            folder = configuration.dataFolderPath;
            return true;
        }

        private static void EnsureAssetFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    if (
                        string.IsNullOrWhiteSpace(AssetDatabase.CreateFolder(current, parts[index]))
                    )
                    {
                        throw new InvalidOperationException(
                            $"Unity could not create folder '{next}'."
                        );
                    }
                }
                current = next;
            }
        }

        private static string BuildCloneName(string sourcePath)
        {
            const string pattern = @"\(Clone(\s+-?\d+)?\)";
            string originalName = Regex
                .Replace(Path.GetFileNameWithoutExtension(sourcePath), pattern, string.Empty)
                .TrimEnd();
            int count = 0;
            while (true)
            {
                string candidate =
                    $"{originalName} (Clone{(count++ == 0 ? string.Empty : $" {count}")})";
                string path =
                    $"{Path.GetDirectoryName(sourcePath)?.Replace('\\', '/')}/{candidate}{Path.GetExtension(sourcePath)}";
                if (
                    string.Equals(
                        AssetDatabase.GenerateUniqueAssetPath(path),
                        path,
                        StringComparison.Ordinal
                    )
                )
                {
                    return candidate;
                }
            }
        }

        private static DataVisualizerAssetOperationResult CompleteCreationOperation(
            DataVisualizerAssetOperationResult result
        )
        {
            result.complete = true;
            result.succeeded = result.items.Count > 0 && result.items.All(item => item.succeeded);
            if (!result.succeeded && string.IsNullOrWhiteSpace(result.diagnostic))
            {
                result.diagnostic =
                    result
                        .items.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.diagnostic))
                        ?.diagnostic
                    ?? "The asset creation operation failed.";
            }
            return result;
        }
    }
}
