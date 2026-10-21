namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class AssetGuidTypeIndexQueueTests
    {
        private static string CreateFolder(TestCleanupScope cleanup)
        {
            string folder = "Assets/QueuedTypeIndex-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            return folder;
        }

        private static string CreateAsset(string path)
        {
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), path);
            return AssetDatabase.AssetPathToGUID(path);
        }

        private static void Drain(AssetGuidTypeIndex index)
        {
            for (int tick = 0; tick < 8 && (!index.IsComplete || 0 < index.PendingPathCount); tick++)
            {
                index.ProcessPendingSlice(double.PositiveInfinity);
            }
            Assert.That(index.IsComplete);
            Assert.AreEqual(0, index.PendingPathCount);
        }

        private static AssetGuidTypeIndex CreateIndex(TestCleanupScope cleanup)
        {
            AssetGuidTypeIndex index = new(false);
            cleanup.Defer(index.Cancel);
            index.EnsureStarted();
            Drain(index);
            return index;
        }

        [Test]
        public void ShouldCoalesceEveryCollectionWithoutClassifyingInsideEnqueue()
        {
            using TestCleanupScope cleanup = new();
            string root = CreateFolder(cleanup);
            AssetGuidTypeIndex index = CreateIndex(cleanup);
            string first = root + "/First.asset";
            string second = root + "/Second.asset";
            string firstGuid = CreateAsset(first);
            string secondGuid = CreateAsset(second);
            int queries = index.TypeQueryCount;
            int snapshots = 0;
            int completions = 0;
            IReadOnlyList<AssetGuidTypeIndexChange> changes = Array.Empty<AssetGuidTypeIndexChange>();
            index.SnapshotCompleted += () => snapshots++;
            index.IndexCompleted += () => completions++;
            index.AssetChangesIndexed += value => changes = value;
            Assert.AreEqual(
                2,
                index.EnqueueAssetChanges(new[] { first, first.ToUpperInvariant(), second, root + "/Ignored.txt" }, new[] { first }, new[] { second }, new[] { second })
            );
            Assert.AreEqual(2, index.PendingPathCount);
            Assert.AreEqual(queries, index.TypeQueryCount);
            CollectionAssert.DoesNotContain(index.GetKnownGuids(typeof(TestDataObject)), firstGuid);
            Assert.That(!index.ProcessPendingSlice(0));
            Assert.AreEqual(queries + 1, index.TypeQueryCount);
            Assert.AreEqual(1, index.PendingPathCount);
            Assert.That(index.ProcessPendingSlice(double.PositiveInfinity));
            Assert.AreEqual(queries + 2, index.TypeQueryCount);
            CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), firstGuid);
            CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), secondGuid);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(0, snapshots);
            Assert.AreEqual(1, completions);
        }

        [Test]
        public void ShouldReserveSnapshotNotificationsForFullScans()
        {
            using TestCleanupScope cleanup = new();
            string root = CreateFolder(cleanup);
            string path = root + "/Existing.asset";
            CreateAsset(path);
            AssetGuidTypeIndex index = new(false);
            cleanup.Defer(index.Cancel);
            int snapshots = 0;
            int changes = 0;
            int completions = 0;
            index.SnapshotCompleted += () => snapshots++;
            index.AssetChangesIndexed += _ => changes++;
            index.IndexCompleted += () => completions++;
            index.EnsureStarted();
            Drain(index);
            Assert.AreEqual(1, snapshots);
            Assert.AreEqual(1, completions);
            index.EnqueueAssetChanges(new[] { path }, null, null, null);
            Drain(index);
            Assert.AreEqual(1, snapshots);
            Assert.AreEqual(2, completions);
            Assert.AreEqual(0, changes);
            index.Rebuild();
            Drain(index);
            Assert.AreEqual(2, snapshots);
            Assert.AreEqual(3, completions);
        }

        [Test]
        public void ShouldRetainPriorGuidForDeferredMovesAndDeletion()
        {
            using TestCleanupScope cleanup = new();
            string root = CreateFolder(cleanup);
            string original = root + "/Original.asset";
            string moved = root + "/Moved.asset";
            string guid = CreateAsset(original);
            AssetGuidTypeIndex index = CreateIndex(cleanup);
            IReadOnlyList<AssetGuidTypeIndexChange> changes = Array.Empty<AssetGuidTypeIndexChange>();
            index.AssetChangesIndexed += value => changes = value;
            Assert.AreEqual(string.Empty, AssetDatabase.MoveAsset(original, moved));
            Assert.AreEqual(2, index.EnqueueAssetChanges(null, null, new[] { moved }, new[] { original }));
            Drain(index);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(original, changes[0].Path);
            Assert.AreEqual(guid, changes[0].PreviousGuid);
            Assert.AreEqual(moved, changes[1].Path);
            CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), guid);
            Assert.That(AssetDatabase.DeleteAsset(moved));
            index.EnqueueAssetChanges(null, new[] { moved }, null, null);
            CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), guid);
            Drain(index);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(guid, changes[0].PreviousGuid);
            CollectionAssert.DoesNotContain(index.GetKnownGuids(typeof(TestDataObject)), guid);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldDeferWhileBusyOrSuspended(bool suspended)
        {
            using TestCleanupScope cleanup = new();
            string root = CreateFolder(cleanup);
            AssetGuidTypeIndex index = CreateIndex(cleanup);
            string path = root + "/Deferred.asset";
            string guid = CreateAsset(path);
            bool busy = !suspended;
            index.AssetDatabaseBusyOverride = () => busy;
            if (suspended)
            {
                index.Suspend();
            }
            int queries = index.TypeQueryCount;
            index.EnqueueAssetChanges(new[] { path }, null, null, null);
            Assert.That(!index.ProcessPendingSlice(double.PositiveInfinity));
            Assert.AreEqual(queries, index.TypeQueryCount);
            Assert.AreEqual(1, index.PendingPathCount);
            busy = false;
            index.Resume();
            Drain(index);
            Assert.AreEqual(queries + 1, index.TypeQueryCount);
            CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), guid);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldKeepWorkQueuedFromCompletionCallbacks(bool rebuild)
        {
            using TestCleanupScope cleanup = new();
            string root = CreateFolder(cleanup);
            AssetGuidTypeIndex index = CreateIndex(cleanup);
            string first = root + "/First.asset";
            string second = root + "/Second.asset";
            CreateAsset(first);
            string secondGuid = CreateAsset(second);
            bool queued = false;
            int completions = 0;
            IReadOnlyList<AssetGuidTypeIndexChange> firstNotification = null;
            index.IndexCompleted += () => completions++;
            index.AssetChangesIndexed += changes =>
            {
                if (queued)
                {
                    return;
                }
                queued = true;
                firstNotification = changes;
                if (rebuild)
                {
                    index.Rebuild();
                }
                index.EnqueueAssetChanges(new[] { second }, null, null, null);
            };
            index.EnqueueAssetChanges(new[] { first }, null, null, null);
            Assert.That(!index.ProcessPendingSlice(0));
            Assert.AreEqual(0, completions);
            Drain(index);
            Assert.AreEqual(1, completions);
            Assert.AreEqual(1, firstNotification.Count);
            Assert.AreEqual(first, firstNotification[0].Path);
            CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), secondGuid);
        }

        [Test]
        public void ShouldDiscardCanceledPartialChangesBeforeANewSnapshot()
        {
            using TestCleanupScope cleanup = new();
            string root = CreateFolder(cleanup);
            AssetGuidTypeIndex index = CreateIndex(cleanup);
            string first = root + "/First.asset";
            string second = root + "/Second.asset";
            CreateAsset(first);
            CreateAsset(second);
            int changes = 0;
            index.AssetChangesIndexed += _ => changes++;
            index.EnqueueAssetChanges(new[] { first, second }, null, null, null);
            Assert.That(!index.ProcessPendingSlice(0));
            index.Cancel();
            Assert.AreEqual(0, index.PendingPathCount);
            Assert.AreEqual(0, index.TypeQueryCount);
            index.EnsureStarted();
            Drain(index);
            Assert.AreEqual(0, changes);
        }
    }
}
