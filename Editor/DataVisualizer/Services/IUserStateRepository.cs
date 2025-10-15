namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using Data;

    internal interface IUserStateRepository
    {
        DataVisualizerSettings LoadSettings();

        DataVisualizerUserState LoadUserState();

        void SaveSettings(DataVisualizerSettings settings);

        void SaveUserState(DataVisualizerUserState userState);

        bool ShouldPersistStateInSettingsAsset();
    }
}
