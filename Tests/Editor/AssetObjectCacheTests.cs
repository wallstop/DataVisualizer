namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
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
            UnityEngine.Object.DestroyImmediate(replacement);
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
    }
}
