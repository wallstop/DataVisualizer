namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Search;

    public sealed class AssetSearchQueryTests
    {
        [TestCase(1)]
        [TestCase(25)]
        [TestCase(100)]
        public void ShouldContinueWithoutDuplicatesAndStopAtResultLimit(int limit)
        {
            List<AssetSearchDocument> documents = new();
            for (int index = 0; index < 75; index++)
            {
                documents.Add(new AssetSearchDocument(index.ToString(), "Matching record", typeof(AssetSearchQueryTests), null));
            }
            AssetSearchQuery query = new(documents, new[] { "matching" }, limit);
            int slices = 0;
            while (!query.IsComplete)
            {
                query.Advance(0);
                slices++;
            }
            int expected = Math.Min(limit, documents.Count);
            Assert.AreEqual(expected, query.Results.Count);
            Assert.AreEqual(expected, query.ScannedCount);
            Assert.AreEqual(expected, slices);
            Assert.AreEqual(limit < documents.Count, query.ReachedLimit);
            for (int index = 0; index < expected; index++)
            {
                Assert.AreEqual(index.ToString(), query.Results[index].Document.Guid);
            }
            query.Advance();
            Assert.AreEqual(expected, query.Results.Count);
        }

        [Test]
        public void ShouldFinishRareAndNoMatchQueriesWithoutChangingCapturedTerms()
        {
            List<AssetSearchDocument> documents = new();
            for (int index = 0; index < 100; index++)
            {
                documents.Add(new AssetSearchDocument(index.ToString(), index == 99 ? "rare" : "ordinary", typeof(AssetSearchQueryTests), null));
            }
            string[] terms = { "rare", "absent" };
            AssetSearchQuery query = new(documents, terms, 25);
            terms[0] = "ordinary";
            while (!query.IsComplete)
            {
                query.Advance(0);
            }
            Assert.AreEqual(100, query.ScannedCount);
            Assert.AreEqual(1, query.Results.Count);
            Assert.AreEqual("99", query.Results[0].Document.Guid);
            AssetSearchQuery noMatch = new(documents, new[] { "absent" }, 25);
            noMatch.Advance(double.PositiveInfinity);
            Assert.That(noMatch.IsComplete);
            Assert.That(noMatch.Results, Is.Empty);
            Assert.That(!noMatch.ReachedLimit);
        }

        [TestCase("café", "CAFÉ")]
        [TestCase("İstanbul", "istanbul")]
        [TestCase("Σίσυφος", "σίσυφος")]
        [TestCase("Ordinary", "ORDINARY")]
        public void ShouldPreserveOrdinalIgnoreCaseForAsciiAndUnicode(string name, string term)
        {
            AssetSearchDocument document = new("guid", name, typeof(AssetSearchQueryTests), null);
            AssetSearchQuery query = new(new[] { document }, new[] { term }, 25);
            query.Advance();
            Assert.AreEqual(name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0, query.Results.Count);
        }

        [Test]
        public void ShouldFinishItsSnapshotWhenTheLiveIndexChangesBetweenSlices()
        {
            List<AssetSearchDocument> documents = new();
            for (int index = 0; index < 100; index++)
            {
                documents.Add(new AssetSearchDocument(index.ToString(), index == 99 ? "late" : "ordinary", typeof(AssetSearchQueryTests), null));
            }
            AssetSearchQuery query = new(documents, new[] { "late" }, 25);
            for (int index = 0; index < 100; index++)
            {
                query.Advance(0);
                documents.Insert(0, new AssetSearchDocument("new-" + index, "ordinary", typeof(AssetSearchQueryTests), null));
            }
            Assert.That(query.IsComplete);
            Assert.AreEqual(100, query.RecordCount);
            Assert.AreEqual(100, query.ScannedCount);
            Assert.AreEqual(1, query.Results.Count);
            Assert.AreEqual("99", query.Results[0].Document.Guid);
        }

        [Test]
        public void ShouldHandleEmptyInputAndRejectInvalidBudgetsOrLimits()
        {
            AssetSearchQuery query = new(Array.Empty<AssetSearchDocument>(), null, 25);
            query.Advance();
            Assert.That(query.IsComplete);
            Assert.That(query.Results, Is.Empty);
            Assert.Throws<ArgumentOutOfRangeException>(() => query.Advance(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => query.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AssetSearchQuery(Array.Empty<AssetSearchDocument>(), null, 0));
        }
    }
}
