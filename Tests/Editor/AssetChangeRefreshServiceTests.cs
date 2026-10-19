namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    internal sealed class AssetChangeRefreshServiceTests
    {
        [TestCase(1000)]
        [TestCase(10000)]
        [TestCase(100000)]
        public void ShouldResolveOnlyChangedPathsRegardlessOfTrackedCount(int count)
        {
            int resolved = 0;
            int published = 0;
            using AssetChangeRefreshService service = new(
                type => type == typeof(PlayModeDataObject),
                (guid, path, type) => published++,
                (path, known) =>
                {
                    resolved++;
                    return (known, path, typeof(PlayModeDataObject));
                }
            );
            for (int index = 0; index < count; index++)
            {
                service.Track($"Assets/{index}.asset", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            service.Enqueue(new[] { "Assets/1.asset", "Assets/1.asset" });
            service.ProcessSlice();
            Assert.AreEqual(1, resolved);
            Assert.AreEqual(1, published);
            Assert.AreEqual(0, service.PendingCount);
        }

        [Test]
        public void ShouldIgnoreUnknownDeletionAndUntrackedImportWithoutPublishing()
        {
            int published = 0;
            using AssetChangeRefreshService service = new(
                type => type == typeof(PlayModeDataObject),
                (guid, path, type) => published++,
                (path, known) => path == "Assets/Other.asset" ? ("other", path, typeof(TextAsset)) : (null, null, null)
            );
            Assert.AreEqual(2, service.Enqueue(new[] { "Assets/Deleted.asset", "Assets/Other.asset", "Assets/Texture.png" }));
            service.ProcessSlice(double.MaxValue);
            Assert.AreEqual(0, published);
        }

        [Test]
        public void ShouldRetainDeletedIdentityAndCoalesceBothSidesOfAMove()
        {
            List<(string Guid, string Path, Type Type)> changes = new();
            using AssetChangeRefreshService service = new(
                type => type == typeof(PlayModeDataObject),
                (guid, path, type) => changes.Add((guid, path, type)),
                (path, known) =>
                    path == "Assets/Deleted.asset" ? (null, null, null)
                    : path == "Assets/Old.asset" && known == null ? (null, null, null)
                    : ("moving", "Assets/New.asset", typeof(PlayModeDataObject))
            );
            service.Track("Assets/Deleted.asset", "deleted");
            service.Track("Assets/Old.asset", "moving");
            service.Enqueue(new[] { "Assets/Deleted.asset", "Assets/Old.asset", "Assets/New.asset" });
            service.ProcessSlice(double.MaxValue);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(("deleted", (string)null, (Type)null), changes[0]);
            Assert.AreEqual(("moving", "Assets/New.asset", typeof(PlayModeDataObject)), changes[1]);
        }

        [Test]
        public void ShouldPublishBothAReplacementAndTheMovedOriginal()
        {
            HashSet<string> published = new(StringComparer.Ordinal);
            using AssetChangeRefreshService service = new(
                type => type == typeof(PlayModeDataObject),
                (guid, path, type) => published.Add(guid),
                (path, known) =>
                    known == "original" ? ("original", "Assets/Moved.asset", typeof(PlayModeDataObject)) : ("replacement", "Assets/Original.asset", typeof(PlayModeDataObject))
            );
            service.Track("Assets/Original.asset", "original");
            service.Enqueue(new[] { "Assets/Original.asset" });
            service.ProcessSlice(double.MaxValue);
            Assert.That(published.SetEquals(new[] { "original", "replacement" }));
        }

        [Test]
        public void ShouldReconcileRealMovesReplacementsAndDeletesByRetainedGuid()
        {
            string root = "Assets/AssetChangeRefreshTests-" + Guid.NewGuid().ToString("N");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(root));
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(root));
            string oldPath = root + "/Original.asset";
            string newPath = root + "/Moved.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<PlayModeDataObject>(), oldPath);
            string originalGuid = AssetDatabase.AssetPathToGUID(oldPath);
            List<(string Guid, string Path, Type Type)> changes = new();
            using AssetChangeRefreshService service = new(type => type == typeof(PlayModeDataObject), (guid, path, type) => changes.Add((guid, path, type)));
            service.Track(oldPath, originalGuid);
            Assert.AreEqual(string.Empty, AssetDatabase.MoveAsset(oldPath, newPath));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<PlayModeDataObject>(), oldPath);
            string replacementGuid = AssetDatabase.AssetPathToGUID(oldPath);
            service.Enqueue(new[] { oldPath, newPath });
            service.ProcessSlice(double.MaxValue);
            Assert.That(changes, Does.Contain((originalGuid, newPath, typeof(PlayModeDataObject))));
            Assert.That(changes, Does.Contain((replacementGuid, oldPath, typeof(PlayModeDataObject))));
            Assert.AreEqual(2, changes.Count);
            changes.Clear();
            Assert.That(AssetDatabase.DeleteAsset(newPath));
            service.Enqueue(new[] { newPath });
            service.ProcessSlice();
            Assert.AreEqual((originalGuid, (string)null, (Type)null), changes[0]);
            Assert.AreEqual(1, changes.Count);
        }

        [Test]
        public void ShouldSuspendYieldAndDisposeWithoutPublishingStaleWork()
        {
            int published = 0;
            using AssetChangeRefreshService service = new(type => true, (guid, path, type) => published++, (path, known) => (path, path, typeof(PlayModeDataObject)));
            service.SetSuspended(true);
            service.Enqueue(new[] { "Assets/A.asset", "Assets/B.asset" });
            Assert.AreEqual(0, service.ProcessSlice());
            service.SetSuspended(false);
            Assert.AreEqual(1, service.ProcessSlice(0));
            Assert.AreEqual(1, service.PendingCount);
            service.Dispose();
            Assert.AreEqual(0, service.ProcessSlice());
            Assert.AreEqual(1, published);
            Assert.AreEqual(0, service.Enqueue(new[] { "Assets/A.asset" }));
        }
    }
}
