namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class AssetPathChangeQueueTests
    {
        [Test]
        public void ShouldSnapshotOverlappingBatchesAndResetPartialOffsets()
        {
            AssetPathChangeQueue queue = new();
            Assert.AreEqual(0, queue.Enqueue(null));
            Assert.AreEqual(0, queue.Enqueue(new string[0]));
            string[] first = { "first", null, "third" };
            queue.Enqueue(first);
            queue.Enqueue(new[] { "last" });
            first[0] = "mutated";
            Assert.AreEqual(4, queue.Count);
            foreach (string expected in new[] { "first", null, "third", "last" })
            {
                Assert.That(queue.TryDequeue(out string path));
                Assert.AreEqual(expected, path);
            }
            Assert.AreEqual(0, queue.Count);
            Assert.That(!queue.TryDequeue(out string absent));
            Assert.That(absent, Is.EqualTo(null));
            queue.Enqueue(first);
            Assert.That(queue.TryDequeue(out _));
            queue.Clear();
            queue.Enqueue(new[] { "fresh" });
            Assert.That(queue.TryDequeue(out string fresh));
            Assert.AreEqual("fresh", fresh);
            Assert.AreEqual(0, queue.Count);
        }
    }
}
