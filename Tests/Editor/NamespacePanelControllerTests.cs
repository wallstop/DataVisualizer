#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Controllers;
    using Data;
    using Events;
    using NUnit.Framework;
    using Services;
    using State;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class NamespacePanelControllerTests
    {
        private sealed class DummyScriptableObject : ScriptableObject { }

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

        [Test]
        public void BuildNamespaceViewCreatesNamespaceContainer()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;

                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                settings.persistStateInSettingsAsset = true;
                settings.namespaceCollapseStates = new List<NamespaceCollapseState>
                {
                    new NamespaceCollapseState
                    {
                        namespaceKey = "TestNamespace",
                        isCollapsed = true,
                    },
                };

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

                dataVisualizer._scriptableObjectTypes.Clear();
                dataVisualizer._namespaceOrder.Clear();
                dataVisualizer._scriptableObjectTypes["TestNamespace"] = new List<Type>
                {
                    typeof(DummyScriptableObject),
                };
                dataVisualizer._namespaceOrder["TestNamespace"] = 0;

                dataVisualizer._namespaceController.Clear();

                dataVisualizer.BuildNamespaceView();

                VisualElement container = dataVisualizer._namespaceListContainer;
                Assert.IsNotNull(container);
                Assert.Greater(container.childCount, 0);

                VisualElement namespaceGroup = container.Children().First();
                Assert.AreEqual("TestNamespace", namespaceGroup.userData as string);

                VisualElement typesContainer = namespaceGroup.Q<VisualElement>(
                    "types-container-TestNamespace"
                );
                Assert.IsNotNull(typesContainer);
                Assert.AreEqual(DisplayStyle.None, typesContainer.style.display.value);
                Assert.Contains(
                    "TestNamespace",
                    new List<string>(dataVisualizer.SessionState.Selection.CollapsedNamespaces)
                );
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void RenderActiveFiltersWithNamespaceSelectionShowsNamespaceChip()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;

                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                settings.persistStateInSettingsAsset = true;

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

                dataVisualizer._namespaceFilterContainer = new VisualElement();
                dataVisualizer.SessionState.Selection.SetSelectedNamespace("TestNamespace");

                DataVisualizerEventHub eventHub = new DataVisualizerEventHub();
                NamespacePanelController controller = new NamespacePanelController(
                    dataVisualizer,
                    dataVisualizer._namespaceController,
                    dataVisualizer.SessionState,
                    eventHub
                );

                try
                {
                    controller.RenderActiveFilters();

                    VisualElement container = dataVisualizer._namespaceFilterContainer;
                    Assert.That(container, Is.Not.Null);
                    Assert.That(container.style.display.value, Is.EqualTo(DisplayStyle.Flex));

                    Button namespaceChip = container.Q<Button>(
                        className: "namespace-filter-chip-namespace"
                    );
                    Assert.That(namespaceChip, Is.Not.Null);
                    Assert.That(namespaceChip.text, Does.Contain("TestNamespace"));
                }
                finally
                {
                    controller.Dispose();
                }

                Object.DestroyImmediate(settings);
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void RenderActiveFiltersWithLabelFiltersRendersChipsAndLogicToggle()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;

                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                settings.persistStateInSettingsAsset = true;

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

                dataVisualizer._namespaceFilterContainer = new VisualElement();

                VisualizerSessionState.LabelFilterState labels = dataVisualizer.SessionState.Labels;
                labels.SetAndLabels(new[] { "Gameplay" });
                labels.SetCombinationType(LabelCombinationType.Or);

                DataVisualizerEventHub eventHub = new DataVisualizerEventHub();
                NamespacePanelController controller = new NamespacePanelController(
                    dataVisualizer,
                    dataVisualizer._namespaceController,
                    dataVisualizer.SessionState,
                    eventHub
                );

                try
                {
                    controller.RenderActiveFilters();

                    VisualElement container = dataVisualizer._namespaceFilterContainer;
                    Assert.That(container, Is.Not.Null);
                    Assert.That(container.style.display.value, Is.EqualTo(DisplayStyle.Flex));

                    Button andChip = container.Q<Button>(className: "namespace-filter-chip-and");
                    Assert.That(andChip, Is.Not.Null);
                    Assert.That(andChip.text, Does.Contain("AND"));
                    Assert.That(andChip.text, Does.Contain("Gameplay"));

                    Button logicButton = container.Q<Button>(
                        className: "namespace-filter-chip-logic"
                    );
                    Assert.That(logicButton, Is.Not.Null);
                    Assert.That(logicButton.text, Is.EqualTo("Logic: OR"));

                    Button clearButton = container.Q<Button>(
                        className: "namespace-filter-clear-button"
                    );
                    Assert.That(clearButton, Is.Not.Null);
                }
                finally
                {
                    controller.Dispose();
                }

                Object.DestroyImmediate(settings);
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
