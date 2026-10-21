namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using Data;
    using UnityEditor;

    /// <summary>Explicit settings preferences shared by the browser and editor automation.</summary>
    public sealed class EditorConfigurationService
    {
        public string DataFolderPath => Settings.DataFolderPath;
        public bool SelectActiveObject => Settings.selectActiveObject;
        public string SettingsPath => AssetDatabase.GetAssetPath(Settings);
        public string SettingsGuid => AssetDatabase.AssetPathToGUID(SettingsPath);

        private DataVisualizerSettings Settings => _settings != null ? _settings : throw new ObjectDisposedException(nameof(DataVisualizerSettings));

        private readonly DataVisualizerSettings _settings;

        public EditorConfigurationService(DataVisualizerSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            _settings = settings;
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
                throw new InvalidOperationException("editor_busy: Wait for compilation and asset updates before resolving settings.");
            }
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(DataVisualizerSettings))
            {
                throw new ArgumentException("The GUID must identify a DataVisualizerSettings main asset under Assets.", nameof(guid));
            }
            return new EditorConfigurationService(AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(path));
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

        public bool SetSelectActiveObject(bool value)
        {
            return ApplyValues(DataFolderPath, value);
        }

        public bool SetDataFolderPath(string path)
        {
            ValidateDataFolder(path);
            return ApplyValues(path, SelectActiveObject);
        }

        private bool ApplyValues(string path, bool selectActiveObject)
        {
            DataVisualizerSettings settings = Settings;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("play_mode: Configuration cannot change during Play Mode or its transition.");
            }
            if (AssetGuidDiscovery.IsAssetDatabaseBusy())
            {
                throw new InvalidOperationException("editor_busy: Wait for compilation and asset updates before applying configuration.");
            }
            string settingsPath = AssetDatabase.GetAssetPath(settings);
            if (!string.IsNullOrEmpty(settingsPath) && (!settingsPath.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsOpenForEdit(settings)))
            {
                throw new InvalidOperationException("read_only: The settings asset must be writable under Assets.");
            }
            bool folderChanged = !string.Equals(settings.DataFolderPath, path, StringComparison.Ordinal);
            bool selectionChanged = settings.selectActiveObject != selectActiveObject;
            if (!folderChanged && !selectionChanged)
            {
                return false;
            }
            settings._dataFolderPath = path;
            settings.selectActiveObject = selectActiveObject;
            settings.MarkDirty();
            if (!string.IsNullOrEmpty(settingsPath))
            {
                AssetDatabase.SaveAssetIfDirty(settings);
            }
            return true;
        }
    }
}
