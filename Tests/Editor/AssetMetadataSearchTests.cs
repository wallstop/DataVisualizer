namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Search;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    public sealed class AssetMetadataSearchTests
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CaptureDestroyedSource(AssetObjectCache cache)
        {
            PlayModeDataObject asset = ScriptableObject.CreateInstance<PlayModeDataObject>();
            asset.name = "Captured";
            asset.Description = "field text";
            cache.Upsert("guid", asset, false);
            WeakReference reference = new(asset);
            UnityEngine.Object.DestroyImmediate(asset);
            return reference;
        }

        [Test]
        public void ShouldReleaseSourceWrappersWhileDocumentsRemainSearchable()
        {
            AssetMetadataStore metadata = new();
            AssetObjectCache cache = new(metadata);
            WeakReference source = CaptureDestroyedSource(cache);
            AssetObjectCache legacy = new(new List<ScriptableObject>());
            WeakReference control = CaptureDestroyedSource(legacy);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(control.IsAlive, "The explicitly owned compatibility list is a strong-reference control");
            Assert.That(!source.IsAlive, "Search metadata must not keep even a destroyed managed wrapper alive");
            Assert.That(cache.RetainedObjectCount, Is.Zero);
            Assert.That(cache.Contains("guid"));
            Assert.That(cache.Documents[0].Match(new[] { "field text" }).isMatch);
            Assert.That(metadata.Count, Is.EqualTo(1));
            GC.KeepAlive(legacy);
            GC.KeepAlive(cache);
        }

        [Test]
        public void ShouldReuseSharedTextAndLabelsAcrossIndependentSearchCaches()
        {
            AssetMetadataStore metadata = new();
            AssetObjectCache first = new(metadata);
            CaptureDestroyedSource(first);
            first.Clear();
            AssetObjectCache second = new(metadata);
            Assert.That(second.UseMetadata("guid", new HashSet<Type> { typeof(PlayModeDataObject) }));
            Assert.That(second.ObjectLoadCount, Is.Zero);
            AssetSearchQuery query = new(second.Documents, new[] { "field text" }, 25);
            while (!query.IsComplete)
            {
                query.Advance(4);
            }
            Assert.That(query.Results.Count, Is.EqualTo(1));
            Assert.That(second.RetainedObjectCount, Is.Zero);
            Assert.That(!second.UseMetadata("guid", new HashSet<Type> { typeof(EditorOnlyCreationData) }));
            Assert.That(!second.UseMetadata("absent", new HashSet<Type> { typeof(PlayModeDataObject) }));
            second.Clear();
            Assert.That(metadata.Count, Is.EqualTo(1), "Clearing one window must not erase shared snapshots");
            metadata.Remove("guid");
            Assert.That(!second.UseMetadata("guid", new HashSet<Type> { typeof(PlayModeDataObject) }));
        }

        [Test]
        public void ShouldResolveSharedMatchesOnlyForExplicitObjectAccess()
        {
            string root = "Assets/MetadataSearchTests-" + Guid.NewGuid().ToString("N");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(root));
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(root));
            PlayModeDataObject asset = ScriptableObject.CreateInstance<PlayModeDataObject>();
            asset.Description = "before";
            string path = root + "/Record.asset";
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SetLabels(asset, new[] { "Label" });
            string guid = AssetDatabase.AssetPathToGUID(path);
            AssetMetadataStore metadata = new();
            AssetObjectCache first = new(metadata);
            first.Upsert(guid, asset, false);
            AssetObjectCache second = new(metadata);
            Assert.That(second.UseMetadata(guid, new HashSet<Type> { typeof(PlayModeDataObject) }));
            Assert.That(!second.TryGetLoaded(guid, out _));
            Assert.That(second.ObjectLoadCount, Is.Zero);
            Assert.That(second.TryGet(guid, out ScriptableObject resolved));
            Assert.That(ReferenceEquals(asset, resolved));
            Assert.That(second.ObjectLoadCount, Is.EqualTo(1));
            Assert.That(second.TryGet(guid, out _));
            Assert.That(second.ObjectLoadCount, Is.EqualTo(1), "A visible result should reuse its live wrapper");
            Assert.That(second.GetGuidOrNull(asset), Is.EqualTo(guid));
            asset.Description = "after";
            first.Upsert(guid, asset, true);
            second.UseMetadata(guid, new HashSet<Type> { typeof(PlayModeDataObject) });
            Assert.That(second.Documents[0].Match(new[] { "after" }).isMatch);
            Assert.That(second.Match(asset, new[] { "after" }).isMatch, "Loaded hints must read the latest shared document");
            Assert.That(metadata.TryGet(guid, out AssetMetadataRecord record));
            CollectionAssert.AreEqual(new[] { "Label" }, record.Labels);
            Assert.That(second.RememberLoaded(guid, asset));
            Assert.That(!second.RememberLoaded("absent", asset));
            Assert.That(second.ResetRetainingRows(new[] { asset, asset }, type => type == typeof(PlayModeDataObject)), Is.EqualTo(1));
            Assert.That(second.RetainedObjectCount, Is.Zero);
            Assert.That(second.TryGetLoaded(guid, out ScriptableObject selected));
            Assert.That(ReferenceEquals(selected, asset));
            Assert.That(second.Upsert(guid, null, true));
            Assert.That(!metadata.TryGet(guid, out _));
            Assert.That(second.Count, Is.Zero);
        }
    }
}
