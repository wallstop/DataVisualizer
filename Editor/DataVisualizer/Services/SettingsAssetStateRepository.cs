namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using Data;
    using UnityEditor;

    internal sealed class SettingsAssetStateRepository : IUserStateRepository
    {
        private DataVisualizerSettings _settingsCache;
        private DataVisualizerUserState _userStateCache;
        private readonly ScriptableAssetSaveScheduler _saveScheduler;

        public SettingsAssetStateRepository(ScriptableAssetSaveScheduler saveScheduler)
        {
            _saveScheduler =
                saveScheduler ?? throw new ArgumentNullException(nameof(saveScheduler));
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

            DataVisualizerSettings settings = LoadSettings();
            if (settings == null)
            {
                _userStateCache = new DataVisualizerUserState();
                return _userStateCache;
            }

            DataVisualizerUserState userState = new DataVisualizerUserState();
            userState.HydrateFrom(settings);
            _userStateCache = userState;
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
            _saveScheduler.ScheduleAssetDatabaseSave();
        }

        public void SaveUserState(DataVisualizerUserState userState)
        {
            if (userState == null)
            {
                return;
            }

            DataVisualizerSettings settings = LoadSettings();
            if (settings == null)
            {
                return;
            }

            settings.HydrateFrom(userState);
            settings.MarkDirty();
            _saveScheduler.ScheduleAssetDatabaseSave();
            _userStateCache = userState;
        }
    }
}
