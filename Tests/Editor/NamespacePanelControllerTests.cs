#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Controllers;
    using Data;
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
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
