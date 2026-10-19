namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    /*
        Coverage for the performance-baseline driver (T01 slice, #114): the
        output-directory resolution contract, median statistics, JSON escaping,
        and background asset accounting with a small temporary dataset.
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

        [Test]
        public void ShouldCountPreExistingBenchmarkNamedAssetsAsBackground()
        {
            string root = "Assets/PerfBaselineFixtures_" + Guid.NewGuid().ToString("N");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(root));
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(root));
            int before = PerfBaseline.CountScriptableObjectAssets();
            PlayModeDataObject background = ScriptableObject.CreateInstance<PlayModeDataObject>();
            AssetDatabase.CreateAsset(background, root + "/Background.asset");
            Assert.AreEqual(before + 1, PerfBaseline.CountScriptableObjectAssets());
        }

        [Test]
        public void ShouldYieldAtTheItemLimitAndRefreshOnlyCompleteFixtures()
        {
            using PerfFixtureStaging staging = new(3);
            staging.Advance(maximumItems: 1);
            Assert.AreEqual(1, staging.CreatedCount);
            Assert.That(staging.IsComplete, Is.False);
            Assert.Throws<InvalidOperationException>(() => staging.Refresh());
            staging.Advance(maximumItems: 1);
            staging.Advance(maximumItems: 1);
            Assert.AreEqual(3, staging.CreatedCount);
            Assert.That(staging.IsComplete, Is.False, "Completion requires the final refresh.");
            staging.Refresh();
            Assert.That(staging.IsComplete);
            Assert.That(staging.TotalMilliseconds, Is.GreaterThanOrEqualTo(staging.CreationMilliseconds + staging.RefreshMilliseconds));
            Assert.That(staging.LongestCreateMilliseconds, Is.GreaterThan(0));
            Assert.That(staging.LongestSliceMilliseconds, Is.GreaterThanOrEqualTo(staging.LongestCreateMilliseconds));
        }

        [Test]
        public void ShouldDisposeOnlyOwnedFixturesAndRejectFurtherStaging()
        {
            using PerfFixtureStaging first = new(2);
            using PerfFixtureStaging other = new(1);
            Assert.That(first.Folder, Is.Not.EqualTo(other.Folder));
            first.Advance(maximumItems: 1);
            other.Advance();
            string otherGuid = AssetDatabase.AssetPathToGUID(other.Folder);
            first.Dispose();
            first.Dispose();
            Assert.That(AssetDatabase.IsValidFolder(first.Folder), Is.False);
            Assert.AreEqual(otherGuid, AssetDatabase.AssetPathToGUID(other.Folder));
            Assert.Throws<ObjectDisposedException>(() => first.Advance());
        }

        [Test]
        public void ShouldLeaveAReplacementFolderUntouchedDuringCleanup()
        {
            using PerfFixtureStaging staging = new(1);
            staging.Advance();
            Assert.That(AssetDatabase.DeleteAsset(staging.Folder));
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(staging.Folder));
            string replacement = AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(staging.Folder));
            staging.Dispose();
            Assert.AreEqual(replacement, AssetDatabase.AssetPathToGUID(staging.Folder));
        }

        [Test]
        public void ShouldPreserveContentsWhenTheOwnershipMarkerChanges()
        {
            using PerfFixtureStaging staging = new(1);
            staging.Advance();
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(staging.Folder));
            System.IO.File.WriteAllText(staging.Folder + "/.perf-owner", "replacement owner");
            staging.Dispose();
            Assert.That(AssetDatabase.IsValidFolder(staging.Folder));
        }

        [TestCase(1)]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(100001)]
        public void ShouldRejectInvalidFixtureSizesBeforeStarting(int size)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerfBaseline.RunBaseline("Temp/Unused", new[] { size }));
            Assert.AreEqual("Idle", PerfBaseline.Status());
        }

        [UnityTest]
        public IEnumerator ShouldCancelWithPartialProgressAndWithoutDeletingOtherAssets()
        {
            string output = "Temp/PerfBaselineTests-" + Guid.NewGuid().ToString("N");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            cleanup.Defer(PerfBaseline.Cancel);
            PerfBaseline.RunBaseline(output);
            for (int tick = 0; tick < 300 && !PerfBaseline.Status().StartsWith("Staging: 1/", StringComparison.Ordinal); tick++)
            {
                if (PerfBaseline.Status().StartsWith("Staging:", StringComparison.Ordinal) && !PerfBaseline.Status().StartsWith("Staging: 0/", StringComparison.Ordinal))
                {
                    break;
                }

                yield return null;
            }

            StringAssert.StartsWith("Staging:", PerfBaseline.Status());
            Assert.That(PerfBaseline.Status().StartsWith("Staging: 0/", StringComparison.Ordinal), Is.False);
            PerfBaseline.Cancel();
            string report = System.IO.File.ReadAllText(output + "/perf-baseline.json");
            StringAssert.Contains("Cancelled before completion", report);
            StringAssert.Contains("\"fixturesRequested\": 100", report);
            StringAssert.DoesNotContain("\"samples\": [0]", report);
            Assert.AreEqual("Idle", PerfBaseline.Status());
        }
    }
}
