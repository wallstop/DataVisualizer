namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
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

        [Test]
        public void ShouldKeepLazyEnumerationsIndependentWhenInterleaved()
        {
            SearchResultMatchInfo matchInfo = new();
            matchInfo.matchedFields.Add(new MatchDetail("first", "second", "FIRST"));

            using IEnumerator<string> firstEnumeration = matchInfo.AllMatchedTerms.GetEnumerator();
            using IEnumerator<string> secondEnumeration = matchInfo.AllMatchedTerms.GetEnumerator();

            Assert.IsTrue(firstEnumeration.MoveNext());
            Assert.AreEqual("first", firstEnumeration.Current);
            Assert.IsTrue(secondEnumeration.MoveNext());
            Assert.AreEqual("first", secondEnumeration.Current);
            Assert.IsTrue(firstEnumeration.MoveNext());
            Assert.AreEqual("second", firstEnumeration.Current);
            Assert.IsTrue(secondEnumeration.MoveNext());
            Assert.AreEqual("second", secondEnumeration.Current);
            Assert.IsFalse(firstEnumeration.MoveNext());
            Assert.IsFalse(secondEnumeration.MoveNext());
        }

        [Test]
        public void ShouldRejectFieldMutationWhileEnumerationIsActive()
        {
            SearchResultMatchInfo matchInfo = new();
            matchInfo.matchedFields.Add(new MatchDetail("first", "second"));
            using IEnumerator<string> enumeration = matchInfo.AllMatchedTerms.GetEnumerator();
            Assert.IsTrue(enumeration.MoveNext());

            matchInfo.matchedFields.Add(new MatchDetail("third"));

            Assert.IsTrue(enumeration.MoveNext());
            Assert.AreEqual("second", enumeration.Current);
            Assert.Throws<InvalidOperationException>(() => enumeration.MoveNext());
        }

        [Test]
        public void ShouldClearReusableStateWhenEnumerationStopsEarly()
        {
            SearchResultMatchInfo matchInfo = new();
            matchInfo.matchedFields.Add(new MatchDetail("first", "second"));
            using (
                IEnumerator<string> partialEnumeration = matchInfo.AllMatchedTerms.GetEnumerator()
            )
            {
                Assert.IsTrue(partialEnumeration.MoveNext());
                Assert.AreEqual("first", partialEnumeration.Current);
            }

            List<string> completeEnumeration = new(matchInfo.AllMatchedTerms);

            CollectionAssert.AreEqual(new[] { "first", "second" }, completeEnumeration);
        }
    }
}
