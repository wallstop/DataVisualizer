namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Search;

    public sealed class AssetSearchDocumentTests
    {
        [TestCase("object", MatchSource.ObjectName)]
        [TestCase("searchnode", MatchSource.TypeName)]
        [TestCase("GUID-123", MatchSource.Guid)]
        [TestCase("nested", "Text")]
        public void ShouldPreserveMatchSourcesAndCaseInsensitiveTerms(string term, string expectedSource)
        {
            SearchNode source = new()
            {
                Text = "root",
                Child = new SearchNode { Text = "nested" },
            };
            AssetSearchDocument document = new("guid-123", "Object name", typeof(SearchNode), source);
            SearchResultMatchInfo result = document.Match(new[] { term });
            Assert.That(result.isMatch);
            Assert.AreEqual(expectedSource, result.MatchedFields[0].fieldName);
            CollectionAssert.AreEqual(new[] { term }, result.AllMatchedTerms);
        }

        [Test]
        public void ShouldKeepCapturedTextAfterSourceChangesAndPreferPrimaryMatches()
        {
            SearchNode source = new()
            {
                Text = "original",
                Child = new SearchNode { Text = "nested" },
            };
            AssetSearchDocument document = new("guid", "original", typeof(SearchNode), source);
            source.Text = "changed";
            source.Child.Text = "changed nested value";
            SearchResultMatchInfo result = document.Match(new[] { "original", "nested", "absent" });
            Assert.That(result.isMatch);
            Assert.AreEqual(2, result.MatchedFields.Count);
            Assert.AreEqual(MatchSource.ObjectName, result.MatchedFields[0].fieldName);
            Assert.AreEqual("nested", result.MatchedFields[1].matchedValue);
            Assert.That(!document.Match(new[] { "changed" }).isMatch);
        }

        [Test]
        public void ShouldBoundCyclesDepthAndNullsWithoutReadingProperties()
        {
            SearchNode root = new() { Text = null };
            root.Child = new SearchNode
            {
                Text = "one",
                Child = new SearchNode
                {
                    Text = "two",
                    Child = new SearchNode { Text = "too deep" },
                },
            };
            root.Cycle = root;
            AssetSearchDocument document = new("exact-guid", "Object", typeof(SearchNode), root);
            Assert.That(document.Match(new[] { "two" }).isMatch);
            Assert.That(!document.Match(new[] { "too deep" }).isMatch);
            Assert.That(!document.Match(new[] { "exact" }).isMatch, "GUID matching requires the whole GUID.");
            Assert.That(!document.Match(null).isMatch);
            Assert.That(!document.Match(Array.Empty<string>()).isMatch);
            Assert.That(!document.Match(new[] { null, " " }).isMatch);
            Assert.That(!new AssetSearchDocument(null, null, typeof(SearchNode), null).Match(new[] { "absent" }).isMatch);
        }

        [Test]
        public void ShouldRejectCombinedTextBoundaryAndGuidSubstringFalsePositives()
        {
            AssetSearchDocument document = new("exact-guid", "Object", typeof(SearchNode), new SearchNode { Text = "field value" });
            Assert.That(document.MatchOrNull(new[] { "exact" }) == null);
            Assert.That(document.MatchOrNull(new[] { "Object\0SearchNode" }) == null);
            Assert.That(document.MatchOrNull(new[] { "value" }) != null);
        }

        private sealed class SearchNode
        {
            public string ThrowingProperty => throw new InvalidOperationException("Search must not invoke properties.");

            public string Text;
            public SearchNode Child;
            public SearchNode Cycle;
        }
    }
}
