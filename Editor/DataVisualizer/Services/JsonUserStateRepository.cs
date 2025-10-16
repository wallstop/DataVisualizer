namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.IO;
    using Data;
    using UnityEditor;
    using UnityEngine;

    internal sealed class JsonUserStateRepository : IUserStateRepository
    {
        private readonly string _userStateFilePath;
        private DataVisualizerSettings _settingsCache;
        private DataVisualizerUserState _userStateCache;
        private readonly ScriptableAssetSaveScheduler _saveScheduler;
        private bool _jsonSavePending;

        public JsonUserStateRepository(
            string userStateFilePath,
            ScriptableAssetSaveScheduler saveScheduler
        )
        {
            if (string.IsNullOrWhiteSpace(userStateFilePath))
            {
                throw new ArgumentException(
                    "User state file path cannot be null or whitespace.",
                    nameof(userStateFilePath)
                );
            }

            _userStateFilePath = userStateFilePath;
            _saveScheduler = saveScheduler ?? throw new ArgumentNullException(nameof(saveScheduler));
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
            if (_saveScheduler != null)
            {
                _saveScheduler.ScheduleAssetDatabaseSave();
            }
            else
            {
                AssetDatabase.SaveAssets();
            }
        }

        public void SaveUserState(DataVisualizerUserState userState)
        {
            if (userState == null)
            {
                return;
            }

            _userStateCache = userState;

            if (_saveScheduler != null)
            {
                if (!_jsonSavePending)
                {
                    _jsonSavePending = true;
                    _saveScheduler.Schedule(WriteUserStateToDisk);
                }
            }
            else
            {
                WriteUserStateToDisk();
            }
        }

        private void WriteUserStateToDisk()
        {
            _jsonSavePending = false;

            try
            {
                string directory = Path.GetDirectoryName(_userStateFilePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(_userStateCache, true);
                File.WriteAllText(_userStateFilePath, json);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Error saving user state to '{_userStateFilePath}': {exception}");
            }
        }
    }
}
