namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using Data;
    using NUnit.Framework;
    using Services;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class DataVisualizerPersistenceTests
    {
        private sealed class StubUserStateRepository : IUserStateRepository
        {
            public DataVisualizerSettings Settings { get; set; }

            public DataVisualizerUserState UserState { get; set; }

            public int SaveSettingsCallCount { get; private set; }

            public int SaveUserStateCallCount { get; private set; }

            public int LoadSettingsCallCount { get; private set; }

            public int LoadUserStateCallCount { get; private set; }

            public DataVisualizerSettings LoadSettings()
            {
                LoadSettingsCallCount++;
                return Settings;
            }

            public DataVisualizerUserState LoadUserState()
            {
                LoadUserStateCallCount++;
                return UserState;
            }

            public void SaveSettings(DataVisualizerSettings settings)
            {
                SaveSettingsCallCount++;
            }

            public void SaveUserState(DataVisualizerUserState userState)
            {
                SaveUserStateCallCount++;
            }
        }

        [Test]
        public void PersistSettingsUsesSettingsWhenPersistenceEnabled()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                settings.persistStateInSettingsAsset = true;

                DataVisualizerUserState userState = new DataVisualizerUserState();

                StubUserStateRepository repository = new StubUserStateRepository
                {
                    Settings = settings,
                    UserState = userState,
                };

                dataVisualizer._userStateRepository = repository;

                bool appliedSettings = false;
                dataVisualizer.PersistSettings(
                    currentSettings =>
                    {
                        appliedSettings = true;
                        return true;
                    },
                    currentUserState => false
                );

                Assert.IsTrue(appliedSettings);
                Assert.AreEqual(1, repository.SaveSettingsCallCount);
                Assert.AreEqual(0, repository.SaveUserStateCallCount);
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void PersistSettingsUsesUserStateWhenPersistenceDisabled()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                settings.persistStateInSettingsAsset = false;

                DataVisualizerUserState userState = new DataVisualizerUserState();

                StubUserStateRepository repository = new StubUserStateRepository
                {
                    Settings = settings,
                    UserState = userState,
                };

                dataVisualizer._userStateRepository = repository;

                bool appliedUserState = false;
                dataVisualizer.PersistSettings(
                    currentSettings => false,
                    currentUserState =>
                    {
                        appliedUserState = true;
                        return true;
                    }
                );

                Assert.IsTrue(appliedUserState);
                Assert.AreEqual(0, repository.SaveSettingsCallCount);
                Assert.AreEqual(1, repository.SaveUserStateCallCount);
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void LoadUserStateFromFileUsesRepository()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                StubUserStateRepository repository = new StubUserStateRepository
                {
                    UserState = new DataVisualizerUserState(),
                };

                dataVisualizer._userStateRepository = repository;

                dataVisualizer.LoadUserStateFromFile();

                Assert.AreEqual(1, repository.LoadUserStateCallCount);
                Assert.AreSame(repository.UserState, dataVisualizer.UserState);
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void SaveUserStateToFileUsesRepository()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                DataVisualizerUserState userState = new DataVisualizerUserState();
                StubUserStateRepository repository = new StubUserStateRepository
                {
                    UserState = userState,
                };

                dataVisualizer._userStateRepository = repository;

                dataVisualizer.SaveUserStateToFile();

                Assert.AreEqual(1, repository.SaveUserStateCallCount);
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }
    }
}
