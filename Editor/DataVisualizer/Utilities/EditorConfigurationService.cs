namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.IO;
    using Data;
    using Styles;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Explicit settings preferences shared by the browser and editor automation.</summary>
    public sealed class EditorConfigurationService
    {
        public string DataFolderPath => Settings.DataFolderPath;
        public bool SelectActiveObject => Settings.selectActiveObject;
        public bool PersistStateInSettingsAsset => Settings.persistStateInSettingsAsset;
        public string ThemeGuid => PersistStateInSettingsAsset ? Settings.themeGuid : RequireUserState().themeGuid;
        public string UserStatePath => _userStatePath;
        public string SettingsPath => AssetDatabase.GetAssetPath(Settings);
        public string SettingsGuid => AssetDatabase.AssetPathToGUID(SettingsPath);

        private DataVisualizerSettings Settings => _settings != null ? _settings : throw new ObjectDisposedException(nameof(DataVisualizerSettings));

        private readonly DataVisualizerSettings _settings;
        private readonly DataVisualizerUserState _userState;
        private readonly string _userStatePath;

        public EditorConfigurationService(DataVisualizerSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            _settings = settings;
        }

        public EditorConfigurationService(DataVisualizerSettings settings, DataVisualizerUserState userState, string userStatePath)
            : this(settings)
        {
            _userState = userState ?? throw new ArgumentNullException(nameof(userState));
            _userStatePath = ValidateUserStatePath(userStatePath);
        }

        /// <summary>Resolves an explicit persistent settings main asset without opening a window.</summary>
        public static EditorConfigurationService FromGuid(string guid)
        {
            if (!Guid.TryParseExact(guid, "N", out _))
            {
                throw new ArgumentException("Specify a 32-hex-digit settings asset GUID.", nameof(guid));
            }
            if (AssetGuidDiscovery.IsAssetDatabaseBusy())
            {
                throw new EditorConfigurationException("editor_busy", "Wait for compilation and asset updates before resolving settings.");
            }
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(DataVisualizerSettings))
            {
                throw new ArgumentException("The GUID must identify a DataVisualizerSettings main asset under Assets.", nameof(guid));
            }
            return new EditorConfigurationService(AssetDatabase.LoadMainAssetAtPath(path) as DataVisualizerSettings);
        }

        /// <summary>Loads an explicit user-state file, or uses empty state when it does not exist. Does not write either store.</summary>
        public static EditorConfigurationService FromGuid(string guid, string userStatePath)
        {
            EditorConfigurationService service = FromGuid(guid);
            string path = ValidateUserStatePath(userStatePath);
            DataVisualizerUserState state = File.Exists(path)
                ? DataVisualizerUserState.FromJson(File.ReadAllText(path)) ?? throw new ArgumentException("User-state JSON is invalid.", nameof(userStatePath))
                : new DataVisualizerUserState();
            return new EditorConfigurationService(service._settings, state, path);
        }

        /// <summary>Returns an absolute JSON path outside protected project folders. The parent must already exist.</summary>
        public static string ValidateUserStatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Specify a user-state .json file.", nameof(path));
            }
            string project = Path.GetDirectoryName(Application.dataPath);
            string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(project, path));
            foreach (string directory in new[] { "Assets", "Packages", "ProjectSettings" })
            {
                string root = Path.Combine(project, directory);
                if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("User-state files must be outside Assets, Packages and ProjectSettings.", nameof(path));
                }
            }
            if (Directory.Exists(full) || !Directory.Exists(Path.GetDirectoryName(full)))
            {
                throw new ArgumentException("The user-state parent directory must exist and the target must be a file.", nameof(path));
            }
            return full;
        }

        /// <summary>Validates an exact theme main-asset GUID without loading it. Empty selects the default theme.</summary>
        public static void ValidateThemeGuid(string guid)
        {
            if (guid == string.Empty)
            {
                return;
            }
            if (guid == null || guid.Length != 32 || !Guid.TryParseExact(guid, "N", out _))
            {
                throw new ArgumentException("Specify a theme GUID or an empty string for the default theme.", nameof(guid));
            }
            if (AssetGuidDiscovery.IsAssetDatabaseBusy())
            {
                throw new EditorConfigurationException("editor_busy", "Wait for compilation and asset updates before resolving themes.");
            }
            if (AssetDatabase.GetMainAssetTypeAtPath(AssetDatabase.GUIDToAssetPath(guid)) != typeof(DataVisualizerThemeSettings))
            {
                throw new ArgumentException("The GUID must identify a theme main asset.", nameof(guid));
            }
        }

        /// <summary>Checks a proposed folder without creating, dirtying or saving anything.</summary>
        public static void ValidateDataFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || (path != "Assets" && !path.StartsWith("Assets/", StringComparison.Ordinal)) || path.Contains('\\'))
            {
                throw new ArgumentException("Use a canonical existing folder under Assets.", nameof(path));
            }
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    throw new ArgumentException("Folder segments must be nonempty names without traversal.", nameof(path));
                }
            }
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!AssetDatabase.IsValidFolder(path) || !string.Equals(AssetDatabase.GUIDToAssetPath(guid), path, StringComparison.Ordinal))
            {
                throw new ArgumentException("The folder must exist with the exact canonical Assets path.", nameof(path));
            }
        }

        /// <summary>Applies both preferences after validation; returns false for an unchanged request.</summary>
        public bool Apply(string dataFolderPath, bool selectActiveObject)
        {
            ValidateDataFolder(dataFolderPath);
            return ApplyValues(dataFolderPath, selectActiveObject);
        }

        /// <summary>Applies all preferences in one synchronous unit. File/save failures may leave partial writes; no rollback is promised.</summary>
        public bool Apply(string dataFolderPath, bool selectActiveObject, bool persistStateInSettingsAsset, string themeGuid)
        {
            ValidateDataFolder(dataFolderPath);
            ValidateThemeGuid(themeGuid);
            return ApplyValues(dataFolderPath, selectActiveObject, persistStateInSettingsAsset, themeGuid.ToLowerInvariant());
        }

        public bool SetPersistenceMode(bool persistStateInSettingsAsset)
        {
            return ApplyValues(DataFolderPath, SelectActiveObject, persistStateInSettingsAsset);
        }

        public bool SetThemeGuid(string guid)
        {
            ValidateThemeGuid(guid);
            return ApplyValues(DataFolderPath, SelectActiveObject, themeGuid: guid.ToLowerInvariant());
        }

        public bool SetSelectActiveObject(bool value)
        {
            return ApplyValues(DataFolderPath, value);
        }

        public bool SetDataFolderPath(string path)
        {
            ValidateDataFolder(path);
            return ApplyValues(path, SelectActiveObject);
        }

        private DataVisualizerUserState RequireUserState()
        {
            return _userState ?? throw new ArgumentException("Supply an explicit user-state instance and file path for state configuration.");
        }

        private bool ApplyValues(string path, bool selectActiveObject, bool? persistStateInSettingsAsset = null, string themeGuid = null)
        {
            DataVisualizerSettings settings = Settings;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new EditorConfigurationException("play_mode", "Configuration cannot change during Play Mode or its transition.");
            }
            if (AssetGuidDiscovery.IsAssetDatabaseBusy())
            {
                throw new EditorConfigurationException("editor_busy", "Wait for compilation and asset updates before applying configuration.");
            }
            string settingsPath = AssetDatabase.GetAssetPath(settings);
            if (!string.IsNullOrEmpty(settingsPath) && (!settingsPath.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsOpenForEdit(settings)))
            {
                throw new EditorConfigurationException("read_only", "The settings asset must be writable under Assets.");
            }
            bool mode = persistStateInSettingsAsset ?? settings.persistStateInSettingsAsset;
            bool modeChanged = mode != settings.persistStateInSettingsAsset;
            bool stateRequested = modeChanged || (themeGuid != null && !mode);
            if (stateRequested)
            {
                RequireUserState();
                ValidateUserStatePath(_userStatePath);
            }
            string previousTheme = settings.persistStateInSettingsAsset ? settings.themeGuid : (stateRequested ? _userState.themeGuid : null);
            bool themeChanged = themeGuid != null && !string.Equals(previousTheme, themeGuid, StringComparison.Ordinal);
            bool folderChanged = !string.Equals(settings.DataFolderPath, path, StringComparison.Ordinal);
            bool selectionChanged = settings.selectActiveObject != selectActiveObject;
            bool settingsChanged = folderChanged || selectionChanged || modeChanged || (themeChanged && mode);
            bool userChanged = !mode && (modeChanged || themeChanged);
            if (!settingsChanged && !userChanged)
            {
                return false;
            }
            if (userChanged)
            {
                if (File.Exists(_userStatePath) && (File.GetAttributes(_userStatePath) & FileAttributes.ReadOnly) != 0)
                {
                    throw new EditorConfigurationException("read_only", "The user-state file must be writable.");
                }
                DataVisualizerUserState candidate;
                if (modeChanged)
                {
                    candidate = new DataVisualizerUserState();
                    candidate.HydrateFrom(settings);
                }
                else
                {
                    candidate = JsonUtility.FromJson<DataVisualizerUserState>(JsonUtility.ToJson(_userState));
                }
                if (themeGuid != null)
                {
                    candidate.themeGuid = themeGuid;
                }
                File.WriteAllText(_userStatePath, JsonUtility.ToJson(candidate, true));
            }
            if (modeChanged)
            {
                if (mode)
                {
                    settings.HydrateFrom(_userState);
                }
                else
                {
                    _userState.HydrateFrom(settings);
                }
            }
            if (themeGuid != null)
            {
                if (mode)
                {
                    settings.themeGuid = themeGuid;
                }
                else
                {
                    _userState.themeGuid = themeGuid;
                }
            }
            settings.persistStateInSettingsAsset = mode;
            settings._dataFolderPath = path;
            settings.selectActiveObject = selectActiveObject;
            if (settingsChanged)
            {
                settings.MarkDirty();
                if (!string.IsNullOrEmpty(settingsPath))
                {
                    AssetDatabase.SaveAssetIfDirty(settings);
                }
            }
            return true;
        }
    }
}
