namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class AssetGuidTypeIndexDeferralTests
    {
        private static void EnsureFolderExists(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        private static string CreateFixtureFolder()
        {
            string folder =
                "Assets/DataVisualizerIndexDeferralTests_" + Guid.NewGuid().ToString("N");
            EnsureFolderExists(folder);
            return folder;
        }

        private static string CreateAsset(ScriptableObject asset, string assetPath)
        {
            AssetDatabase.CreateAsset(asset, assetPath);
            return AssetDatabase.AssetPathToGUID(assetPath);
        }

        [Test]
        public void ShouldDeferSnapshotAndClassificationWhileAssetDatabaseIsBusy()
        {
            string folder = CreateFixtureFolder();
            try
            {
                string guid = CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    folder + "/First.asset"
                );
                AssetDatabase.SaveAssets();

                AssetGuidTypeIndex index = new();
                bool isBusy = true;
                index.AssetDatabaseBusyOverride = () => isBusy;
                index.Rebuild();

                Assert.IsFalse(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsFalse(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsFalse(index.IsComplete);
                Assert.IsEmpty(index.GetKnownGuids(typeof(TestDataObject)));

                int completedCount = 0;
                index.IndexCompleted += () => completedCount++;
                isBusy = false;

                Assert.IsFalse(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsTrue(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsTrue(index.IsComplete);
                CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), guid);
                Assert.AreEqual(1, completedCount);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void ShouldKeepPostprocessorPathSynchronousWhileAssetDatabaseIsBusy()
        {
            string folder = CreateFixtureFolder();
            try
            {
                string importedGuid = CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    folder + "/ImportedWhileBusy.asset"
                );
                AssetDatabase.SaveAssets();

                AssetGuidTypeIndex index = new() { AssetDatabaseBusyOverride = () => true };
                index.Rebuild();
                Assert.IsFalse(index.ProcessPendingSlice(double.PositiveInfinity));

                /*
                    The AssetPostprocessor runs inside the refresh and indexes the paths Unity
                    just imported, so its reconciliation must stay synchronous: only the
                    background pump defers while the database is busy.
                */
                Assert.IsTrue(
                    index.ApplyAssetChanges(
                        new[] { folder + "/ImportedWhileBusy.asset" },
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>()
                    )
                );
                CollectionAssert.Contains(
                    index.GetKnownGuids(typeof(TestDataObject)),
                    importedGuid
                );
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void ShouldClassifyImmediatelyWhenDefaultBusyPredicateIsIdle()
        {
            string folder = CreateFixtureFolder();
            try
            {
                string guid = CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    folder + "/First.asset"
                );
                AssetDatabase.SaveAssets();

                AssetGuidTypeIndex index = new();
                index.Rebuild();

                Assert.IsFalse(EditorApplication.isCompiling);
                Assert.IsFalse(EditorApplication.isUpdating);
                Assert.IsFalse(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsTrue(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsTrue(index.IsComplete);
                CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), guid);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }
    }
}
