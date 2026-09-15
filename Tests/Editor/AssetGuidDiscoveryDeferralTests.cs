namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    /*
        Pins the busy-window deferral of GUID normalization recorded as future scope on issue
        #36: while the AssetDatabase is busy, referenced and saved order GUIDs stay raw
        unverified candidates and the saved GUID is handed back instead of null, so a transient
        resolution failure can neither drop candidates nor clear a still-valid saved selection.
        Each test pairs the busy outcome with the idle control on the same input.
    */
    public sealed class AssetGuidDiscoveryDeferralTests
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

        [Test]
        public void ShouldKeepRawSavedGuidWhenNormalizationDeferredWhileAssetDatabaseIsBusy()
        {
            using (TestCleanupScope cleanup = new())
            {
                AssetGuidTypeIndex isolatedIndex = new();
                cleanup.Defer(isolatedIndex.Cancel);

                string[] mergedGuids = AssetGuidDiscovery.MergeCandidates(
                    typeof(TestDataObject),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    "stale-saved-guid",
                    out string normalizedSavedObjectGuid,
                    isolatedIndex,
                    deferNormalization: true
                );

                CollectionAssert.AreEqual(new[] { "stale-saved-guid" }, mergedGuids);
                Assert.AreEqual(
                    "stale-saved-guid",
                    normalizedSavedObjectGuid,
                    "a deferred normalization must hand the raw GUID back so the caller keeps the saved selection"
                );

                /*
                    Idle control: the same stale GUID fails normalization, so it is neither added
                    as a candidate nor handed back.
                */
                string[] idleGuids = AssetGuidDiscovery.MergeCandidates(
                    typeof(TestDataObject),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    "stale-saved-guid",
                    out string idleNormalizedSavedObjectGuid,
                    isolatedIndex
                );

                Assert.IsEmpty(idleGuids);
                Assert.IsNull(idleNormalizedSavedObjectGuid);
            }
        }

        [Test]
        public void ShouldKeepRawReferencedGuidsWhenNormalizationDeferredWhileAssetDatabaseIsBusy()
        {
            using (TestCleanupScope cleanup = new())
            {
                AssetGuidTypeIndex isolatedIndex = new();
                cleanup.Defer(isolatedIndex.Cancel);

                string[] mergedGuids = AssetGuidDiscovery.MergeCandidates(
                    typeof(TestDataObject),
                    Array.Empty<string>(),
                    new[] { "stale-order-guid", "STALE-ORDER-GUID" },
                    null,
                    out _,
                    isolatedIndex,
                    deferNormalization: true
                );

                CollectionAssert.AreEqual(new[] { "stale-order-guid" }, mergedGuids);

                /*
                    Idle control: the same referenced GUIDs fail normalization and are dropped.
                */
                string[] idleGuids = AssetGuidDiscovery.MergeCandidates(
                    typeof(TestDataObject),
                    Array.Empty<string>(),
                    new[] { "stale-order-guid", "STALE-ORDER-GUID" },
                    null,
                    out _,
                    isolatedIndex
                );

                Assert.IsEmpty(idleGuids);
            }
        }

        [Test]
        public void ShouldHandBackValidSavedGuidUnchangedWhenNormalizationDeferred()
        {
            string folder =
                "Assets/DataVisualizerGuidDeferralTests_" + Guid.NewGuid().ToString("N");
            EnsureFolderExists(folder);
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });

                string assetPath = folder + "/First.asset";
                AssetDatabase.CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    assetPath
                );
                AssetDatabase.SaveAssets();
                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                string upperGuid = assetGuid.ToUpperInvariant();

                AssetGuidTypeIndex isolatedIndex = new();
                cleanup.Defer(isolatedIndex.Cancel);

                string[] mergedGuids = AssetGuidDiscovery.MergeCandidates(
                    typeof(TestDataObject),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    upperGuid,
                    out string normalizedSavedObjectGuid,
                    isolatedIndex,
                    deferNormalization: true
                );

                /*
                    The busy path must not canonicalize: the raw casing comes back untouched,
                    which is how the caller can tell the value is unverified.
                */
                Assert.AreEqual(upperGuid, normalizedSavedObjectGuid);
                CollectionAssert.AreEqual(new[] { upperGuid }, mergedGuids);

                /*
                    Idle control: the same upper-cased GUID normalizes to the canonical casing.
                */
                string[] idleGuids = AssetGuidDiscovery.MergeCandidates(
                    typeof(TestDataObject),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    upperGuid,
                    out string idleNormalizedSavedObjectGuid,
                    isolatedIndex
                );

                Assert.AreEqual(assetGuid, idleNormalizedSavedObjectGuid);
                CollectionAssert.AreEqual(new[] { assetGuid }, idleGuids);
            }
        }
    }
}
