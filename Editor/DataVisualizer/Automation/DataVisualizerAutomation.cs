namespace WallstopStudios.DataVisualizer.Editor.Automation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Data;

    [Serializable]
    public sealed class DataVisualizerConfiguration
    {
        public string dataFolderPath = DataVisualizerSettings.DefaultDataFolderPath;
        public bool persistStateInSettingsAsset;
        public bool selectActiveObject;

        public DataVisualizerConfiguration Clone()
        {
            return new DataVisualizerConfiguration
            {
                dataFolderPath = dataFolderPath,
                persistStateInSettingsAsset = persistStateInSettingsAsset,
                selectActiveObject = selectActiveObject,
            };
        }

        internal bool TryValidate(out string diagnostic)
        {
            if (string.IsNullOrWhiteSpace(dataFolderPath))
            {
                diagnostic = "dataFolderPath is required.";
                return false;
            }

            string normalizedPath = dataFolderPath.Replace('\\', '/').TrimEnd('/');
            if (
                !normalizedPath.Equals("Assets", StringComparison.Ordinal)
                && !normalizedPath.StartsWith("Assets/", StringComparison.Ordinal)
            )
            {
                diagnostic = "dataFolderPath must be inside the Assets folder.";
                return false;
            }

            dataFolderPath = normalizedPath;
            diagnostic = string.Empty;
            return true;
        }
    }

    [Serializable]
    public sealed class DataVisualizerTypeDescriptor
    {
        public string assemblyQualifiedName;
        public string fullName;
        public string name;
    }

    [Serializable]
    public sealed class DataVisualizerAssetMetadata
    {
        public string guid;
        public string path;
        public string assemblyQualifiedTypeName;
        public string displayName;
    }

    [Serializable]
    public sealed class DataVisualizerAssetMetadataPage
    {
        public int page;
        public int pageSize;
        public int totalCount;
        public bool isComplete;
        public List<DataVisualizerAssetMetadata> items = new();
    }

    [Serializable]
    public sealed class DataVisualizerOperationResult
    {
        public bool succeeded;
        public string diagnostic;

        public static DataVisualizerOperationResult Success()
        {
            return new DataVisualizerOperationResult
            {
                succeeded = true,
                diagnostic = string.Empty,
            };
        }

        public static DataVisualizerOperationResult Failure(string message)
        {
            return new DataVisualizerOperationResult
            {
                succeeded = false,
                diagnostic = message ?? "The operation failed.",
            };
        }
    }

    public static class DataVisualizerAutomation
    {
        private const string SettingsPath = "Assets/Editor/DataVisualizerSettings.asset";

        public static DataVisualizerConfiguration ReadConfiguration()
        {
            DataVisualizerSettings settings = LoadSettings();
            return new DataVisualizerConfiguration
            {
                dataFolderPath = settings.DataFolderPath,
                persistStateInSettingsAsset = settings.persistStateInSettingsAsset,
                selectActiveObject = settings.selectActiveObject,
            };
        }

        public static DataVisualizerOperationResult ApplyConfiguration(
            DataVisualizerConfiguration configuration
        )
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return DataVisualizerOperationResult.Failure(
                    "Configuration cannot be applied while entering or in Play Mode."
                );
            }

            if (configuration == null)
            {
                return DataVisualizerOperationResult.Failure("Configuration is required.");
            }

            DataVisualizerConfiguration copy = configuration.Clone();
            if (!copy.TryValidate(out string diagnostic))
            {
                return DataVisualizerOperationResult.Failure(diagnostic);
            }

            DataVisualizerSettings settings = LoadSettings();
            settings._dataFolderPath = copy.dataFolderPath;
            settings.persistStateInSettingsAsset = copy.persistStateInSettingsAsset;
            settings.selectActiveObject = copy.selectActiveObject;
            settings.MarkDirty();
            AssetDatabase.SaveAssets();
            return DataVisualizerOperationResult.Success();
        }

        public static string ExportConfigurationJson()
        {
            return JsonUtility.ToJson(ReadConfiguration(), true);
        }

        public static DataVisualizerOperationResult ImportConfigurationJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return DataVisualizerOperationResult.Failure("Configuration JSON is required.");
            }

            try
            {
                DataVisualizerConfiguration configuration =
                    JsonUtility.FromJson<DataVisualizerConfiguration>(json);
                return ApplyConfiguration(configuration);
            }
            catch (ArgumentException exception)
            {
                return DataVisualizerOperationResult.Failure(
                    $"Configuration JSON is malformed: {exception.Message}"
                );
            }
        }

        public static IReadOnlyList<DataVisualizerTypeDescriptor> DiscoverManagedTypes()
        {
            return TypeCache
                .GetTypesDerivedFrom<ScriptableObject>()
                .Where(IsManagedType)
                .Select(type => new DataVisualizerTypeDescriptor
                {
                    assemblyQualifiedName = type.AssemblyQualifiedName,
                    fullName = type.FullName ?? type.Name,
                    name = type.Name,
                })
                .OrderBy(type => type.assemblyQualifiedName, StringComparer.Ordinal)
                .ToArray();
        }

        public static DataVisualizerAssetMetadataPage QueryAssetMetadata(
            string assemblyQualifiedTypeName,
            int page,
            int pageSize,
            string searchFolder = null
        )
        {
            if (page < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(page));
            }

            if (pageSize is < 1 or > 500)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            }

            string[] searchFolders = null;
            if (!string.IsNullOrWhiteSpace(searchFolder))
            {
                searchFolder = searchFolder.Replace('\\', '/').TrimEnd('/');
                if (!AssetDatabase.IsValidFolder(searchFolder))
                {
                    throw new ArgumentException(
                        "searchFolder must be a valid project folder.",
                        nameof(searchFolder)
                    );
                }

                searchFolders = new[] { searchFolder };
            }

            DataVisualizerAssetMetadata[] metadata = AssetDatabase
                .FindAssets("t:ScriptableObject", searchFolders)
                .Select(guid => new { guid, path = AssetDatabase.GUIDToAssetPath(guid) })
                .Where(asset => !string.IsNullOrWhiteSpace(asset.path))
                .Select(asset =>
                {
                    Type type = AssetDatabase.GetMainAssetTypeAtPath(asset.path);
                    return new DataVisualizerAssetMetadata
                    {
                        guid = asset.guid,
                        path = asset.path,
                        assemblyQualifiedTypeName = type?.AssemblyQualifiedName,
                        displayName = Path.GetFileNameWithoutExtension(asset.path),
                    };
                })
                .Where(asset =>
                    string.IsNullOrWhiteSpace(assemblyQualifiedTypeName)
                    || string.Equals(
                        asset.assemblyQualifiedTypeName,
                        assemblyQualifiedTypeName,
                        StringComparison.Ordinal
                    )
                )
                .OrderBy(asset => asset.path, StringComparer.Ordinal)
                .ThenBy(asset => asset.guid, StringComparer.Ordinal)
                .ToArray();

            int offset = checked(page * pageSize);
            DataVisualizerAssetMetadataPage result = new()
            {
                page = page,
                pageSize = pageSize,
                totalCount = metadata.Length,
                isComplete = true,
            };
            result.items.AddRange(metadata.Skip(offset).Take(pageSize));
            return result;
        }

        public static DataVisualizerOperationResult RefreshAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return DataVisualizerOperationResult.Failure(
                    "Asset refresh cannot be requested while entering or in Play Mode."
                );
            }

            AssetDatabase.Refresh();
            return DataVisualizerOperationResult.Success();
        }

        public static DataVisualizerOperationResult SelectAsset(string guid)
        {
            ScriptableObject asset = LoadAsset(guid, out string diagnostic);
            if (asset == null)
            {
                return DataVisualizerOperationResult.Failure(diagnostic);
            }

            Selection.activeObject = asset;
            return DataVisualizerOperationResult.Success();
        }

        public static DataVisualizerOperationResult OpenAsset(string guid)
        {
            ScriptableObject asset = LoadAsset(guid, out string diagnostic);
            if (asset == null)
            {
                return DataVisualizerOperationResult.Failure(diagnostic);
            }

            return AssetDatabase.OpenAsset(asset)
                ? DataVisualizerOperationResult.Success()
                : DataVisualizerOperationResult.Failure(
                    "Unity could not open the requested asset."
                );
        }

        private static ScriptableObject LoadAsset(string guid, out string diagnostic)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                diagnostic = "Asset selection cannot run while entering or in Play Mode.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(guid))
            {
                diagnostic = "Asset GUID is required.";
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject asset = string.IsNullOrWhiteSpace(path)
                ? null
                : AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
            diagnostic =
                asset == null ? "The requested ScriptableObject asset was not found." : null;
            return asset;
        }

        private static bool IsManagedType(Type type)
        {
            return type != null
                && type != typeof(ScriptableObject)
                && !type.IsAbstract
                && !type.IsGenericType
                && !type.IsInterface
                && !type.IsNestedPrivate
                && !typeof(Editor).IsAssignableFrom(type)
                && !typeof(EditorWindow).IsAssignableFrom(type)
                && type.Namespace?.StartsWith("UnityEditor", StringComparison.Ordinal) != true
                && type.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) != true;
        }

        private static DataVisualizerSettings LoadSettings()
        {
            DataVisualizerSettings settings = AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(
                SettingsPath
            );
            if (settings != null)
            {
                return settings;
            }

            string directory = Path.GetDirectoryName(SettingsPath)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(directory) && !AssetDatabase.IsValidFolder(directory))
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(directory));
            }

            settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(SettingsPath) ?? settings;
        }
    }
}
