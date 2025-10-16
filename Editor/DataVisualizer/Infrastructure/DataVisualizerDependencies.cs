namespace WallstopStudios.DataVisualizer.Editor.Infrastructure
{
    using System;
    using Data;
    using Events;
    using Services;
    using State;

    internal sealed class DataVisualizerDependencies
    {
        public DataVisualizerDependencies(
            string userStateFilePath,
            VisualizerSessionState sessionState,
            ScriptableAssetSaveScheduler saveScheduler
        )
        {
            if (sessionState == null)
            {
                throw new ArgumentNullException(nameof(sessionState));
            }

            if (string.IsNullOrWhiteSpace(userStateFilePath))
            {
                throw new ArgumentException(
                    "User state file path cannot be null or whitespace.",
                    nameof(userStateFilePath)
                );
            }

            UserStateFilePath = userStateFilePath;
            SessionState = sessionState;
            SaveScheduler = saveScheduler ?? new ScriptableAssetSaveScheduler();
            InitializeRepositories();
            AssetService = new DataAssetService();
        }

        public VisualizerSessionState SessionState { get; }

        public IDataAssetService AssetService { get; }

        public DataVisualizerEventHub EventHub { get; } = new DataVisualizerEventHub();

        public IUserStateRepository UserStateRepository { get; private set; }

        public DataVisualizerSettings Settings { get; private set; }

        public DataVisualizerUserState UserState { get; private set; }

        public string UserStateFilePath { get; private set; }

        public ScriptableAssetSaveScheduler SaveScheduler { get; }

        public void RefreshUserState()
        {
            if (UserStateRepository == null)
            {
                return;
            }

            Settings = UserStateRepository.LoadSettings();
            UserState = UserStateRepository.LoadUserState();
        }

        public void UpdatePersistenceStrategy(bool persistInSettingsAsset, string userStateFilePath)
        {
            if (string.IsNullOrWhiteSpace(userStateFilePath))
            {
                throw new ArgumentException(
                    "User state file path cannot be null or whitespace.",
                    nameof(userStateFilePath)
                );
            }

            UserStateFilePath = userStateFilePath;
            if (persistInSettingsAsset)
            {
                UserStateRepository = new SettingsAssetStateRepository(SaveScheduler);
            }
            else
            {
                UserStateRepository = new JsonUserStateRepository(UserStateFilePath, SaveScheduler);
            }

            RefreshUserState();
        }

        private void InitializeRepositories()
        {
            DataVisualizerSettings existingSettings = DataVisualizer.LoadOrCreateSettings();
            if (existingSettings == null)
            {
                existingSettings = new DataVisualizerSettings();
            }

            if (existingSettings.persistStateInSettingsAsset)
            {
                UserStateRepository = new SettingsAssetStateRepository(SaveScheduler);
            }
            else
            {
                UserStateRepository = new JsonUserStateRepository(UserStateFilePath, SaveScheduler);
            }

            Settings = UserStateRepository.LoadSettings() ?? existingSettings;
            UserState = UserStateRepository.LoadUserState();
        }
    }
}
