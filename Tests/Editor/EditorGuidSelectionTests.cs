namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorGuidSelectionTests
    {
        private const string FirstGuid = "abcdef0123456789abcdef0123456789";
        private const string SecondGuid = "fedcba9876543210fedcba9876543210";

        [Test]
        public void ShouldImportOrderedIndependentIdentitiesWithoutResolvingMissingAssets()
        {
            EditorAssetSelection selection = new();
            Assert.That(selection.HasSelectionIntent, Is.False);
            string[] input = { FirstGuid.ToUpperInvariant(), SecondGuid, FirstGuid };
            Assert.That(selection.ReplaceGuids(typeof(TestDataObject), input), Is.True);
            Assert.That(selection.CopyGuids(), Is.EqualTo(new[] { FirstGuid, SecondGuid }));
            Assert.That(selection.TypeIdentity, Is.EqualTo(typeof(TestDataObject).AssemblyQualifiedName));
            Assert.That(selection.HasSelectionIntent, Is.True);
            Assert.That(selection.Count, Is.EqualTo(2));
            Assert.That(selection.ReplaceGuids(typeof(TestDataObject), input), Is.False);
            input[0] = SecondGuid;
            string[] exported = selection.CopyGuids();
            exported[0] = SecondGuid;
            Assert.That(selection.CopyGuids(), Is.EqualTo(new[] { FirstGuid, SecondGuid }));
            Assert.That(selection.ReplaceGuids(typeof(TestDataObject), new[] { SecondGuid, FirstGuid }), Is.True);
            Assert.That(selection.CopyGuids(), Is.EqualTo(new[] { SecondGuid, FirstGuid }));
            Assert.Throws<ArgumentException>(() => new EditorAssetOperation(EditorAssetOperationKind.Delete, typeof(TestDataObject), selection.CopyGuids()));
            Assert.That(selection.Count, Is.EqualTo(2), "Operation rejection must not erase explicit selection intent.");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not-a-guid")]
        [TestCase(" abcdef0123456789abcdef0123456789")]
        [TestCase("{abcdef0123456789abcdef0123456789}")]
        public void ShouldRejectMalformedInputWithoutChangingTypeOrSelection(string invalid)
        {
            EditorAssetSelection selection = new();
            selection.ReplaceGuids(typeof(TestDataObject), new[] { FirstGuid });
            Assert.Throws<ArgumentException>(() => selection.ReplaceGuids(typeof(OperationLifecycleData), new[] { SecondGuid, invalid }));
            Assert.That(selection.TypeIdentity, Is.EqualTo(typeof(TestDataObject).AssemblyQualifiedName));
            Assert.That(selection.CopyGuids(), Is.EqualTo(new[] { FirstGuid }));
            Assert.That(selection.HasSelectionIntent, Is.True);
        }

        [TestCase(null)]
        [TestCase(typeof(string))]
        [TestCase(typeof(BaseDataObject))]
        [TestCase(typeof(EditorTypeCatalogTests.GenericData<>))]
        public void ShouldRejectUnsupportedTypeWithoutErasingExistingIntent(Type type)
        {
            EditorAssetSelection selection = new();
            selection.ReplaceGuids(typeof(TestDataObject), new[] { FirstGuid });
            Assert.Throws<ArgumentException>(() => selection.ReplaceGuids(type, new[] { SecondGuid }));
            Assert.That(selection.TypeIdentity, Is.EqualTo(typeof(TestDataObject).AssemblyQualifiedName));
            Assert.That(selection.CopyGuids(), Is.EqualTo(new[] { FirstGuid }));
            Assert.Throws<ArgumentNullException>(() => selection.ReplaceGuids(typeof(TestDataObject), null));
            Assert.That(selection.Count, Is.EqualTo(1));
        }

        [Test]
        public void ShouldPreserveExplicitEmptyIntentAcrossSerializationAndTypeChanges()
        {
            EditorAssetSelection selection = new();
            Assert.That(selection.ReplaceGuids(typeof(TestDataObject), Array.Empty<string>()), Is.True);
            Assert.That(selection.ReplaceGuids(typeof(TestDataObject), Array.Empty<string>()), Is.False);
            EditorAssetSelection restored = JsonUtility.FromJson<EditorAssetSelection>(JsonUtility.ToJson(selection));
            Assert.That(restored.TypeIdentity, Is.EqualTo(selection.TypeIdentity));
            Assert.That(restored.HasSelectionIntent, Is.True);
            Assert.That(restored.CopyGuids(), Is.Empty);
            Assert.That(restored.ReplaceGuids(typeof(TestDataObject), Array.Empty<string>()), Is.False);
            Assert.Throws<ArgumentException>(() => new EditorAssetOperation(EditorAssetOperationKind.Delete, typeof(TestDataObject), restored.CopyGuids()));
            Assert.That(restored.ReplaceGuids(typeof(OperationLifecycleData), Array.Empty<string>()), Is.True);
            Assert.That(restored.HasSelectionIntent, Is.True);
            restored.SetType(typeof(TestDataObject));
            Assert.That(restored.HasSelectionIntent, Is.False, "Browsing another type still resets explicit intent.");
        }

        [Test]
        public void ShouldShareImportedSelectionWithBrowserEditsAndOperationPreview()
        {
            using TestCleanupScope cleanup = new();
            string folder = "Assets/GuidSelection-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            ScriptableObject[] assets = new ScriptableObject[2];
            string[] paths = new string[2];
            string[] guids = new string[2];
            byte[][] bytes = new byte[2][];
            for (int i = 0; i < assets.Length; i++)
            {
                ScriptableObject asset = ScriptableObject.CreateInstance<TestDataObject>();
                cleanup.Defer(() =>
                {
                    if (asset != null && !AssetDatabase.Contains(asset))
                    {
                        UnityEngine.Object.DestroyImmediate(asset);
                    }
                });
                paths[i] = folder + "/" + i + ".asset";
                AssetDatabase.CreateAsset(asset, paths[i]);
                AssetDatabase.SaveAssetIfDirty(asset);
                assets[i] = asset;
                guids[i] = AssetDatabase.AssetPathToGUID(paths[i]);
                bytes[i] = File.ReadAllBytes(paths[i]);
            }
            int loaded = Resources.FindObjectsOfTypeAll<TestDataObject>().Length;
            EditorAssetSelection selection = new();
            selection.ReplaceGuids(typeof(TestDataObject), new[] { guids[1], guids[0] });
            EditorAssetSelection restored = JsonUtility.FromJson<EditorAssetSelection>(JsonUtility.ToJson(selection));
            Assert.That(restored.GetVisibleIndices(assets), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(restored.CopyGuids(assets), Is.EqualTo(guids), "The browser still exports its canonical row order.");
            using (EditorAssetOperation preview = new(EditorAssetOperationKind.Delete, typeof(TestDataObject), restored.CopyGuids()))
            {
                Assert.That(preview.Targets[0].Guid, Is.EqualTo(guids[1]));
                Assert.That(preview.Targets[1].Guid, Is.EqualTo(guids[0]));
                Assert.That(preview.Status, Is.EqualTo("preview"));
            }
            for (int i = 0; i < assets.Length; i++)
            {
                Assert.That(File.ReadAllBytes(paths[i]), Is.EqualTo(bytes[i]));
                Assert.That(EditorUtility.IsDirty(assets[i]), Is.False);
            }
            Assert.That(Resources.FindObjectsOfTypeAll<TestDataObject>().Length, Is.EqualTo(loaded));
            restored.ReplaceVisible(new[] { assets[0] }, Array.Empty<object>());
            Assert.That(restored.CopyGuids(), Is.EqualTo(new[] { guids[1] }), "Deselecting visible rows preserves the hidden imported target.");
            Assert.That(restored.GetVisibleIndices(assets), Is.EqualTo(new[] { 1 }));
            Assert.That(AssetDatabase.DeleteAsset(paths[1]), Is.True);
            Assert.Throws<ArgumentException>(() => new EditorAssetOperation(EditorAssetOperationKind.Delete, typeof(TestDataObject), restored.CopyGuids()));
            Assert.That(restored.CopyGuids(), Is.EqualTo(new[] { guids[1] }), "Deleted intent must not silently broaden the operation scope.");
        }
    }
}
