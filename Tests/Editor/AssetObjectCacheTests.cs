namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using Unity.Profiling;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    internal sealed class AssetObjectCacheTests
    {
        [Test]
        public void ShouldPreserveSortWhenSeveralUnityObjectsRenameBeforeReconciliation()
        {
            using TestCleanupScope cleanup = new();
            PlayModeDataObject first = ScriptableObject.CreateInstance<PlayModeDataObject>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(first));
            PlayModeDataObject middle = ScriptableObject.CreateInstance<PlayModeDataObject>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(middle));
            PlayModeDataObject last = ScriptableObject.CreateInstance<PlayModeDataObject>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(last));
            first.name = "A";
            middle.name = "M";
            last.name = "Z";
            List<ScriptableObject> ordered = new();
            AssetObjectCache cache = new(ordered);
            cache.Upsert("last", last, false);
            cache.Upsert("first", first, false);
            cache.Upsert("middle", middle, false);
            cache.Sort();
            first.name = "Y";
            last.name = "B";
            cache.Upsert("first", first, true);
            cache.Upsert("last", last, true);
            CollectionAssert.AreEqual(new ScriptableObject[] { last, middle, first }, ordered);
            Assert.That(cache.TryGet("middle", out ScriptableObject unchanged));
            Assert.That(ReferenceEquals(middle, unchanged));
        }

        [Test]
        public void ShouldReplaceGuidAndRemoveDestroyedWrapperWithoutLeavingRows()
        {
            using TestCleanupScope cleanup = new();
            PlayModeDataObject original = ScriptableObject.CreateInstance<PlayModeDataObject>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(original));
            PlayModeDataObject replacement = ScriptableObject.CreateInstance<PlayModeDataObject>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(replacement));
            List<ScriptableObject> ordered = new();
            AssetObjectCache cache = new(ordered);
            cache.Upsert("guid", original, true);
            cache.Upsert("guid", replacement, true);
            CollectionAssert.AreEqual(new[] { replacement }, ordered);
            cache.Upsert("other", original, true);
            UnityEngine.Object.DestroyImmediate(replacement);
            Assert.AreEqual(1, cache.ResetRetainingRows(new ScriptableObject[] { replacement }, type => type == typeof(PlayModeDataObject)));
            Assert.That(cache.TryGet("guid", out ScriptableObject destroyed));
            Assert.That(ReferenceEquals(replacement, destroyed));
            Assert.That(!cache.Contains("other"));
            Assert.That(!cache.Contains("guid"));
            Assert.That(cache.Remove("guid"));
            Assert.AreEqual(0, cache.Count);
            Assert.AreEqual(0, ordered.Count);
            Assert.That(!cache.TryGet(null, out ScriptableObject missing));
            Assert.That(missing == null);
            Assert.That(!cache.Upsert(null, original, true));
        }

        [Test]
        public void ShouldRetainSharedLabelsUntilTheirLastAssetIsRemoved()
        {
            string root = "Assets/AssetLabelUsageTests-" + Guid.NewGuid().ToString("N");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(root));
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(root));
            PlayModeDataObject first = ScriptableObject.CreateInstance<PlayModeDataObject>();
            AssetDatabase.CreateAsset(first, root + "/First.asset");
            PlayModeDataObject second = ScriptableObject.CreateInstance<PlayModeDataObject>();
            AssetDatabase.CreateAsset(second, root + "/Second.asset");
            AssetDatabase.SetLabels(first, new[] { "Shared", "First" });
            AssetDatabase.SetLabels(second, new[] { "Shared", "Second" });
            AssetLabelUsage usage = new();
            usage.GetOrRead(first);
            usage.GetOrRead(second);
            List<string> names = new();
            usage.CopyNamesTo(names);
            CollectionAssert.AreEqual(new[] { "First", "Second", "Shared" }, names);
            AssetDatabase.SetLabels(first, new[] { "Changed" });
            Assert.That(usage.Update(first));
            Assert.That(!usage.Update(first));
            usage.CopyNamesTo(names);
            CollectionAssert.AreEqual(new[] { "Changed", "Second", "Shared" }, names);
            Assert.That(AssetDatabase.DeleteAsset(root + "/Second.asset"));
            List<ScriptableObject> rows = new() { first, second, null };
            Assert.AreEqual(2, usage.RemoveDestroyedRows(rows));
            CollectionAssert.AreEqual(new[] { first }, rows);
            Assert.That(!usage.Remove(second));
            Assert.AreEqual(0, usage.RemoveDestroyedRows(rows));
            usage.CopyNamesTo(names);
            CollectionAssert.AreEqual(new[] { "Changed" }, names);
            usage.Clear();
            usage.CopyNamesTo(names);
            Assert.AreEqual(0, names.Count);
        }

        [Test]
        public void ShouldRemoveDestroyedRowsInOrderWithoutAllocating()
        {
            using TestCleanupScope cleanup = new();
            PlayModeDataObject first = ScriptableObject.CreateInstance<PlayModeDataObject>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(first));
            PlayModeDataObject last = ScriptableObject.CreateInstance<PlayModeDataObject>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(last));
            PlayModeDataObject destroyed = ScriptableObject.CreateInstance<PlayModeDataObject>();
            UnityEngine.Object.DestroyImmediate(destroyed);
            List<ScriptableObject> rows = new(8);
            AssetLabelUsage usage = new();
            rows.Add(destroyed);
            usage.RemoveDestroyedRows(rows);
            using (ProfilerRecorder calibration = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 8, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                byte[] control = new byte[1024];
                calibration.Stop();
                Assert.That(calibration.Count, Is.Positive, "The allocation recorder must detect a positive control.");
                GC.KeepAlive(control);
            }
            int removed = 0;
            using ProfilerRecorder allocations = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1024, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (int iteration = 0; iteration < 100; iteration++)
            {
                rows.Add(null);
                rows.Add(first);
                rows.Add(destroyed);
                rows.Add(last);
                rows.Add(null);
                removed += usage.RemoveDestroyedRows(rows);
                rows.Clear();
            }
            allocations.Stop();
            Assert.That(allocations.Valid);
            Assert.AreEqual(300, removed);
            Assert.AreEqual(0, allocations.Count);
            rows.Add(null);
            rows.Add(first);
            rows.Add(destroyed);
            rows.Add(last);
            Assert.AreEqual(2, usage.RemoveDestroyedRows(rows));
            CollectionAssert.AreEqual(new ScriptableObject[] { first, last }, rows);
            Assert.AreEqual(0, usage.RemoveDestroyedRows(rows));
        }

        [Test]
        public void ShouldReportRemovalAndKeepSharedLabelUntilLastOwnerLeaves()
        {
            string root = "Assets/AssetLabelRemovalTests-" + Guid.NewGuid().ToString("N");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(root));
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(root));
            PlayModeDataObject first = ScriptableObject.CreateInstance<PlayModeDataObject>();
            PlayModeDataObject second = ScriptableObject.CreateInstance<PlayModeDataObject>();
            AssetDatabase.CreateAsset(first, root + "/First.asset");
            AssetDatabase.CreateAsset(second, root + "/Second.asset");
            AssetDatabase.SetLabels(first, new[] { "shared", "Shared" });
            AssetDatabase.SetLabels(second, new[] { "shared" });
            AssetLabelUsage usage = new();
            Assert.That(usage.Update(first));
            Assert.That(!usage.Update(first));
            Assert.That(usage.Update(second));
            List<string> names = new() { "stale" };
            usage.CopyNamesTo(names);
            CollectionAssert.AreEqual(new[] { "Shared", "shared" }, names);
            Assert.That(!usage.Remove(null));
            Assert.That(usage.Remove(first));
            Assert.That(!usage.Remove(first));
            usage.CopyNamesTo(names);
            CollectionAssert.AreEqual(new[] { "shared" }, names);
            Assert.That(AssetDatabase.DeleteAsset(root + "/Second.asset"));
            Assert.That(usage.Remove(second));
            Assert.That(!usage.Remove(second));
            usage.CopyNamesTo(names);
            Assert.That(names, Is.Empty);
        }
    }
}
