namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Data;

    public sealed class LabelFilterEvaluatorTests
    {
        private static readonly string[] None = Array.Empty<string>();

        private static TypeLabelFilterConfig Config(
            LabelCombinationType type,
            string[] and,
            string[] or
        ) =>
            new()
            {
                combinationType = type,
                andLabels = new List<string>(and),
                orLabels = new List<string>(or),
            };

        [Test]
        public void Should_IncludeEverything_When_NoLabelsConfigured()
        {
            TypeLabelFilterConfig config = Config(LabelCombinationType.And, None, None);
            Assert.IsTrue(LabelFilterEvaluator.Matches(new[] { "anything" }, config));
            Assert.IsTrue(LabelFilterEvaluator.Matches(None, config));
        }

        [Test]
        public void Should_IncludeEverything_When_ConfigIsNull()
        {
            Assert.IsTrue(LabelFilterEvaluator.Matches(new[] { "x" }, null));
        }

        private static TestCaseData Case(
            string name,
            LabelCombinationType type,
            string[] and,
            string[] or,
            string[] labels,
            bool expected
        ) => new TestCaseData(labels, Config(type, and, or)).Returns(expected).SetName(name);

        private static IEnumerable<TestCaseData> Cases()
        {
            // AND mode, only AND labels: object must have all of them.
            yield return Case(
                "And_all_present",
                LabelCombinationType.And,
                new[] { "a", "b" },
                None,
                new[] { "a", "b" },
                true
            );
            yield return Case(
                "And_superset",
                LabelCombinationType.And,
                new[] { "a", "b" },
                None,
                new[] { "a", "b", "c" },
                true
            );
            yield return Case(
                "And_missing_one",
                LabelCombinationType.And,
                new[] { "a", "b" },
                None,
                new[] { "a" },
                false
            );

            // AND mode, AND+OR: needs all AND labels and at least one OR label.
            yield return Case(
                "And_and_or_satisfied",
                LabelCombinationType.And,
                new[] { "a", "b" },
                new[] { "x", "y" },
                new[] { "a", "b", "x" },
                true
            );
            yield return Case(
                "And_and_or_missing_or",
                LabelCombinationType.And,
                new[] { "a", "b" },
                new[] { "x", "y" },
                new[] { "a", "b" },
                false
            );
            yield return Case(
                "And_and_or_missing_and",
                LabelCombinationType.And,
                new[] { "a", "b" },
                new[] { "x", "y" },
                new[] { "a", "x" },
                false
            );

            // OR mode, only OR labels: at least one must be present.
            // Regression guard for the bug where OR-mode matched every object.
            yield return Case(
                "Or_one_present",
                LabelCombinationType.Or,
                None,
                new[] { "a", "b" },
                new[] { "a" },
                true
            );
            yield return Case(
                "Or_none_present",
                LabelCombinationType.Or,
                None,
                new[] { "a", "b" },
                new[] { "z" },
                false
            );
            yield return Case(
                "Or_empty_labels",
                LabelCombinationType.Or,
                None,
                new[] { "a", "b" },
                None,
                false
            );

            // OR mode, only AND labels: object must have all AND labels.
            yield return Case(
                "Or_mode_and_all",
                LabelCombinationType.Or,
                new[] { "a", "b" },
                None,
                new[] { "a", "b" },
                true
            );
            yield return Case(
                "Or_mode_and_partial",
                LabelCombinationType.Or,
                new[] { "a", "b" },
                None,
                new[] { "a" },
                false
            );

            // OR mode, both clauses: match iff (all AND) OR (any OR).
            yield return Case(
                "Or_both_via_and",
                LabelCombinationType.Or,
                new[] { "a", "b" },
                new[] { "x", "y" },
                new[] { "a", "b" },
                true
            );
            yield return Case(
                "Or_both_via_or",
                LabelCombinationType.Or,
                new[] { "a", "b" },
                new[] { "x", "y" },
                new[] { "x" },
                true
            );
            yield return Case(
                "Or_both_neither",
                LabelCombinationType.Or,
                new[] { "a", "b" },
                new[] { "x", "y" },
                new[] { "a" },
                false
            );

            // The obsolete 'None' combination is treated as AND, matching the None->And migration in
            // TypeLabelFilterConfig and the enum's real default. Locks that intended behavior.
#pragma warning disable CS0618 // Intentionally exercising the obsolete value.
            yield return Case(
                "None_is_and_all_present",
                LabelCombinationType.None,
                new[] { "a", "b" },
                None,
                new[] { "a", "b" },
                true
            );
            yield return Case(
                "None_is_and_missing_one",
                LabelCombinationType.None,
                new[] { "a", "b" },
                None,
                new[] { "a" },
                false
            );
#pragma warning restore CS0618
        }

        [TestCaseSource(nameof(Cases))]
        public bool Matches_RespectsCombinationRules(
            string[] objectLabels,
            TypeLabelFilterConfig config
        ) => LabelFilterEvaluator.Matches(objectLabels, config);
    }
}
