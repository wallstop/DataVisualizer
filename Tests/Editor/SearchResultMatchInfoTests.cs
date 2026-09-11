namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Search;

    public sealed class SearchResultMatchInfoTests
    {
        [Test]
        public void ShouldReturnNoTermsWhenNoFieldsMatch()
        {
            SearchResultMatchInfo matchInfo = new();

            List<string> matchedTerms = new(matchInfo.AllMatchedTerms);

            CollectionAssert.IsEmpty(matchedTerms);
        }

        [Test]
        public void ShouldKeepFirstSeenTermWhenMatchesDifferOnlyByCase()
        {
            SearchResultMatchInfo matchInfo = new();
            matchInfo.matchedFields.Add(new MatchDetail("alpha", "BETA", "alpha"));
            matchInfo.matchedFields.Add(new MatchDetail("Alpha", "gamma", "beta"));

            List<string> matchedTerms = new(matchInfo.AllMatchedTerms);

            CollectionAssert.AreEqual(new[] { "alpha", "BETA", "gamma" }, matchedTerms);
        }

        [Test]
        public void ShouldReflectCurrentMatchesWithoutChangingEarlierMaterialization()
        {
            SearchResultMatchInfo matchInfo = new();
            matchInfo.matchedFields.Add(new MatchDetail("first"));
            List<string> firstEnumeration = new(matchInfo.AllMatchedTerms);

            matchInfo.matchedFields.Add(new MatchDetail("second"));
            List<string> secondEnumeration = new(matchInfo.AllMatchedTerms);

            CollectionAssert.AreEqual(new[] { "first" }, firstEnumeration);
            CollectionAssert.AreEqual(new[] { "first", "second" }, secondEnumeration);
        }
    }
}
