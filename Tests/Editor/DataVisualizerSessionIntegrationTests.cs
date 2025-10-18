#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using Controllers;
    using Data;
    using Events;
    using NUnit.Framework;
    using Services;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class DataVisualizerSessionIntegrationTests
    {
        private sealed class DummyScriptableObject : ScriptableObject { }

        [Test]
        public void SelectTypePersistsSessionState()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;

                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                try
                {
                    settings.persistStateInSettingsAsset = true;
                    DataVisualizerUserState userState = new DataVisualizerUserState();

                    StubUserStateRepository repository = new StubUserStateRepository
                    {
                        Settings = settings,
                        UserState = userState,
                    };

                    dataVisualizer.OverrideUserStateRepositoryForTesting(
                        repository,
                        repository.Settings,
                        repository.UserState
                    );

                    dataVisualizer._scriptableObjectTypes.Clear();
                    dataVisualizer._namespaceOrder.Clear();
                    dataVisualizer._scriptableObjectTypes["TestNamespace"] =
                        new System.Collections.Generic.List<System.Type>
                        {
                            typeof(DummyScriptableObject),
                        };
                    dataVisualizer._namespaceOrder["TestNamespace"] = 0;

                    DataVisualizerEventHub eventHub = new DataVisualizerEventHub();
                    NamespacePanelController controller = new NamespacePanelController(
                        dataVisualizer,
                        dataVisualizer._namespaceController,
                        dataVisualizer.SessionState,
                        eventHub
                    );

                    try
                    {
                        controller.BuildNamespaceView();
                        controller.SelectType(typeof(DummyScriptableObject));

                        Assert.AreEqual(
                            "TestNamespace",
                            dataVisualizer.SessionState.Selection.SelectedNamespaceKey
                        );
                        Assert.AreEqual(
                            typeof(DummyScriptableObject).FullName,
                            dataVisualizer.SessionState.Selection.SelectedTypeFullName
                        );
                        Assert.AreEqual("TestNamespace", settings.lastSelectedNamespaceKey);
                        Assert.AreEqual(
                            typeof(DummyScriptableObject).FullName,
                            settings.lastSelectedTypeName
                        );
                    }
                    finally
                    {
                        controller.Dispose();
                    }
                }
                finally
                {
                    Object.DestroyImmediate(settings);
                }
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        private sealed class StubUserStateRepository : IUserStateRepository
        {
            public DataVisualizerSettings Settings { get; set; }

            public DataVisualizerUserState UserState { get; set; }

            public DataVisualizerSettings LoadSettings()
            {
                return Settings;
            }

            public DataVisualizerUserState LoadUserState()
            {
                return UserState;
            }

            public void SaveSettings(DataVisualizerSettings settings) { }

            public void SaveUserState(DataVisualizerUserState userState) { }
        }

        private sealed class StubDataAssetService : IDataAssetService
        {
            public event Action<DataAssetChangeEventArgs> AssetsChanged;

            public void ConfigureTrackedTypes(IEnumerable<Type> types) { }

            public void MarkDirty() { }

            public void ForceRebuild() { }

            public int GetAssetCount(Type type)
            {
                return 0;
            }

            public DataAssetPage GetAssetsPage(Type type, int offset, int count)
            {
                return new DataAssetPage(type, Array.Empty<DataAssetMetadata>(), 0, 0);
            }

            public IReadOnlyList<DataAssetMetadata> GetAssetsForType(Type type)
            {
                return Array.Empty<DataAssetMetadata>();
            }

            public IReadOnlyList<string> GetGuidsForType(Type type)
            {
                return Array.Empty<string>();
            }

            public IEnumerable<DataAssetMetadata> GetAllAssets()
            {
                return Array.Empty<DataAssetMetadata>();
            }

            public bool TryGetAssetByGuid(string guid, out DataAssetMetadata metadata)
            {
                metadata = null;
                return false;
            }

            public bool TryGetAssetByPath(string path, out DataAssetMetadata metadata)
            {
                metadata = null;
                return false;
            }

            public void RefreshAsset(string guid) { }

            public void RefreshType(Type type) { }

            public void RemoveAsset(string guid) { }

            public IReadOnlyCollection<string> EnumerateLabels(Type type)
            {
                return Array.Empty<string>();
            }
        }

        [Test]
        public void UpdateSearchOptionsFromSettingsUsesSettingsValuesWhenPersistingInAsset()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            DataVisualizerSettings settings = null;
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;

                settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
                settings.persistStateInSettingsAsset = true;
                settings.enableFuzzySearch = false;
                settings.fuzzyMatchThreshold = 0.1f;

                DataVisualizerUserState userState = new DataVisualizerUserState();
                StubUserStateRepository repository = new StubUserStateRepository
                {
                    Settings = settings,
                    UserState = userState,
                };

                dataVisualizer.OverrideUserStateRepositoryForTesting(
                    repository,
                    settings,
                    userState
                );

                SearchService searchService = new SearchService(new StubDataAssetService());
                dataVisualizer.OverrideSearchServiceForTesting(searchService);

                Assert.IsFalse(searchService.EnableFuzzyMatching);
                Assert.AreEqual(0.3f, searchService.FuzzyMatchThreshold);
            }
            finally
            {
                if (settings != null)
                {
                    Object.DestroyImmediate(settings);
                }

                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void UpdateSearchOptionsFromSettingsUsesUserStateWhenPersistingToFile()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            DataVisualizerSettings settings = null;
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;

                settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
                settings.persistStateInSettingsAsset = false;

                DataVisualizerUserState userState = new DataVisualizerUserState
                {
                    enableFuzzySearch = true,
                    fuzzyMatchThreshold = 0.85f,
                };
                StubUserStateRepository repository = new StubUserStateRepository
                {
                    Settings = settings,
                    UserState = userState,
                };

                dataVisualizer.OverrideUserStateRepositoryForTesting(
                    repository,
                    settings,
                    userState
                );

                SearchService searchService = new SearchService(new StubDataAssetService());
                dataVisualizer.OverrideSearchServiceForTesting(searchService);

                Assert.IsTrue(searchService.EnableFuzzyMatching);
                Assert.AreEqual(0.85f, searchService.FuzzyMatchThreshold);
            }
            finally
            {
                if (settings != null)
                {
                    Object.DestroyImmediate(settings);
                }

                Object.DestroyImmediate(dataVisualizer);
            }
        }
    }
}
