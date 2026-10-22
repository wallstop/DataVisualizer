namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Search;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    public sealed class AssetMetadataStoreTests
    {
        private static AssetMetadataRecord Make(string guid, string path)
        {
            return new AssetMetadataRecord(new AssetSearchDocument(guid, "Name", typeof(PlayModeDataObject), null), typeof(PlayModeDataObject), path, null, null);
        }

        [Test]
        public void ShouldCopyLabelsAndTextWithoutRetainingTheSource()
        {
            using TestCleanupScope cleanup = new();
            PlayModeDataObject asset = ScriptableObject.CreateInstance<PlayModeDataObject>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(asset));
            asset.name = "Before";
            asset.Description = "nested text";
            string[] labels = { "Shared", "Only" };
            AssetMetadataRecord record = new(new AssetSearchDocument("guid", asset.name, asset.GetType(), asset), asset.GetType(), "Assets/Before.asset", "Display", labels);
            AssetMetadataStore store = new();
            Assert.That(store.Upsert(record));
            Assert.That(!store.Upsert(record));
            labels[0] = "Changed";
            asset.name = "After";
            asset.Description = "after";
            UnityEngine.Object.DestroyImmediate(asset);
            Assert.That(store.TryGet("guid", out AssetMetadataRecord retained));
            Assert.That(retained.ExactType, Is.EqualTo(typeof(PlayModeDataObject)));
            Assert.That(retained.DisplayTitle, Is.EqualTo("Display"));
            CollectionAssert.AreEqual(new[] { "Shared", "Only" }, retained.Labels);
            Assert.That(retained.Document.Name, Is.EqualTo("Before"));
            Assert.That(retained.Document.Match(new[] { "nested text" }).isMatch);
            Assert.That(!retained.Document.Match(new[] { "after" }).isMatch);
        }

        [Test]
        public void ShouldReplaceMovedAndReusedPathsWithoutRemovingNewIdentities()
        {
            AssetMetadataStore store = new();
            AssetMetadataRecord first = Make("a", "Assets/First.asset");
            Assert.That(store.Upsert(first));
            Assert.That(store.Upsert(Make("a", "Assets/Moved.asset")));
            Assert.That(!store.RemovePath("Assets/First.asset"));
            Assert.That(store.Upsert(Make("b", "Assets/Moved.asset")));
            Assert.That(!store.TryGet("a", out _));
            Assert.That(store.Count, Is.EqualTo(1));
            int version = store.Version;
            Assert.That(store.RemovePath("assets/moved.asset"));
            Assert.That(store.Version, Is.EqualTo(version + 1));
            Assert.That(!store.Remove("b"));
            Assert.That(!store.Remove(null));
            Assert.That(!store.TryGet(null, out _));
            Assert.That(!store.Clear());
            store.Upsert(first);
            Assert.That(store.Clear());
            Assert.That(store.Count, Is.Zero);
        }

        [Test]
        public void ShouldInvalidateCapturedRecordsBeforeClassificationAndOnReset()
        {
            string root = "Assets/MetadataStoreTests-" + Guid.NewGuid().ToString("N");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(root));
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(root));
            PlayModeDataObject asset = ScriptableObject.CreateInstance<PlayModeDataObject>();
            string path = root + "/Record.asset";
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SetLabels(asset, new[] { "Authored" });
            string guid = AssetDatabase.AssetPathToGUID(path);
            AssetGuidTypeIndex index = new(false);
            cleanup.Defer(index.Cancel);
            index.AssetDatabaseBusyOverride = () => false;
            index.EnsureStarted();
            AssetMetadataRecord captured = new(guid, asset);
            Assert.That(index.Metadata.Upsert(captured));
            Assert.That(captured.Path, Is.EqualTo(path));
            CollectionAssert.AreEqual(new[] { "Authored" }, captured.Labels);
            index.ApplyAssetChanges(new[] { path }, null, null, null);
            Assert.That(!index.Metadata.TryGet(guid, out _), "Reimports must invalidate even before the initial path snapshot");
            index.Metadata.Upsert(captured);
            index.ApplyAssetChanges(null, new[] { path }, null, null);
            Assert.That(index.Metadata.Count, Is.Zero);
            index.Metadata.Upsert(captured);
            index.Cancel();
            Assert.That(index.Metadata.Count, Is.Zero);
        }

        [Test]
        public void ShouldRejectMismatchedExactTypeOrMissingIdentity()
        {
            Assert.Throws<ArgumentException>(() => new AssetMetadataRecord(new AssetSearchDocument("guid", "Name", typeof(string), null), typeof(int), null, null, null));
            Assert.Throws<ArgumentException>(() => Make(string.Empty, "Assets/Missing.asset"));
            Assert.Throws<ArgumentNullException>(() => new AssetMetadataStore().Upsert(null));
        }
    }
}
