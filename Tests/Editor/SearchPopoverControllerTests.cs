#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using Controllers;
    using Data;
    using NUnit.Framework;
    using Search;
    using Services;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class SearchPopoverControllerTests
    {
        [Test]
        public void FuzzyToggleChangeDisablesMatchingInSettingsAndService()
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
                    settings.enableFuzzySearch = true;
                    settings.fuzzyMatchThreshold = 0.6f;
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

                    StubAssetService assetService = new StubAssetService();
                    SearchService searchService = new SearchService(assetService);
                    dataVisualizer.OverrideSearchServiceForTesting(searchService);

                    SearchPopoverController controller = new SearchPopoverController(
                        dataVisualizer,
                        searchService
                    );
                    TextField searchField = new TextField();
                    VisualElement popover = new VisualElement();
                    controller.Attach(searchField, popover);

                    Toggle fuzzyToggle = popover.Q<Toggle>("search-fuzzy-toggle");
                    Assert.That(fuzzyToggle, Is.Not.Null);

                    fuzzyToggle.value = false;

                    Assert.That(settings.enableFuzzySearch, Is.False);
                    Assert.That(searchService.EnableFuzzyMatching, Is.False);
                    Assert.That(fuzzyToggle.value, Is.False);
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

        [Test]
        public void ThresholdSliderChangeUpdatesSettingsAndService()
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
                    settings.enableFuzzySearch = true;
                    settings.fuzzyMatchThreshold = 0.6f;
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

                    StubAssetService assetService = new StubAssetService();
                    SearchService searchService = new SearchService(assetService);
                    dataVisualizer.OverrideSearchServiceForTesting(searchService);

                    SearchPopoverController controller = new SearchPopoverController(
                        dataVisualizer,
                        searchService
                    );
                    TextField searchField = new TextField();
                    VisualElement popover = new VisualElement();
                    controller.Attach(searchField, popover);

                    Slider thresholdSlider = popover.Q<Slider>("search-fuzzy-threshold-slider");
                    Assert.That(thresholdSlider, Is.Not.Null);

                    thresholdSlider.value = 0.9f;

                    Assert.That(settings.fuzzyMatchThreshold, Is.EqualTo(0.9f).Within(0.0001f));
                    Assert.That(
                        searchService.FuzzyMatchThreshold,
                        Is.EqualTo(0.9f).Within(0.0001f)
                    );

                    Label valueLabel = popover.Q<Label>("search-fuzzy-threshold-label");
                    Assert.That(valueLabel, Is.Not.Null);
                    Assert.That(valueLabel.text, Does.Contain("90"));
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

        [Test]
        public void ShowScoresTogglePersistsSettingAndHidesLegend()
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
                    settings.showSearchScores = true;
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

                    StubAssetService assetService = new StubAssetService();
                    SearchService searchService = new SearchService(assetService);
                    dataVisualizer.OverrideSearchServiceForTesting(searchService);

                    SearchPopoverController controller = new SearchPopoverController(
                        dataVisualizer,
                        searchService
                    );
                    TextField searchField = new TextField();
                    VisualElement popover = new VisualElement();
                    controller.Attach(searchField, popover);

                    Toggle showScoresToggle = popover.Q<Toggle>("search-show-scores-toggle");
                    Assert.That(showScoresToggle, Is.Not.Null);

                    Label legend = popover.Q<Label>("search-confidence-legend");
                    Assert.That(legend, Is.Not.Null);
                    Assert.That(legend.style.display.value, Is.EqualTo(DisplayStyle.Flex));

                    showScoresToggle.value = false;

                    Assert.That(settings.showSearchScores, Is.False);
                    Assert.That(legend.style.display.value, Is.EqualTo(DisplayStyle.None));
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

        private sealed class StubAssetService : IDataAssetService
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
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
