#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using Data;
    using NUnit.Framework;
    using Scaffolding;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class UndoIntegrationTests
    {
        private sealed class DummyScriptableObject : ScriptableObject { }

        [Test]
        public void AddLabelCanBeUndone()
        {
            string assetPath = "Assets/UndoIntegrationTests_AddLabel.asset";
            try
            {
                DummyScriptableObject asset =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                using DataVisualizerTestHarness harness = new DataVisualizerTestHarness();
                harness.InitializeWindow();
                DataVisualizer window = harness.Window;

                window._selectedObject = asset;
                window._inspectorNewLabelInput = new TextField();
                window._inspectorNewLabelInput.value = "Gameplay";

                Undo.IncrementCurrentGroup();
                window.AddLabelToSelectedAsset();
                Assert.That(AssetDatabase.GetLabels(asset), Does.Contain("Gameplay"));

                Undo.PerformUndo();
                Assert.That(AssetDatabase.GetLabels(asset), Does.Not.Contain("Gameplay"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.SaveAssets();
            }
        }

        [Test]
        public void RenameAssetCanBeUndone()
        {
            string assetPath = "Assets/UndoIntegrationTests_Rename.asset";
            try
            {
                DummyScriptableObject asset =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                using DataVisualizerTestHarness harness = new DataVisualizerTestHarness();
                harness.InitializeWindow();
                DataVisualizer window = harness.Window;

                string originalPath = AssetDatabase.GetAssetPath(asset);
                window._popoverContext = originalPath;
                Label titleLabel = new Label();
                TextField nameField = new TextField { value = "RenamedAsset" };
                Label errorLabel = new Label();

                Undo.IncrementCurrentGroup();
                window.HandleRenameConfirmed(titleLabel, nameField, errorLabel);
                string renamedPath = AssetDatabase.GetAssetPath(asset);
                Assert.That(renamedPath, Does.Contain("RenamedAsset"));

                Undo.PerformUndo();
                string revertedPath = AssetDatabase.GetAssetPath(asset);
                Assert.That(revertedPath, Is.EqualTo(originalPath));
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
