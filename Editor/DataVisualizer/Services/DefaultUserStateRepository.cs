namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.IO;
    using Data;
    using UnityEditor;
    using UnityEngine;

    internal sealed class DefaultUserStateRepository : IUserStateRepository
    {
        private readonly string _userStateFilePath;
        private DataVisualizerSettings _settingsCache;
        private DataVisualizerUserState _userStateCache;

        public DefaultUserStateRepository(string userStateFilePath)
        {
            _userStateFilePath = userStateFilePath;
        }

        public DataVisualizerSettings LoadSettings()
        {
            if (_settingsCache == null)
            {
                _settingsCache = DataVisualizer.LoadOrCreateSettings();
            }

            return _settingsCache;
        }

        public DataVisualizerUserState LoadUserState()
        {
            if (_userStateCache != null)
            {
                return _userStateCache;
            }

            if (string.IsNullOrWhiteSpace(_userStateFilePath))
            {
                _userStateCache = new DataVisualizerUserState();
                return _userStateCache;
            }

            if (!File.Exists(_userStateFilePath))
            {
                _userStateCache = new DataVisualizerUserState();
                return _userStateCache;
            }

            try
            {
                string json = File.ReadAllText(_userStateFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _userStateCache = new DataVisualizerUserState();
                    return _userStateCache;
                }

                DataVisualizerUserState loaded = JsonUtility.FromJson<DataVisualizerUserState>(
                    json
                );
                _userStateCache = loaded ?? new DataVisualizerUserState();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Error loading user state from '{_userStateFilePath}': {exception}. Using default state."
                );
                _userStateCache = new DataVisualizerUserState();
            }

            return _userStateCache;
        }

        public void SaveSettings(DataVisualizerSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            _settingsCache = settings;
            settings.MarkDirty();
            AssetDatabase.SaveAssets();
        }

        public void SaveUserState(DataVisualizerUserState userState)
        {
            if (userState == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_userStateFilePath))
            {
                return;
            }

            _userStateCache = userState;
            try
            {
                string directory = Path.GetDirectoryName(_userStateFilePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(userState, true);
                File.WriteAllText(_userStateFilePath, json);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Error saving user state to '{_userStateFilePath}': {exception}");
            }
        }

        public bool ShouldPersistStateInSettingsAsset()
        {
            DataVisualizerSettings settings = LoadSettings();
            return settings != null && settings.persistStateInSettingsAsset;
        }
    }
}
