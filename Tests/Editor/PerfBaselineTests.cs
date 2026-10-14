namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

    /*
        Pure-part coverage for the performance-baseline driver (T01 slice, #114): the
        output-directory resolution contract, median statistics, and JSON escaping.
        The staged-dataset measurements themselves run on a Unity host through the
        driver entry point and land in the JSON report; spinning a full fixture
        dataset inside an EditMode test would measure the host instead of the
        window, so those paths stay host-driven by design.
    */
    internal sealed class PerfBaselineTests
    {
        private static IEnumerable<TestCaseData> OutputDirectoryCases()
        {
            yield return new TestCaseData(new[] { "-perfBaselineOutputDir", "Assets/Out" }, null, "Assets/Out").SetName("ShouldUseArgumentWhenPresent");
            yield return new TestCaseData(Array.Empty<string>(), "Env/Dir", "Env/Dir").SetName("ShouldUseEnvironmentWhenNoArgument");
            yield return new TestCaseData(new[] { "-perfBaselineOutputDir" }, "Env/Dir", "Env/Dir").SetName("ShouldUseEnvironmentWhenArgumentValueMissing");
            yield return new TestCaseData(new[] { "-perfBaselineOutputDir", "  " }, "Env/Dir", "Env/Dir").SetName("ShouldUseEnvironmentWhenArgumentValueBlank");
            yield return new TestCaseData(new[] { "-otherFlag", "x" }, null, "Temp/PerfBaseline").SetName("ShouldUseDefaultWhenNothingSet");
            yield return new TestCaseData(new[] { "-perfBaselineOutputDir", "First/Dir", "-perfBaselineOutputDir", "Second/Dir" }, null, "First/Dir").SetName(
                "ShouldUseFirstArgumentWhenRepeated"
            );
        }

        private static IEnumerable<TestCaseData> JsonEscapeCases()
        {
            yield return new TestCaseData("plain", "plain").SetName("ShouldKeepPlainCharacters");
            yield return new TestCaseData("quote\"inside", "quote\\\"inside").SetName("ShouldEscapeQuotes");
            yield return new TestCaseData("back\\slash", "back\\\\slash").SetName("ShouldEscapeBackslashes");
            yield return new TestCaseData("new\nline", "new\\nline").SetName("ShouldEscapeNewlines");
            yield return new TestCaseData("tab\there", "tab\\there").SetName("ShouldEscapeTabs");
            yield return new TestCaseData("control\u0001char", "control\\u0001char").SetName("ShouldEscapeControlCharacters");
        }

        [Test]
        [TestCaseSource(nameof(OutputDirectoryCases))]
        public void ShouldResolveOutputDirectoryFromArgumentsAndEnvironment(string[] arguments, string environmentValue, string expected)
        {
            string resolved = PerfBaseline.ResolveOutputDirectory(arguments, environmentValue);
            Assert.AreEqual(expected, resolved);
        }

        [Test]
        public void ShouldReturnZeroMedianWhenNoSamples()
        {
            Assert.AreEqual(0d, PerfBaseline.Median(new List<double>()));
            Assert.AreEqual(0d, PerfBaseline.Median(null));
        }

        [Test]
        public void ShouldReturnMiddleSampleWhenOddCount()
        {
            Assert.AreEqual(3d, PerfBaseline.Median(new List<double> { 5, 1, 3, 2, 4 }));
        }

        [Test]
        public void ShouldReturnMeanOfMiddleSamplesWhenEvenCount()
        {
            Assert.AreEqual(2.5d, PerfBaseline.Median(new List<double> { 4, 1, 3, 2 }));
        }

        [Test]
        [TestCaseSource(nameof(JsonEscapeCases))]
        public void ShouldEscapeJsonStrings(string input, string expected)
        {
            Assert.AreEqual(expected, PerfBaseline.JsonEscape(input));
        }

        [TestCase(0, "Player Save")]
        [TestCase(7, "Damage Table")]
        [TestCase(8, "Player Save 000001")]
        [TestCase(9, "World Settings 000001")]
        [TestCase(15, "Damage Table 000001")]
        [TestCase(16, "Player Save 000002")]
        [TestCase(9999, "Damage Table 001249")]
        public void ShouldNameFixturesDeterministically(int index, string expected)
        {
            Assert.AreEqual(expected, PerfBaseline.FixtureName(index));
        }

        [Test]
        public void ShouldCarryTheSchemaVersionContract()
        {
            Assert.AreEqual("1", PerfBaseline.SchemaVersion);
        }
    }
}
