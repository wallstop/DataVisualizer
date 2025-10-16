#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
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

                    dataVisualizer._userStateRepository = repository;

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
    }
}
