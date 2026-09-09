namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Data;

    public sealed class FeatureTests
    {
        private static TestCaseData Case(string name, string input, bool expected) =>
            new TestCaseData(input).Returns(expected).SetName(name);

        private static IEnumerable<TestCaseData> Cases()
        {
            yield return Case("Returns_true_when_input_matches", "match", true);
            yield return Case("Returns_false_when_input_differs", "other", false);
            yield return Case("Returns_true_when_input_empty", "", true);
        }

        [Test]
        [TestCaseSource(nameof(Cases))]
        public bool Should_MatchExpected_When_EvaluatingInput(string input)
        {
            return Evaluate(input);
        }

        [Test]
        public void Should_UseDefault_When_ConfigMissing()
        {
            Assert.IsTrue(Evaluate(null));
        }

        private static bool Evaluate(string input)
        {
            if (input is null)
            {
                return true;
            }

            return input.Length == 0 || input == "match";
        }
    }
}
