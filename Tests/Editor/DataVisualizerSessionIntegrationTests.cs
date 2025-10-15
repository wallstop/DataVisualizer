#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using Data;
    using NUnit.Framework;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class DataVisualizerSessionIntegrationTests
    {
        private sealed class DummyScriptableObject : ScriptableObject { }

        [Test]
        public void SaveNamespaceAndTypeSelectionStatePersistsSessionState()
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
                    dataVisualizer._settings = settings;
                    dataVisualizer._userState = new DataVisualizerUserState();

                    NamespaceController.SaveNamespaceAndTypeSelectionState(
                        dataVisualizer,
                        "TestNamespace",
                        typeof(DummyScriptableObject)
                    );

                    Assert.AreEqual(
                        "TestNamespace",
                        dataVisualizer.SessionState.Selection.SelectedNamespaceKey
                    );
                    Assert.AreEqual(
                        typeof(DummyScriptableObject).FullName,
                        dataVisualizer.SessionState.Selection.SelectedTypeFullName
                    );
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
    }
}
