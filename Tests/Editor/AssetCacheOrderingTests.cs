namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Search;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class AssetCacheOrderingTests
    {
        private static AssetObjectCache CreateCache(AssetMetadataStore metadata, int count)
        {
            AssetObjectCache cache = new(metadata);
            for (int index = count - 1; 0 <= index; index--)
            {
                Put(metadata, cache, index.ToString(), "Match " + index % 7);
            }
            return cache;
        }

        private static void Put(AssetMetadataStore metadata, AssetObjectCache cache, string guid, string name)
        {
            AssetSearchDocument document = new(guid, name, typeof(AssetCacheOrderingTests), null);
            metadata.Upsert(new AssetMetadataRecord(document, document.ExactType, "Assets/" + guid + ".asset", name, null));
            Assert.That(cache.UseMetadata(guid, new HashSet<Type> { typeof(AssetCacheOrderingTests) }));
        }

        private static void AssertLookups(AssetObjectCache cache)
        {
            for (int index = 0; index < cache.Documents.Count; index++)
            {
                AssetSearchDocument document = cache.Documents[index];
                Assert.That(cache.TryGetDocument(document.Guid, out AssetSearchDocument found));
                Assert.That(ReferenceEquals(found, document));
            }
        }

        [TestCase(2, false)]
        [TestCase(31, false)]
        [TestCase(257, true)]
        public void ShouldSharePreparationWithoutSharingQueryProgress(int count, bool advanceWaitingQuery)
        {
            AssetMetadataStore metadata = new();
            AssetObjectCache cache = CreateCache(metadata, count);
            List<AssetSearchDocument> expected = new(cache.Documents);
            expected.Sort(AssetSearchDocument.CompareIdentity);
            AssetSearchQuery waiting = cache.CreateQuery(new[] { "Match" }, int.MaxValue);
            AssetSearchQuery limited = cache.CreateQuery(new[] { "Match" }, 1);
            if (advanceWaitingQuery)
            {
                waiting.Advance(0);
            }
            Assert.That(waiting.IsPreparing);
            while (!limited.IsComplete)
            {
                limited.Advance(0);
            }
            Assert.That(!waiting.IsPreparing, "A sibling completed this snapshot's preparation");
            Assert.That(waiting.ScannedCount, Is.Zero);
            Assert.That(waiting.Results.Count, Is.Zero);
            Assert.That(limited.Results.Count, Is.EqualTo(1));
            AssetSearchQuery absent = cache.CreateQuery(new[] { "absent" }, int.MaxValue);
            Assert.That(!absent.IsPreparing, "Later queries reuse completed preparation");
            while (!absent.IsComplete || !waiting.IsComplete)
            {
                absent.Advance(0);
                waiting.Advance(0);
            }
            Assert.That(absent.Results.Count, Is.Zero);
            Assert.That(waiting.Results.Count, Is.EqualTo(count));
            for (int index = 0; index < expected.Count; index++)
            {
                Assert.That(ReferenceEquals(waiting.Results[index].Document, expected[index]));
            }
            Put(metadata, cache, "arrival", "A Match arrival");
            AssetSearchQuery changed = cache.CreateQuery(new[] { "Match" }, int.MaxValue);
            Assert.That(changed.IsPreparing, "A changed snapshot prepares independently");
            Assert.That(!waiting.IsPreparing);
            Assert.That(waiting.RecordCount, Is.EqualTo(count));
            Assert.That(cache.IsSorted, Is.False, "Query preparation does not publish or mutate the cache");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(31)]
        [TestCase(257)]
        public void ShouldPublishSortedDocumentsAndLookupsTogether(int count)
        {
            AssetMetadataStore metadata = new();
            AssetObjectCache cache = CreateCache(metadata, count);
            List<AssetSearchDocument> original = new(cache.Documents);
            List<AssetSearchDocument> expected = new(original);
            expected.Sort(AssetSearchDocument.CompareIdentity);
            int version = cache.Version;
            int steps = 0;
            while (!cache.AdvanceSort(0))
            {
                Assert.That(cache.Version, Is.EqualTo(version));
                Assert.That(!cache.IsSorted);
                CollectionAssert.AreEqual(original, cache.Documents);
                AssertLookups(cache);
                Assert.That(++steps, Is.LessThan(10000));
            }
            Assert.That(cache.IsSorted);
            CollectionAssert.AreEqual(expected, cache.Documents);
            AssertLookups(cache);
            Assert.That(cache.RetainedObjectCount, Is.Zero);
            Assert.That(cache.ObjectLoadCount, Is.Zero);
            Assert.That(cache.Version, Is.EqualTo(version + (1 < count ? 1 : 0)));
            version = cache.Version;
            Assert.That(cache.AdvanceSort(0));
            Assert.That(cache.Version, Is.EqualTo(version));
        }

        [TestCase("remove", false)]
        [TestCase("remove", true)]
        [TestCase("replace", false)]
        [TestCase("replace", true)]
        [TestCase("add", false)]
        [TestCase("add", true)]
        [TestCase("clear", false)]
        [TestCase("clear", true)]
        [TestCase("cancel", false)]
        [TestCase("cancel", true)]
        [TestCase("sync", false)]
        [TestCase("sync", true)]
        public void ShouldDiscardStalePreparationWhenCacheChanges(string mutation, bool preparingLookup)
        {
            AssetMetadataStore metadata = new();
            AssetObjectCache cache = CreateCache(metadata, 31);
            Assert.That(!cache.AdvanceSort(preparingLookup ? double.PositiveInfinity : 0));
            if (preparingLookup)
            {
                Assert.That(!cache.AdvanceSort(0));
            }
            AssetSearchQuery snapshot = new(cache.Documents, new[] { "Match" }, int.MaxValue, true);
            switch (mutation)
            {
                case "remove":
                    Assert.That(cache.Remove("0"));
                    break;
                case "replace":
                    Put(metadata, cache, "0", "A Match replacement");
                    break;
                case "add":
                    Put(metadata, cache, "new", "A Match arrival");
                    break;
                case "clear":
                    cache.Clear();
                    Put(metadata, cache, "new", "Match after reset");
                    break;
                case "cancel":
                    cache.CancelOrdering();
                    break;
                case "sync":
                    cache.Sort();
                    break;
                default:
                    Assert.Fail("Unknown mutation");
                    break;
            }
            List<AssetSearchDocument> expected = new(cache.Documents);
            expected.Sort(AssetSearchDocument.CompareIdentity);
            int steps = 0;
            while (!cache.AdvanceSort(0))
            {
                AssertLookups(cache);
                Assert.That(++steps, Is.LessThan(10000));
            }
            CollectionAssert.AreEqual(expected, cache.Documents);
            AssertLookups(cache);
            while (!snapshot.IsComplete)
            {
                snapshot.Advance();
            }
            Assert.That(snapshot.Results.Count, Is.EqualTo(31), "An in-flight query keeps its original snapshot");
            if (mutation == "remove" || mutation == "clear")
            {
                Assert.That(!cache.Contains("0"));
            }
        }

        [TestCase("remove", false)]
        [TestCase("remove", true)]
        [TestCase("replace", false)]
        [TestCase("replace", true)]
        [TestCase("add", false)]
        [TestCase("add", true)]
        [TestCase("clear", false)]
        [TestCase("clear", true)]
        [TestCase("sync", false)]
        [TestCase("sync", true)]
        [TestCase("async", false)]
        [TestCase("async", true)]
        public void ShouldKeepSharedQueriesStableAcrossCacheMutations(string mutation, bool sorted)
        {
            AssetMetadataStore metadata = new();
            AssetObjectCache cache = CreateCache(metadata, 31);
            if (sorted)
            {
                cache.Sort();
            }
            List<AssetSearchDocument> expected = new(cache.Documents);
            expected.Sort(AssetSearchDocument.CompareIdentity);
            AssetSearchQuery first = cache.CreateQuery(new[] { "Match" }, int.MaxValue);
            first.Advance(0);
            AssetSearchQuery second = cache.CreateQuery(new[] { "Match" }, int.MaxValue);
            switch (mutation)
            {
                case "remove":
                    cache.Remove("0");
                    break;
                case "replace":
                    Put(metadata, cache, "0", "Replacement");
                    break;
                case "add":
                    Put(metadata, cache, "new", "Match arrival");
                    break;
                case "clear":
                    cache.Clear();
                    Put(metadata, cache, "new", "Match after reset");
                    break;
                case "sync":
                case "async":
                    Put(metadata, cache, "new", "A Match first");
                    if (mutation == "sync")
                    {
                        cache.Sort();
                    }
                    else
                    {
                        while (!cache.AdvanceSort(0)) { }
                    }
                    break;
            }
            foreach (AssetSearchQuery query in new[] { first, second })
            {
                while (!query.IsComplete)
                {
                    query.Advance();
                }
                Assert.That(query.RecordCount, Is.EqualTo(31));
                Assert.That(query.Results.Count, Is.EqualTo(31));
                for (int index = 0; index < expected.Count; index++)
                {
                    Assert.That(ReferenceEquals(query.Results[index].Document, expected[index]));
                }
            }
            AssetSearchQuery current = cache.CreateQuery(new[] { "Match" }, int.MaxValue);
            while (!current.IsComplete)
            {
                current.Advance();
            }
            Assert.That(current.RecordCount, Is.EqualTo(cache.Count));
            int expectedMatches =
                mutation == "clear" ? 1
                : mutation == "remove" || mutation == "replace" ? 30
                : 32;
            Assert.That(current.Results.Count, Is.EqualTo(expectedMatches));
            AssertLookups(cache);
            Assert.That(cache.RetainedObjectCount, Is.Zero);
            Assert.That(cache.ObjectLoadCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldKeepQueryConstructionAllocationIndependentOfRecordCount(bool sorted)
        {
            AssetObjectCache cache = CreateCache(new AssetMetadataStore(), 10000);
            if (sorted)
            {
                cache.Sort();
            }
            string[] terms = { "absent" };
            cache.CreateQuery(terms, 25);
            AssetSearchQuery[] queries = new AssetSearchQuery[32];
            long before = GC.GetTotalMemory(true);
            for (int index = 0; index < queries.Length; index++)
            {
                queries[index] = cache.CreateQuery(terms, 25);
            }
            long retained = GC.GetTotalMemory(true) - before;
            Assert.That(retained, Is.LessThan(128 * 1024), "Unadvanced cache queries must not each retain reference arrays per record");
            Assert.That(queries[0].RecordCount, Is.EqualTo(10000));
            GC.KeepAlive(queries);
            GC.KeepAlive(cache);
        }

        [Test]
        public void ShouldRejectInvalidBudgetsAndStronglyOwnedCooperativeSorts()
        {
            AssetObjectCache cache = new(new AssetMetadataStore());
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.AdvanceSort(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.AdvanceSort(-1));
            AssetObjectCache legacy = new(new List<UnityEngine.ScriptableObject>());
            Assert.Throws<InvalidOperationException>(() => legacy.AdvanceSort());
        }
    }
}
