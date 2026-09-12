namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Search;

    public sealed class SearchHighlightUtilityTests
    {
        private static List<(int Start, int Length)> CollectMatches(
            string fullText,
            params string[] terms
        )
        {
            List<(int Start, int Length)> matches = new();
            SearchHighlightUtility.CollectMatches(fullText, terms, matches);
            return matches;
        }

        private static IEnumerable<TestCaseData> NoMatchCases()
        {
            yield return new TestCaseData(null, new[] { "term" }).SetName(
                "ShouldCollectNoMatchesWhenInputIsInvalid(null text)"
            );
            yield return new TestCaseData(string.Empty, new[] { "term" }).SetName(
                "ShouldCollectNoMatchesWhenInputIsInvalid(empty text)"
            );
            yield return new TestCaseData("text", null).SetName(
                "ShouldCollectNoMatchesWhenInputIsInvalid(null terms)"
            );
            yield return new TestCaseData("text", Array.Empty<string>()).SetName(
                "ShouldCollectNoMatchesWhenInputIsInvalid(empty terms)"
            );
        }

        private static IEnumerable<TestCaseData> BuildCases()
        {
            yield return new TestCaseData(null, new List<(int, int)>(), true, string.Empty).SetName(
                "ShouldBuildHighlightedRichTextWhenScenarioApplies(null text)"
            );
            yield return new TestCaseData(
                "   ",
                new List<(int, int)>(),
                true,
                string.Empty
            ).SetName("ShouldBuildHighlightedRichTextWhenScenarioApplies(whitespace text)");
            yield return new TestCaseData("plain", null, true, "plain").SetName(
                "ShouldBuildHighlightedRichTextWhenScenarioApplies(null matches)"
            );
            yield return new TestCaseData("<x>", new List<(int, int)>(), true, "&lt;x&gt;").SetName(
                "ShouldBuildHighlightedRichTextWhenScenarioApplies(no matches escapes)"
            );
            /*
                A whitespace-only gap between two matches renders between the bold terms
                so adjacent matches keep their spacing (issue #78).
            */
            yield return new TestCaseData(
                "Ab ab",
                new List<(int, int)> { (0, 2), (3, 2) },
                true,
                "<color=yellow><b>Ab</b></color> <color=yellow><b>ab</b></color>"
            ).SetName("ShouldBuildHighlightedRichTextWhenScenarioApplies(colorified matches)");
            yield return new TestCaseData(
                "Ab ab",
                new List<(int, int)> { (0, 2), (3, 2) },
                false,
                "<b>Ab</b> <b>ab</b>"
            ).SetName("ShouldBuildHighlightedRichTextWhenScenarioApplies(hover keeps bold)");
            yield return new TestCaseData(
                " a",
                new List<(int, int)> { (1, 1) },
                true,
                " <color=yellow><b>a</b></color>"
            ).SetName(
                "ShouldBuildHighlightedRichTextWhenScenarioApplies(whitespace-only leading gap)"
            );
            yield return new TestCaseData(
                "a ",
                new List<(int, int)> { (0, 1) },
                true,
                "<color=yellow><b>a</b></color> "
            ).SetName("ShouldBuildHighlightedRichTextWhenScenarioApplies(whitespace-only tail)");
            yield return new TestCaseData(
                "<b>",
                new List<(int, int)> { (1, 1) },
                true,
                "&lt;<color=yellow><b>b</b></color>&gt;"
            ).SetName("ShouldBuildHighlightedRichTextWhenScenarioApplies(escapes segments)");
            yield return new TestCaseData(
                "abcd",
                new List<(int, int)> { (0, 4), (0, 3) },
                true,
                "<color=yellow><b>abcd</b></color>"
            ).SetName("ShouldBuildHighlightedRichTextWhenScenarioApplies(overlaps skipped)");
            yield return new TestCaseData(
                "abc",
                new List<(int, int)> { (2, 1) },
                true,
                "ab<color=yellow><b>c</b></color>"
            ).SetName("ShouldBuildHighlightedRichTextWhenScenarioApplies(match at end)");
        }

        [TestCaseSource(nameof(NoMatchCases))]
        public void ShouldCollectNoMatchesWhenInputIsInvalid(string fullText, string[] terms)
        {
            List<(int Start, int Length)> matches = new();

            SearchHighlightUtility.CollectMatches(fullText, terms, matches);

            CollectionAssert.IsEmpty(matches);
        }

        [Test]
        public void ShouldSkipWhitespaceTermsWhenCollectingMatches()
        {
            List<(int Start, int Length)> matches = new();

            SearchHighlightUtility.CollectMatches("abc", new[] { " ", "b", null }, matches);

            CollectionAssert.AreEqual(new[] { (1, 1) }, matches);
        }

        [Test]
        public void ShouldCollectSortedMatchesAcrossTerms()
        {
            List<(int Start, int Length)> matches = new();

            SearchHighlightUtility.CollectMatches("ab", new[] { "b", "a" }, matches);

            CollectionAssert.AreEqual(new[] { (0, 1), (1, 1) }, matches);
        }

        [Test]
        public void ShouldKeepTermInputOrderWhenTermsMatchAtSameIndex()
        {
            List<(int Start, int Length)> matches = new();

            SearchHighlightUtility.CollectMatches("abc", new[] { "ab", "abc" }, matches);

            CollectionAssert.AreEqual(new[] { (0, 2), (0, 3) }, matches);
        }

        [Test]
        public void ShouldCollectRepeatedMatchesWithinOneTerm()
        {
            List<(int Start, int Length)> matches = new();

            SearchHighlightUtility.CollectMatches("abab", new[] { "ab" }, matches);

            CollectionAssert.AreEqual(new[] { (0, 2), (2, 2) }, matches);
        }

        [Test]
        public void ShouldBuildCompoundHighlightedTextWithEscapedGaps()
        {
            List<(int Start, int Length)> matches = CollectMatches(
                "See <Value> and value",
                "value"
            );

            CollectionAssert.AreEqual(new[] { (5, 5), (16, 5) }, matches);

            string result = SearchHighlightUtility.BuildHighlightedRichText(
                "See <Value> and value",
                matches,
                true
            );

            Assert.AreEqual(
                "See &lt;<color=yellow><b>Value</b></color>&gt; and "
                    + "<color=yellow><b>value</b></color>",
                result
            );
        }

        [TestCaseSource(nameof(BuildCases))]
        public void ShouldBuildHighlightedRichTextWhenScenarioApplies(
            string fullText,
            List<(int Start, int Length)> matches,
            bool colorify,
            string expected
        )
        {
            string result = SearchHighlightUtility.BuildHighlightedRichText(
                fullText,
                matches,
                colorify
            );

            Assert.AreEqual(expected, result);
        }
    }
}
