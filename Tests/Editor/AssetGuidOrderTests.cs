namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class AssetGuidOrderTests
    {
        [Test]
        public void Should_PreserveUnloadedSlots_When_LoadedSubsetIsReordered()
        {
            List<string> mergedOrder = AssetGuidOrder.MergeLoadedOrder(
                new[] { "a", "b", "c", "d" },
                new[] { "c", "a" }
            );

            CollectionAssert.AreEqual(new[] { "c", "b", "a", "d" }, mergedOrder);
        }

        [Test]
        public void Should_AppendCreatedGuidAfterPendingGuids_When_LoadIsPartial()
        {
            List<string> canonicalOrder = new() { "a", "b", "c", "d" };
            AssetGuidOrder.PlaceLast(canonicalOrder, "new");

            List<string> mergedOrder = AssetGuidOrder.MergeLoadedOrder(
                canonicalOrder,
                new[] { "a", "c", "new" }
            );

            CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "new" }, mergedOrder);
        }

        [Test]
        public void Should_MoveGuidAfterPendingGuids_When_PlacedLastDuringPartialLoad()
        {
            List<string> canonicalOrder = new() { "a", "b", "c", "d" };
            AssetGuidOrder.PlaceLast(canonicalOrder, "a");

            List<string> mergedOrder = AssetGuidOrder.MergeLoadedOrder(
                canonicalOrder,
                new[] { "c", "a" }
            );

            CollectionAssert.AreEqual(new[] { "b", "c", "d", "a" }, mergedOrder);
        }

        [Test]
        public void Should_MoveGuidBeforePendingGuids_When_PlacedFirstDuringPartialLoad()
        {
            List<string> canonicalOrder = new() { "a", "b", "c", "d" };
            AssetGuidOrder.PlaceFirst(canonicalOrder, "c");

            List<string> mergedOrder = AssetGuidOrder.MergeLoadedOrder(
                canonicalOrder,
                new[] { "c", "a" }
            );

            CollectionAssert.AreEqual(new[] { "c", "a", "b", "d" }, mergedOrder);
        }

        [Test]
        public void Should_InsertCloneAfterOriginal_When_LoadIsPartial()
        {
            List<string> canonicalOrder = new() { "a", "b", "c", "d" };
            AssetGuidOrder.PlaceAfter(canonicalOrder, "clone", "b");

            List<string> mergedOrder = AssetGuidOrder.MergeLoadedOrder(
                canonicalOrder,
                new[] { "a", "b", "clone", "d" }
            );

            CollectionAssert.AreEqual(new[] { "a", "b", "clone", "c", "d" }, mergedOrder);
        }

        [Test]
        public void Should_AppendUnknownLoadedGuid_When_CanonicalOrderDoesNotContainIt()
        {
            List<string> mergedOrder = AssetGuidOrder.MergeLoadedOrder(
                new[] { "a", "b" },
                new[] { "a", "new" }
            );

            CollectionAssert.AreEqual(new[] { "a", "b", "new" }, mergedOrder);
        }

        [Test]
        public void Should_NotRestoreDeletedGuid_When_MergingPartialLoad()
        {
            List<string> canonicalOrder = new() { "a", "c", "d" };

            List<string> mergedOrder = AssetGuidOrder.MergeLoadedOrder(
                canonicalOrder,
                new[] { "a", "c" }
            );

            CollectionAssert.AreEqual(new[] { "a", "c", "d" }, mergedOrder);
            CollectionAssert.DoesNotContain(mergedOrder, "b");
        }
    }
}
