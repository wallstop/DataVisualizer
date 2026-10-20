namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorAssetQueryTests
    {
        [UnityTest]
        public IEnumerator ShouldAdvanceManualIndexOnlyWhenItsOwnerRequestsWork()
        {
            using TestCleanupScope cleanup = new();
            string path = "Assets/ManualQueryIndex-" + Guid.NewGuid().ToString("N") + ".asset";
            cleanup.Defer(() => AssetDatabase.DeleteAsset(path));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), path);
            AssetGuidTypeIndex index = new(automaticUpdates: false);
            cleanup.Defer(index.Cancel);
            index.AssetDatabaseBusyOverride = () => false;
            Assert.That(index.ProcessPendingSlice(0), Is.False);
            yield return null;
            yield return null;
            Assert.That(index.IsComplete, Is.False);
            while (!index.IsComplete)
            {
                index.ProcessPendingSlice(10);
            }
            Assert.That(index.GetKnownGuids(typeof(TestDataObject)), Does.Contain(AssetDatabase.AssetPathToGUID(path)));
        }

        [Test]
        public void ShouldQueryExactTypesWithSharedTextAndLabelSemanticsWithoutWindow()
        {
            using TestCleanupScope cleanup = new();
            string folder = "Assets/EditorAssetQueryTests-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            EditorOnlyCreationData asset = ScriptableObject.CreateInstance<EditorOnlyCreationData>();
            AssetDatabase.CreateAsset(asset, folder + "/Needle.asset");
            AssetDatabase.SetLabels(asset, new[] { "included" });
            DerivedEditorOnlyCreationData derived = ScriptableObject.CreateInstance<DerivedEditorOnlyCreationData>();
            AssetDatabase.CreateAsset(derived, folder + "/Derived.asset");
            AssetDatabase.SaveAssets();
            string guid = AssetDatabase.AssetPathToGUID(folder + "/Needle.asset");
            var filter = new TypeLabelFilterConfig { andLabels = new() { "included" } };
            using EditorAssetQuery query = new(new[] { typeof(EditorOnlyCreationData) }, "absent " + guid, filter);
            filter.andLabels.Add("later-change");
            for (int i = 0; i < 10000 && !query.IsComplete; i++)
            {
                query.Advance(10);
            }
            Assert.That(query.IsComplete, Is.True);
            EditorAssetRecord[] page = query.GetPage(0, 10);
            Assert.That(page.Length, Is.EqualTo(1));
            Assert.That(page[0].Guid, Is.EqualTo(guid));
            Assert.That(page[0].TypeIdentity, Is.EqualTo(typeof(EditorOnlyCreationData).AssemblyQualifiedName));
            Assert.That(page[0].Error == null);
            Assert.That(page[0].Labels, Is.EquivalentTo(new[] { "included" }));
            Assert.That(query.GetPage(10, 10), Is.Empty);
            Assert.That(EditorUtility.IsDirty(asset), Is.False);
        }

        [Test]
        public void ShouldReportIncompleteResultsAndStopWhenCancelled()
        {
            using TestCleanupScope cleanup = new();
            Func<bool> previous = AssetGuidDiscovery.AssetDatabaseBusyOverride;
            cleanup.Defer(() => AssetGuidDiscovery.AssetDatabaseBusyOverride = previous);
            AssetGuidDiscovery.AssetDatabaseBusyOverride = () => true;
            using EditorAssetQuery query = new(new[] { typeof(TestDataObject) });
            query.Advance();
            Assert.That(query.IsWaitingForEditor, Is.True);
            Assert.That(query.IsComplete, Is.False);
            Assert.That(query.GetPage(0, 10), Is.Empty);
            query.Dispose();
            AssetGuidDiscovery.AssetDatabaseBusyOverride = () => false;
            query.Advance();
            Assert.That(query.IsCancelled, Is.True);
            Assert.That(query.ScannedCount, Is.Zero);
        }

        [Test]
        public void ShouldRejectInvalidScopePagesAndBudgets()
        {
            Assert.Throws<ArgumentException>(() => new EditorAssetQuery(Array.Empty<Type>()));
            Assert.Throws<ArgumentException>(() => new EditorAssetQuery(new[] { typeof(string) }));
            using EditorAssetQuery query = new(new[] { typeof(TestDataObject) });
            Assert.Throws<ArgumentOutOfRangeException>(() => query.GetPage(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => query.GetPage(0, 1001));
            Assert.Throws<ArgumentOutOfRangeException>(() => query.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => query.Advance(double.PositiveInfinity));
        }
    }
}
