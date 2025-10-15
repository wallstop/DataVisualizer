#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using Services;
    using State;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class ObjectSelectionServiceTests
    {
        private const string TempFolderPath = "Assets/TempObjectSelection";

        private sealed class DummyScriptableObject : ScriptableObject { }

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", "TempObjectSelection");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.DeleteAsset(TempFolderPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void SynchronizeSelectionSetsPrimaryGuid()
        {
            VisualizerSessionState sessionState = new VisualizerSessionState();
            ObjectSelectionService service = new ObjectSelectionService(sessionState);

            DummyScriptableObject asset = CreateAsset("Primary.asset");
            try
            {
                service.SynchronizeSelection(new[] { asset }, asset);

                Assert.AreEqual(
                    service.ResolveGuid(asset),
                    sessionState.Selection.PrimarySelectedObjectGuid
                );
                CollectionAssert.AreEqual(
                    new List<string> { service.ResolveGuid(asset) },
                    sessionState.Selection.SelectedObjectGuids
                );
            }
            finally
            {
                DeleteAsset(asset);
            }
        }

        [Test]
        public void SynchronizeSelectionAddsPrimaryWhenMissingFromList()
        {
            VisualizerSessionState sessionState = new VisualizerSessionState();
            ObjectSelectionService service = new ObjectSelectionService(sessionState);

            DummyScriptableObject assetA = CreateAsset("A.asset");
            DummyScriptableObject assetB = CreateAsset("B.asset");
            try
            {
                service.SynchronizeSelection(new[] { assetA }, assetB);

                Assert.AreEqual(
                    service.ResolveGuid(assetB),
                    sessionState.Selection.PrimarySelectedObjectGuid
                );
                CollectionAssert.AreEqual(
                    new List<string> { service.ResolveGuid(assetB), service.ResolveGuid(assetA) },
                    sessionState.Selection.SelectedObjectGuids
                );
            }
            finally
            {
                DeleteAsset(assetA);
                DeleteAsset(assetB);
            }
        }

        private static DummyScriptableObject CreateAsset(string fileName)
        {
            DummyScriptableObject asset = ScriptableObject.CreateInstance<DummyScriptableObject>();
            string path = $"{TempFolderPath}/{fileName}";
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<DummyScriptableObject>(path);
        }

        private static void DeleteAsset(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrWhiteSpace(path))
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
