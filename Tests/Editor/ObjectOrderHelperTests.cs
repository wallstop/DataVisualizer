namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    [TestFixture]
    public sealed class ObjectOrderHelperTests
    {
        [Test]
        public void ReorderItemInsertsBeforeTarget()
        {
            List<string> items = new() { "1", "2", "3" };

            ObjectOrderHelper.ReorderItem(items, "3", insertBefore: "2", insertAfter: null);

            CollectionAssert.AreEqual(new[] { "1", "3", "2" }, items);
        }

        [Test]
        public void ReorderItemInsertsAfterTargetWhenBeforeUnavailable()
        {
            List<string> items = new() { "alpha", "beta", "gamma" };

            ObjectOrderHelper.ReorderItem(items, "alpha", insertBefore: null, insertAfter: "beta");

            CollectionAssert.AreEqual(new[] { "beta", "alpha", "gamma" }, items);
        }

        [Test]
        public void ReorderItemMovesElementToEnd()
        {
            List<string> items = new() { "1", "2", "3" };

            ObjectOrderHelper.ReorderItem(items, "2", insertBefore: null, insertAfter: "3");

            CollectionAssert.AreEqual(new[] { "1", "3", "2" }, items);
        }

        [Test]
        public void ReorderItemAppendsWhenTargetsMissing()
        {
            List<string> items = new() { "a", "b" };

            ObjectOrderHelper.ReorderItem(
                items,
                "a",
                insertBefore: "missing",
                insertAfter: "also-missing"
            );

            CollectionAssert.AreEqual(new[] { "b", "a" }, items);
        }

        [Test]
        public void ReorderItemNoOpWhenItemMissing()
        {
            List<string> items = new() { "one", "two" };

            ObjectOrderHelper.ReorderItem(items, "three", insertBefore: "one", insertAfter: null);

            CollectionAssert.AreEqual(new[] { "one", "two" }, items);
        }
    }
}
