namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
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

        [UnityTest]
        public IEnumerator ShouldKeepLegacyReadinessSeparateFromPendingSharedIndexWork()
        {
            string output = "Temp/PerfPendingIndex-" + Guid.NewGuid().ToString("N");
            var index = WallstopStudios.DataVisualizer.Editor.Utilities.AssetGuidTypeIndex.Shared;
            Func<bool> previous = index.AssetDatabaseBusyOverride;
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            cleanup.Defer(() => index.AssetDatabaseBusyOverride = previous);
            cleanup.Defer(PerfBaseline.Cancel);
            index.AssetDatabaseBusyOverride = () => true;
            PerfBaseline.RunBaseline(output, new[] { 2 }, timeoutMinutes: 5);
            for (int tick = 0; tick < 300; tick++)
            {
                yield return null;
            }
            Assert.That(!index.IsComplete);
            PerfBaseline.Cancel();
            string json = System.IO.File.ReadAllText(output + "/perf-baseline.json");
            InteractionReport report = JsonUtility.FromJson<InteractionErrorReport>(json).partialResults ?? JsonUtility.FromJson<InteractionReport>(json);
            bool observedLegacyReady = false;
            foreach (PerfReadinessSample sample in report.readinessSamples)
            {
                observedLegacyReady = observedLegacyReady || 0 <= sample.legacyIndexReadyMilliseconds;
                Assert.That(sample.complete, Is.False, "Shared classification is still blocked.");
                Assert.That(sample.openToIndexMilliseconds, Is.EqualTo(-1));
            }
            Assert.That(observedLegacyReady, Is.True);
            Assert.That(PerfBaseline.Status(), Is.EqualTo("Idle"));
        }

        [UnityTest]
        public IEnumerator ShouldRecordInteractionReadinessAndRestoreNativeOwners()
        {
            string output = "Temp/PerfInteractions-" + Guid.NewGuid().ToString("N");
            int hosts = EditorSurfaceCapture.CountNativeWindowHosts();
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            cleanup.Defer(PerfBaseline.Cancel);
            PerfBaseline.RunBaseline(output, new[] { 4 }, timeoutMinutes: 5, fixtureTypes: new[] { typeof(OtherEditorOnlyCreationData), typeof(PlayModeDataObject) });
            for (int tick = 0; tick < 6000 && PerfBaseline.Status() != "Idle"; tick++)
            {
                yield return null;
            }
            Assert.That(PerfBaseline.Status(), Is.EqualTo("Idle"));
            InteractionReport report = JsonUtility.FromJson<InteractionReport>(System.IO.File.ReadAllText(output + "/perf-baseline.json"));
            Assert.That(report.interactionSamples.Length, Is.EqualTo(105));
            Dictionary<string, int> measured = new(StringComparer.Ordinal);
            foreach (PerfInteractionSample sample in report.interactionSamples)
            {
                Assert.That(sample.complete, Is.True);
                Assert.That(0 <= sample.firstRootPaintMilliseconds);
                Assert.That(sample.firstRootPaintMilliseconds <= sample.observedReadyMilliseconds);
                Assert.That(sample.actionMilliseconds <= sample.observedReadyMilliseconds);
                Assert.That(2 <= sample.loadedRows);
                if (sample.mode == "warm")
                {
                    measured.TryGetValue(sample.kind, out int count);
                    measured[sample.kind] = count + 1;
                }
            }
            foreach (string kind in new[] { "label-filter", "object-scroll", "type-switch" })
            {
                Assert.That(measured[kind], Is.EqualTo(30));
            }
            Assert.That(EditorSurfaceCapture.CountNativeWindowHosts(), Is.EqualTo(hosts));
            Assert.That(System.IO.Directory.GetFiles(output, "window-state-*.json"), Is.Empty);
        }

        [UnityTest]
        public IEnumerator ShouldKeepIncompleteInteractionWhenCancelledBeforeReadiness()
        {
            string output = "Temp/PerfInteractionCancel-" + Guid.NewGuid().ToString("N");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            cleanup.Defer(PerfBaseline.Cancel);
            PerfBaseline.RunBaseline(output, new[] { 2 }, timeoutMinutes: 5);
            for (int tick = 0; tick < 3000 && !PerfBaseline.Status().StartsWith("WaitingLabelFilter:", StringComparison.Ordinal); tick++)
            {
                yield return null;
            }
            Assert.That(PerfBaseline.Status(), Does.StartWith("WaitingLabelFilter:"));
            PerfBaseline.Cancel();
            InteractionErrorReport report = JsonUtility.FromJson<InteractionErrorReport>(System.IO.File.ReadAllText(output + "/perf-baseline.json"));
            Assert.That(report.phase, Is.EqualTo("WaitingLabelFilter"));
            Assert.That(report.partialResults.interactionSamples.Length, Is.EqualTo(1));
            Assert.That(report.partialResults.interactionSamples[0].complete, Is.False);
            Assert.That(report.partialResults.interactionSamples[0].observedReadyMilliseconds, Is.EqualTo(-1));
            Assert.That(PerfBaseline.Status(), Is.EqualTo("Idle"));
            Assert.That(System.IO.Directory.GetFiles(output, "window-state-*.json"), Is.Empty);
        }

        [UnityTest]
        public IEnumerator ShouldRestoreOwnerWhenFinalReportCannotBeWritten()
        {
            string output = "Temp/PerfPublicationFailure-" + Guid.NewGuid().ToString("N");
            int hosts = EditorSurfaceCapture.CountNativeWindowHosts();
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            cleanup.Defer(PerfBaseline.Cancel);
            PerfBaseline.RunBaseline(output, new[] { 2 }, timeoutMinutes: 5);
            for (int tick = 0; tick < 600 && !PerfBaseline.Status().StartsWith("InteractionSamples:", StringComparison.Ordinal); tick++)
            {
                yield return null;
            }
            Assert.That(PerfBaseline.Status(), Does.StartWith("InteractionSamples:"));
            System.IO.Directory.CreateDirectory(output + "/perf-baseline.json");
            LogAssert.Expect(LogType.Error, new Regex(@"\[PerfBaseline\] FAILED: phase Finished threw: .*"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[PerfBaseline\] the error report could not be written: .*"));
            for (int tick = 0; tick < 3000 && PerfBaseline.Status() != "Idle"; tick++)
            {
                yield return null;
            }
            Assert.That(PerfBaseline.Status(), Is.EqualTo("Idle"));
            Assert.That(EditorSurfaceCapture.CountNativeWindowHosts(), Is.EqualTo(hosts));
            Assert.That(System.IO.Directory.GetFiles(output, "window-state-*.json"), Is.Empty);
            yield return null;
        }

        [TestCase(double.NaN, 10)]
        [TestCase(double.PositiveInfinity, 10)]
        [TestCase(0, 10)]
        [TestCase(-1, 10)]
        [TestCase(4, 0)]
        public void ShouldRejectInvalidStagingBudgetsBeforeStarting(double milliseconds, int items)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PerfBaseline.RunBaseline("Temp/Unused", new[] { 2 }, stagingBudgetMilliseconds: milliseconds, maximumStagingItems: items)
            );
            Assert.That(PerfBaseline.Status(), Is.EqualTo("Idle"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldStageOrdinaryAndBaseFixturesInDeterministicTypeOrder(bool ordinaryFirst)
        {
            Type[] types = ordinaryFirst
                ? new[] { typeof(OtherEditorOnlyCreationData), typeof(PlayModeDataObject) }
                : new[] { typeof(PlayModeDataObject), typeof(OtherEditorOnlyCreationData) };
            Type[] expected = (Type[])types.Clone();
            using PerfFixtureStaging staging = new(7, fixtureTypes: types);
            types[0] = typeof(string);
            while (staging.CreatedCount < staging.TargetCount)
            {
                staging.Advance(double.MaxValue, maximumItems: 3);
            }
            staging.Refresh();
            Assert.That(staging.IsComplete, Is.True);
            for (int index = 0; index < staging.TargetCount; index++)
            {
                string path = staging.Folder + "/" + PerfBaseline.FixtureName(index) + ".asset";
                ScriptableObject asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                Assert.That(asset != null);
                Assert.That(asset.GetType(), Is.EqualTo(expected[index % expected.Length]));
                Assert.That(asset.name, Is.EqualTo(PerfBaseline.FixtureName(index)));
                if (asset is BaseDataObject data)
                {
                    Assert.That(data.Id, Is.EqualTo(PerfBaseline.FixtureId(index)));
                }
            }
        }

        [TestCase(typeof(string))]
        [TestCase(typeof(ScriptableObject))]
        [TestCase(typeof(ScriptFolderProbeBehaviour))]
        [TestCase(typeof(EditorTypeCatalogTests.AbstractData))]
        [TestCase(typeof(EditorTypeCatalogTests.GenericData<>))]
        public void ShouldRejectUnsupportedFixtureTypesBeforeStarting(Type type)
        {
            Assert.Throws<ArgumentException>(() => PerfBaseline.RunBaseline("Temp/Unused", new[] { 2 }, fixtureTypes: new[] { type }));
            Assert.That(PerfBaseline.Status(), Is.EqualTo("Idle"));
        }

        [Test]
        public void ShouldRejectEmptyDuplicateAndIncompleteTypeDistributions()
        {
            Assert.Throws<ArgumentException>(() => PerfBaseline.RunBaseline("Temp/Unused", new[] { 2 }, fixtureTypes: Array.Empty<Type>()));
            Assert.Throws<ArgumentException>(() =>
                PerfBaseline.RunBaseline("Temp/Unused", new[] { 2 }, fixtureTypes: new[] { typeof(PlayModeDataObject), typeof(PlayModeDataObject) })
            );
            Assert.Throws<ArgumentException>(() =>
                PerfBaseline.RunBaseline(
                    "Temp/Unused",
                    new[] { 2 },
                    fixtureTypes: new[] { typeof(PlayModeDataObject), typeof(OtherEditorOnlyCreationData), typeof(SelectionPersistenceGuidData) }
                )
            );
            Assert.That(PerfBaseline.Status(), Is.EqualTo("Idle"));
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
            Assert.That(
                staging.TotalMilliseconds,
                Is.GreaterThanOrEqualTo(staging.CreationMilliseconds + staging.BatchImportMilliseconds + staging.InitializationMilliseconds + staging.RefreshMilliseconds)
            );
            Assert.That(staging.LongestCreateMilliseconds, Is.GreaterThan(0));
            Assert.That(staging.LongestSliceMilliseconds, Is.GreaterThanOrEqualTo(staging.LongestCreateMilliseconds));
        }

        [Test]
        public void ShouldImportBatchedFixturesWithDistinctPersistentIdentitiesBeforeYielding()
        {
            using PerfFixtureStaging staging = new(3);
            staging.Advance(double.MaxValue, 3);
            Assert.AreEqual(3, staging.CreatedCount);
            for (int index = 0; index < 3; index++)
            {
                string path = $"{staging.Folder}/{PerfBaseline.FixtureName(index)}.asset";
                PlayModeDataObject asset = AssetDatabase.LoadAssetAtPath<PlayModeDataObject>(path);
                Assert.That(asset != null);
                string authoredId = PerfBaseline.FixtureId(index);
                Assert.AreEqual(authoredId, asset.Id);
                Resources.UnloadAsset(asset);
                Assert.AreEqual(authoredId, AssetDatabase.LoadAssetAtPath<PlayModeDataObject>(path).Id);
            }
            Assert.That(0 < staging.BatchImportMilliseconds);
            Assert.That(staging.LongestBatchImportMilliseconds <= staging.LongestSliceMilliseconds);
        }

        [Test]
        public void ShouldResumeImportsWhenFixtureCreationThrowsInsideABatch()
        {
            int attempted = 0;
            using PerfFixtureStaging staging = new(
                3,
                (asset, path) =>
                {
                    if (attempted++ == 1)
                    {
                        throw new InvalidOperationException("Expected fixture failure.");
                    }
                    AssetDatabase.CreateAsset(asset, path);
                }
            );
            Assert.Throws<InvalidOperationException>(() => staging.Advance(double.MaxValue));
            Assert.AreEqual(1, staging.CreatedCount);
            string probePath = staging.Folder + "/Resume.txt";
            System.IO.File.WriteAllText(probePath, "imports resumed");
            AssetDatabase.ImportAsset(probePath, ImportAssetOptions.ForceSynchronousImport);
            Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(probePath) != null);
            Assert.That(0 < staging.BatchImportMilliseconds);
            Assert.That(staging.LongestBatchImportMilliseconds <= staging.BatchImportMilliseconds);
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

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(1441)]
        public void ShouldRejectInvalidRunBudgetsBeforeStarting(double timeoutMinutes)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerfBaseline.RunBaseline("Temp/Unused", timeoutMinutes: timeoutMinutes));
            Assert.AreEqual("Idle", PerfBaseline.Status());
        }

        [Test]
        public void ShouldReportTheExplicitBudgetWhenCancelledBeforeStaging()
        {
            string output = "Temp/PerfBaselineBudgetTests-" + Guid.NewGuid().ToString("N");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            cleanup.Defer(PerfBaseline.Cancel);
            PerfBaseline.RunBaseline(output, timeoutMinutes: 120);
            PerfBaseline.Cancel();
            string report = System.IO.File.ReadAllText(output + "/perf-baseline.json");
            StringAssert.Contains("\"timeoutMinutes\": 120", report);
        }

        [Test]
        public void ShouldLeaveHostChangesIntactWhenCancelledBeforeOpeningAWindow()
        {
            const string preference = "WallstopStudios.Editor.DataVisualizer.PreferredWindowSize";
            string output = "Temp/PerfBaselineUnownedStateTests-" + Guid.NewGuid().ToString("N");
            UnityEngine.Object[] selection = Selection.objects;
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            cleanup.Defer(DocsImageCapture.WindowPreferenceSnapshot.Capture().Restore);
            cleanup.Defer(() => Selection.objects = selection);
            PlayModeDataObject selected = ScriptableObject.CreateInstance<PlayModeDataObject>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(selected));
            cleanup.Defer(PerfBaseline.Cancel);
            PerfBaseline.RunBaseline(output, new[] { 2 });
            Selection.objects = new UnityEngine.Object[] { selected };
            EditorPrefs.SetString(preference, "changed during staging");
            PerfBaseline.Cancel();
            Assert.AreSame(selected, Selection.activeObject);
            Assert.AreEqual("changed during staging", EditorPrefs.GetString(preference));
        }

        [UnityTest]
        public IEnumerator ShouldRestoreHostStateAndNativeOwnersWhenCancelledAfterOpening()
        {
            const string preference = "WallstopStudios.Editor.DataVisualizer.PreferredWindowSize";
            string output = "Temp/PerfBaselineHostStateTests-" + Guid.NewGuid().ToString("N");
            string userFile = System.IO.Path.Combine(Application.persistentDataPath, "DataVisualizerUserState.json");
            bool existed = System.IO.File.Exists(userFile);
            string original = existed ? System.IO.File.ReadAllText(userFile) : null;
            UnityEngine.Object[] selection = Selection.objects;
            DocsImageCapture.WindowPreferenceSnapshot preferences = DocsImageCapture.WindowPreferenceSnapshot.Capture();
            bool hadPreference = EditorPrefs.HasKey(preference);
            string originalPreference = EditorPrefs.GetString(preference);
            int nativeHosts = EditorSurfaceCapture.CountNativeWindowHosts();
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            cleanup.Defer(() =>
            {
                if (existed)
                {
                    System.IO.File.WriteAllText(userFile, original);
                }
                else if (System.IO.File.Exists(userFile))
                {
                    System.IO.File.Delete(userFile);
                }
            });
            cleanup.Defer(preferences.Restore);
            cleanup.Defer(() => Selection.objects = selection);
            PlayModeDataObject selected = ScriptableObject.CreateInstance<PlayModeDataObject>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(selected));
            cleanup.Defer(PerfBaseline.Cancel);
            PerfBaseline.RunBaseline(output, new[] { 2 });
            for (
                int tick = 0;
                tick < 600
                    && (
                        !PerfBaseline.Status().StartsWith("InteractionSamples:", StringComparison.Ordinal)
                        || System.IO.Directory.GetFiles(output, "window-state-*.json").Length == 0
                    );
                tick++
            )
            {
                yield return null;
            }
            StringAssert.StartsWith("InteractionSamples:", PerfBaseline.Status());
            Assert.AreEqual(1, System.IO.Directory.GetFiles(output, "window-state-*.json").Length, "The benchmark must write only to its owned state file.");
            EditorPrefs.SetString(preference, "benchmark mutation");
            Selection.objects = new UnityEngine.Object[] { selected };
            PerfBaseline.Cancel();
            Assert.AreEqual(existed, System.IO.File.Exists(userFile));
            if (existed)
            {
                Assert.AreEqual(original, System.IO.File.ReadAllText(userFile));
            }
            Assert.AreEqual(hadPreference, EditorPrefs.HasKey(preference));
            Assert.AreEqual(originalPreference, EditorPrefs.GetString(preference));
            CollectionAssert.AreEqual(selection, Selection.objects);
            Assert.AreEqual(nativeHosts, EditorSurfaceCapture.CountNativeWindowHosts());
            Assert.AreEqual(0, System.IO.Directory.GetFiles(output, "window-state-*.json").Length);
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

        [Serializable]
        private sealed class InteractionReport
        {
            public PerfInteractionSample[] interactionSamples = Array.Empty<PerfInteractionSample>();
            public PerfReadinessSample[] readinessSamples = Array.Empty<PerfReadinessSample>();
        }

        [Serializable]
        private sealed class InteractionErrorReport
        {
            public string phase = string.Empty;
            public InteractionReport partialResults = null;
        }
    }
}
