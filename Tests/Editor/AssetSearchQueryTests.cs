namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Search;

    public sealed class AssetSearchQueryTests
    {
        [Test]
        public void ShouldPreserveThePublicThreeArgumentConstructorSignature()
        {
            // Public metadata is the binary contract; optional arguments cannot preserve it.
            var constructor = typeof(AssetSearchQuery).GetConstructor(new[] { typeof(IReadOnlyList<AssetSearchDocument>), typeof(string[]), typeof(int) });
            Assert.That(constructor != null);
            AssetSearchQuery query = (AssetSearchQuery)constructor.Invoke(new object[] { Array.Empty<AssetSearchDocument>(), Array.Empty<string>(), 25 });
            query.Advance();
            Assert.That(query.IsComplete);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(31)]
        [TestCase(100)]
        [TestCase(257)]
        public void ShouldSortPartialSnapshotsAcrossBudgetedMergeSteps(int count)
        {
            List<AssetSearchDocument> documents = new();
            for (int index = count - 1; 0 <= index; index--)
            {
                documents.Add(new AssetSearchDocument(index.ToString("D4"), "Match " + index % 7, typeof(AssetSearchQueryTests), null));
            }
            AssetSearchDocument[] expected = documents.ToArray();
            Array.Sort(expected, AssetSearchDocument.CompareIdentity);
            AssetSearchQuery query = new(documents, new[] { "match" }, int.MaxValue, sortByIdentity: true);
            bool preparing = query.IsPreparing;
            query.Advance(0);
            if (preparing)
            {
                Assert.That(query.IsPreparing);
                Assert.AreEqual(0, query.ScannedCount);
                Assert.That(query.Results, Is.Empty);
            }
            // Arrivals and replacements cannot change this query's captured ordering.
            documents.Clear();
            documents.Add(new AssetSearchDocument("new", "Match arrival", typeof(AssetSearchQueryTests), null));
            int steps = 0;
            while (!query.IsComplete && steps++ < 10000)
            {
                query.Advance(0);
            }
            Assert.That(query.IsComplete);
            Assert.That(!query.IsPreparing);
            Assert.AreEqual(count, query.ScannedCount);
            Assert.AreEqual(count, query.Results.Count);
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.AreSame(expected[index], query.Results[index].Document);
            }
        }

        [Test]
        public void ShouldKeepOrdinalIdentityOrderAndLimitsAfterPreparation()
        {
            AssetSearchDocument[] documents =
            {
                new("c", "same", typeof(AssetSearchQueryTests), null),
                new("b", "Same", typeof(AssetSearchQueryTests), null),
                new("a", "Same", typeof(AssetSearchQueryTests), null),
                new("d", "Same", typeof(TestDataObject), null),
            };
            AssetSearchQuery query = new(documents, new[] { "same" }, 3, sortByIdentity: true);
            query.Advance(double.PositiveInfinity);
            Assert.That(query.IsComplete);
            Assert.That(query.ReachedLimit);
            Assert.AreEqual(3, query.ScannedCount);
            Assert.AreEqual("a", query.Results[0].Document.Guid);
            Assert.AreEqual("b", query.Results[1].Document.Guid);
            Assert.AreEqual("d", query.Results[2].Document.Guid);
        }

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

        [TestCase("Kelvin", "kelvin")]
        [TestCase("ſample", "sample")]
        [TestCase("Straße", "STRASSE")]
        [TestCase("ıstanbul", "ISTANBUL")]
        [TestCase("e\u0301", "É")]
        [TestCase("\U00010400", "\U00010428")]
        [TestCase("emoji 😀", "😀")]
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
        public void ShouldKeepBmpCaseGroupsCompatibleWithRuntimeOrdinalIgnoreCase()
        {
            Dictionary<string, string> groups = new(StringComparer.OrdinalIgnoreCase);
            for (int code = 0; code < 65536; code++)
            {
                char character = (char)code;
                if (char.IsSurrogate(character))
                {
                    continue;
                }
                string original = character.ToString();
                string normalized = AssetSearchDocument.NormalizeOrdinalOrNull(original);
                if (groups.TryGetValue(original, out string previous))
                {
                    Assert.That(normalized, Is.EqualTo(previous), code.ToString("X4"));
                }
                else
                {
                    groups.Add(original, normalized);
                }
            }
        }

        [TestCase("perf検索é000000", "PERF検索É000000")]
        [TestCase("Kelvin", "kelvin")]
        [TestCase("ſample", "sample")]
        [TestCase("Straße", "STRASSE")]
        [TestCase("İstanbul", "istanbul")]
        [TestCase("ıstanbul", "ISTANBUL")]
        [TestCase("e\u0301", "É")]
        [TestCase("\U00010400", "\U00010428")]
        [TestCase("emoji 😀", "😀")]
        public void ShouldPreserveFieldMatchesAndOrTermsThroughTheUnicodeFilter(string value, string term)
        {
            AssetSearchDocument document = new("exact-guid", "Ordinary", typeof(AssetSearchQueryTests), new SearchTextSource { Text = value });
            string[] terms = { "absent", term };
            SearchResultMatchInfo expected = document.MatchOrNull(terms);
            AssetSearchQuery query = new(new[] { document }, terms, 25);
            query.Advance(double.PositiveInfinity);
            Assert.That(query.Results.Count, Is.EqualTo(expected == null ? 0 : 1));
            if (expected != null)
            {
                Assert.That(query.Results[0].Match.MatchedFields[0].matchedValue, Is.EqualTo(value));
                Assert.That(query.Results[0].Match.MatchedFields[0].fieldName, Is.EqualTo(nameof(SearchTextSource.Text)));
            }
        }

        [Test]
        public void ShouldCacheUnicodeOnceWithoutDuplicatingTheAsciiCache()
        {
            AssetSearchDocument ascii = new("guid", "Ordinary", typeof(AssetSearchQueryTests), null);
            Assert.That(ascii.NormalizedAsciiText != null);
            Assert.That(ascii.NormalizedOrdinalText == null);
            AssetSearchDocument unicode = new("guid", "café", typeof(AssetSearchQueryTests), null);
            Assert.That(unicode.NormalizedAsciiText == null);
            Assert.That(unicode.NormalizedOrdinalText, Does.Contain("CAFÉ"));
            AssetSearchDocument supplementary = new("guid", "😀", typeof(AssetSearchQueryTests), null);
            Assert.That(supplementary.NormalizedAsciiText == null);
            Assert.That(supplementary.NormalizedOrdinalText == null);
            Assert.That(AssetSearchDocument.NormalizeOrdinalOrNull(null) == null);
            AssetSearchQuery boundary = new(new[] { unicode }, new[] { "café\0AssetSearchQueryTests" }, 25);
            boundary.Advance(double.PositiveInfinity);
            Assert.That(boundary.Results, Is.Empty);
            AssetSearchQuery guidSubstring = new(new[] { unicode }, new[] { "gui" }, 25);
            guidSubstring.Advance(double.PositiveInfinity);
            Assert.That(guidSubstring.Results, Is.Empty);
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

        private sealed class SearchTextSource
        {
            public string Text;
        }
    }
}
