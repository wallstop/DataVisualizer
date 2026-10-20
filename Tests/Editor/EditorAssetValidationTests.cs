namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorAssetValidationTests
    {
        private static string CreateFolder(TestCleanupScope cleanup)
        {
            string folder = "Assets/AssetValidationTests-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            return folder;
        }

        private static string CreateAsset(string path, string id)
        {
            AssetValidationTestData asset = ScriptableObject.CreateInstance<AssetValidationTestData>();
            AssetDatabase.CreateAsset(asset, path);
            using SerializedObject serialized = new(asset);
            serialized.FindProperty("_assetGuid").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return AssetDatabase.AssetPathToGUID(path);
        }

        [UnityTest]
        public IEnumerator ShouldKeepRejectedSelectionMessageAndContinueThePreviousReport()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            CreateAsset(folder + "/Previous.asset", "previous");
            AssetDatabase.SaveAssets();
            Func<bool> busy = AssetGuidDiscovery.AssetDatabaseBusyOverride;
            cleanup.Defer(() => AssetGuidDiscovery.AssetDatabaseBusyOverride = busy);
            UnityEngine.Object[] selection = Selection.objects;
            cleanup.Defer(() => Selection.objects = selection);
            AssetValidationWindow window = ScriptableObject.CreateInstance<AssetValidationWindow>();
            cleanup.Defer(() => window.Close());
            window.Show();
            window.CreateGUI();
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(folder + "/Previous.asset");
            foreach (bool mixed in new[] { false, true })
            {
                Selection.objects = new[] { asset };
                AssetGuidDiscovery.AssetDatabaseBusyOverride = () => true;
                window.ValidateSelection();
                Assert.That(window.rootVisualElement.Q<Label>("validation-status").text, Does.StartWith("Processing (partial): 0/1"));
                Selection.objects = mixed ? new[] { asset, AssetDatabase.LoadMainAssetAtPath(folder) } : Array.Empty<UnityEngine.Object>();
                window.ValidateSelection();
                Label message = window.rootVisualElement.Q<Label>("validation-selection-message");
                Assert.That(message.text, Does.Contain("No new validation started"));
                AssetGuidDiscovery.AssetDatabaseBusyOverride = () => false;
                double deadline = EditorApplication.timeSinceStartup + 3;
                while (
                    window.rootVisualElement.Q<Label>("validation-status").text.StartsWith("Processing", StringComparison.Ordinal) && EditorApplication.timeSinceStartup < deadline
                )
                {
                    yield return null;
                }
                Assert.That(window.rootVisualElement.Q<Label>("validation-status").text, Does.StartWith("Complete: 1/1"));
                Assert.That(message.text, Does.Contain("No new validation started"));
            }
        }

        [TestCase(1000)]
        [TestCase(10000)]
        public void ShouldYieldThroughLargeExplicitScopesAndCancelBetweenAssets(int count)
        {
            using TestCleanupScope cleanup = new();
            Func<bool> previous = AssetGuidDiscovery.AssetDatabaseBusyOverride;
            cleanup.Defer(() => AssetGuidDiscovery.AssetDatabaseBusyOverride = previous);
            AssetGuidDiscovery.AssetDatabaseBusyOverride = () => false;
            List<string> guids = new();
            for (int i = 1; i <= count; i++)
            {
                guids.Add(i.ToString("x32"));
            }
            using EditorAssetValidation validation = new(guids);
            validation.Advance(0);
            Assert.That(validation.ProcessedCount, Is.EqualTo(1));
            Assert.That(validation.IsComplete, Is.False);
            for (int i = 1; i < count; i++)
            {
                validation.Advance(0);
            }
            Assert.That(validation.IsComplete, Is.True);
            Assert.That(validation.Findings.Count, Is.EqualTo(count));
            using EditorAssetValidation cancelled = new(guids);
            cancelled.Advance(0);
            cancelled.Dispose();
            cancelled.Advance(1000);
            Assert.That(cancelled.ProcessedCount, Is.EqualTo(1));
            Assert.That(cancelled.IsCancelled, Is.True);
        }

        [Test]
        public void ShouldReportEachDuplicateAuthoredIdWithoutRewritingAssets()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            string first = CreateAsset(folder + "/First.asset", "authored");
            string second = CreateAsset(folder + "/Second.asset", "authored");
            string third = CreateAsset(folder + "/Third.asset", "AUTHORED");
            AssetDatabase.SaveAssets();
            byte[] before = File.ReadAllBytes(folder + "/First.asset");
            using EditorAssetValidation validation = new(new[] { first, second, third, first.ToUpperInvariant() });
            validation.Advance(0);
            Assert.That(validation.IsComplete, Is.False);
            Assert.That(validation.ProcessedCount, Is.EqualTo(1));
            while (!validation.IsComplete)
            {
                validation.Advance(0);
            }
            Assert.That(validation.TotalCount, Is.EqualTo(3));
            Assert.That(validation.Findings.Select(f => f.Code), Is.EquivalentTo(new[] { "duplicate_authored_id", "duplicate_authored_id" }));
            Assert.That(validation.Findings.Select(f => f.Guid), Is.EquivalentTo(new[] { first, second }));
            Assert.That(File.ReadAllBytes(folder + "/First.asset"), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(AssetDatabase.LoadMainAssetAtPath(folder + "/First.asset")), Is.False);
        }

        [Test]
        public void ShouldDistinguishMissingReferenceFromIntentionalNullAndOrdinaryAssets()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            string missing = CreateAsset(folder + "/Missing.asset", "first");
            string intentional = CreateAsset(folder + "/Null.asset", "second");
            string target = CreateAsset(folder + "/Target.asset", "target");
            AssetValidationTestData asset = AssetDatabase.LoadAssetAtPath<AssetValidationTestData>(folder + "/Missing.asset");
            asset.reference = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(target));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(folder + "/Target.asset");
            AssetDatabase.ImportAsset(folder + "/Missing.asset", ImportAssetOptions.ForceUpdate);
            EditorOnlyCreationData ordinary = ScriptableObject.CreateInstance<EditorOnlyCreationData>();
            AssetDatabase.CreateAsset(ordinary, folder + "/Ordinary.asset");
            string ordinaryGuid = AssetDatabase.AssetPathToGUID(folder + "/Ordinary.asset");
            using EditorAssetValidation validation = new(new[] { missing, intentional, ordinaryGuid });
            validation.Advance(1000);
            Assert.That(validation.IsComplete, Is.True);
            Assert.That(validation.Findings.Count, Is.EqualTo(1));
            Assert.That(validation.Findings[0].Code, Is.EqualTo("missing_reference"));
            Assert.That(validation.Findings[0].Guid, Is.EqualTo(missing));
            Assert.That(validation.Findings[0].PropertyPath, Is.EqualTo(nameof(AssetValidationTestData.reference)));
        }

        [Test]
        public void ShouldPreservePartialFindingsAfterCancellationAndReportDeletedAssets()
        {
            using EditorAssetValidation validation = new(new[] { "0123456789abcdef0123456789abcdef", "abcdef0123456789abcdef0123456789" });
            validation.Advance(0);
            validation.Dispose();
            validation.Dispose();
            validation.Advance();
            Assert.That(validation.ProcessedCount, Is.EqualTo(1));
            Assert.That(validation.IsCancelled, Is.True);
            Assert.That(validation.IsComplete, Is.False);
            Assert.That(validation.Findings[0].Code, Is.EqualTo("asset_unavailable"));
            Assert.Throws<ArgumentException>(() => new EditorAssetValidation(Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => new EditorAssetValidation(new[] { "not-a-guid" }));
        }

        [Test]
        public void ShouldFinishCyclicManagedReferencesAndUseTheSameReportInTheWindow()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            string guid = CreateAsset(folder + "/Cycle.asset", "cycle");
            AssetValidationTestData asset = AssetDatabase.LoadAssetAtPath<AssetValidationTestData>(folder + "/Cycle.asset");
            asset.node = new AssetValidationTestNode();
            asset.node.next = asset.node;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            using EditorAssetValidation validation = new(new[] { guid });
            validation.Advance(1000);
            Assert.That(validation.IsComplete, Is.True);
            Assert.That(validation.Findings, Is.Empty);
            UnityEngine.Object[] selection = Selection.objects;
            cleanup.Defer(() => Selection.objects = selection);
            AssetValidationWindow window = ScriptableObject.CreateInstance<AssetValidationWindow>();
            cleanup.Defer(() => window.Close());
            window.Show();
            window.CreateGUI();
            Selection.objects = new UnityEngine.Object[] { asset };
            window.ValidateSelection();
            Assert.That(window.rootVisualElement.Q<Label>("validation-status").text, Is.EqualTo("Complete: 1/1 assets, 0 findings."));
        }
    }
}
