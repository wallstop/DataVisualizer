namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using CollisionA = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.First.Data;
    using CollisionB = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.Second.Data;

    public sealed class AssetGuidDiscoveryTests
    {
        private static string _indexRootFolder;
        private static string _neverRegisteredGuid;
        private static string _firstCollisionGuid;
        private static string _secondCollisionGuid;

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

        private static string CreateAsset(ScriptableObject asset, string assetPath)
        {
            AssetDatabase.CreateAsset(asset, assetPath);
            return AssetDatabase.AssetPathToGUID(assetPath);
        }

        [OneTimeSetUp]
        public void BuildProjectAssetTypeIndex()
        {
            _indexRootFolder =
                "Assets/DataVisualizerAssetGuidTypeIndexTests_" + Guid.NewGuid().ToString("N");
            EnsureFolderExists(_indexRootFolder);
            _neverRegisteredGuid = CreateAsset(
                ScriptableObject.CreateInstance<EditorOnlyCreationData>(),
                _indexRootFolder + "/NeverRegistered.asset"
            );
            _firstCollisionGuid = CreateAsset(
                ScriptableObject.CreateInstance<CollisionA.OrderCollisionData>(),
                _indexRootFolder + "/FirstCollision.asset"
            );
            _secondCollisionGuid = CreateAsset(
                ScriptableObject.CreateInstance<CollisionB.OrderCollisionData>(),
                _indexRootFolder + "/SecondCollision.asset"
            );
            AssetDatabase.SaveAssets();

            AssetGuidTypeIndex.Shared.Rebuild();
            bool pathSnapshotCompleted = AssetGuidTypeIndex.Shared.ProcessPendingSlice(
                double.PositiveInfinity
            );
            bool classificationCompleted = AssetGuidTypeIndex.Shared.ProcessPendingSlice(
                double.PositiveInfinity
            );
            Assert.IsFalse(pathSnapshotCompleted);
            Assert.IsTrue(classificationCompleted);
            Assert.IsTrue(AssetGuidTypeIndex.Shared.IsComplete);
        }

        [OneTimeTearDown]
        public void ClearProjectAssetTypeIndex()
        {
            AssetGuidTypeIndex.Shared.Cancel();
            AssetDatabase.DeleteAsset(_indexRootFolder);
            AssetDatabase.Refresh();
        }

        [Test]
        public void ShouldIncludeNeverRegisteredAssetWhenProjectIndexRebuildCompletes()
        {
            string[] typeFilterGuids = AssetDatabase.FindAssets(
                $"t:{nameof(EditorOnlyCreationData)}"
            );

            string[] discoveredGuids = AssetGuidDiscovery.MergeCandidates(
                typeof(EditorOnlyCreationData),
                typeFilterGuids,
                Array.Empty<string>(),
                null,
                out _
            );

            CollectionAssert.DoesNotContain(typeFilterGuids, _neverRegisteredGuid);
            CollectionAssert.Contains(discoveredGuids, _neverRegisteredGuid);
        }

        [Test]
        public void ShouldKeepIndexedGuidsSeparateWhenShortTypeNamesCollide()
        {
            string[] firstGuids = AssetGuidTypeIndex.Shared.GetKnownGuids(
                typeof(CollisionA.OrderCollisionData)
            );
            string[] secondGuids = AssetGuidTypeIndex.Shared.GetKnownGuids(
                typeof(CollisionB.OrderCollisionData)
            );

            CollectionAssert.Contains(firstGuids, _firstCollisionGuid);
            CollectionAssert.DoesNotContain(firstGuids, _secondCollisionGuid);
            CollectionAssert.Contains(secondGuids, _secondCollisionGuid);
            CollectionAssert.DoesNotContain(secondGuids, _firstCollisionGuid);
        }

        [Test]
        public void ShouldApplyEveryAssetChangeCollectionWhenIndexIsComplete()
        {
            string importedAssetPath = _indexRootFolder + "/ImportedAfterIndex.asset";
            string movedAssetPath = _indexRootFolder + "/MovedAfterIndex.asset";
            string importedAssetGuid = CreateAsset(
                ScriptableObject.CreateInstance<EditorOnlyCreationData>(),
                importedAssetPath
            );
            string movedAssetGuid = CreateAsset(
                ScriptableObject.CreateInstance<EditorOnlyCreationData>(),
                movedAssetPath
            );
            AssetDatabase.SaveAssets();

            Assert.IsTrue(
                AssetGuidTypeIndex.Shared.ApplyAssetChanges(
                    Array.Empty<string>(),
                    new[] { importedAssetPath },
                    Array.Empty<string>(),
                    new[] { movedAssetPath }
                )
            );

            Assert.IsTrue(
                AssetGuidTypeIndex.Shared.ApplyAssetChanges(
                    new[] { importedAssetPath },
                    Array.Empty<string>(),
                    new[] { movedAssetPath },
                    Array.Empty<string>()
                )
            );
            string[] indexedGuids = AssetGuidTypeIndex.Shared.GetKnownGuids(
                typeof(EditorOnlyCreationData)
            );
            CollectionAssert.Contains(indexedGuids, importedAssetGuid);
            CollectionAssert.Contains(indexedGuids, movedAssetGuid);

            AssetDatabase.DeleteAsset(importedAssetPath);
            AssetDatabase.DeleteAsset(movedAssetPath);
            indexedGuids = AssetGuidTypeIndex.Shared.GetKnownGuids(typeof(EditorOnlyCreationData));
            CollectionAssert.DoesNotContain(indexedGuids, importedAssetGuid);
            CollectionAssert.DoesNotContain(indexedGuids, movedAssetGuid);
        }

        [Test]
        public void ShouldIsolateIndexStateWhenUsingSeparateInstances()
        {
            AssetGuidTypeIndex isolatedIndex = new();

            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(isolatedIndex.Cancel);
                Assert.IsFalse(isolatedIndex.IsComplete);
                CollectionAssert.IsEmpty(
                    isolatedIndex.GetKnownGuids(typeof(EditorOnlyCreationData))
                );

                isolatedIndex.Rebuild();
                Assert.IsFalse(isolatedIndex.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsTrue(isolatedIndex.ProcessPendingSlice(double.PositiveInfinity));

                string[] isolatedGuids = AssetGuidDiscovery.MergeCandidates(
                    typeof(EditorOnlyCreationData),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    null,
                    out _,
                    isolatedIndex
                );
                CollectionAssert.Contains(isolatedGuids, _neverRegisteredGuid);
                Assert.IsTrue(AssetGuidTypeIndex.Shared.IsComplete);
            }
        }

        [Test]
        public void ShouldIncludeReferencedAssetWhenTypeFilterDoesNotReturnIt()
        {
            string rootFolder =
                "Assets/DataVisualizerAssetGuidDiscoveryTests_" + Guid.NewGuid().ToString("N");
            string typeFolder = Path.Combine(
                    rootFolder,
                    typeof(EditorOnlyCreationData).FullName.Replace('.', '/')
                )
                .Replace('\\', '/');
            string assetPath = typeFolder + "/Created.asset";

            EnsureFolderExists(typeFolder);
            EditorOnlyCreationData asset =
                ScriptableObject.CreateInstance<EditorOnlyCreationData>();

            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(rootFolder);
                    AssetDatabase.Refresh();
                });
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                string[] typeFilterGuids = AssetDatabase.FindAssets(
                    $"t:{nameof(EditorOnlyCreationData)}"
                );
                string[] discoveredGuids = AssetGuidDiscovery.MergeCandidates(
                    typeof(EditorOnlyCreationData),
                    typeFilterGuids,
                    new[] { assetGuid },
                    null,
                    out _
                );

                CollectionAssert.DoesNotContain(typeFilterGuids, assetGuid);
                CollectionAssert.Contains(discoveredGuids, assetGuid);
                Assert.AreEqual(
                    1,
                    discoveredGuids.Count(guid =>
                        string.Equals(guid, assetGuid, StringComparison.OrdinalIgnoreCase)
                    )
                );
                Assert.AreEqual(
                    typeof(EditorOnlyCreationData),
                    AssetDatabase.GetMainAssetTypeAtPath(AssetDatabase.GUIDToAssetPath(assetGuid))
                );
            }
        }

        [Test]
        public void ShouldReturnOriginalArrayWhenNoReferencedGuidNeedsAdding()
        {
            string[] discoveredGuids = { "already-discovered" };

            string[] mergedGuids = AssetGuidDiscovery.MergeCandidates(
                typeof(UnindexedAssetGuidDiscoveryData),
                discoveredGuids,
                Array.Empty<string>(),
                null,
                out string normalizedSavedObjectGuid
            );

            Assert.AreSame(discoveredGuids, mergedGuids);
            Assert.IsNull(normalizedSavedObjectGuid);
        }

        [Test]
        public void ShouldAssignNullOutputWhenGuidNormalizationFails()
        {
            Assert.IsFalse(
                AssetGuidDiscovery.TryNormalizeGuidForType(
                    typeof(EditorOnlyCreationData),
                    "missing-guid",
                    out string normalizedGuid
                )
            );
            Assert.IsNull(normalizedGuid);
        }

        [Test]
        public void ShouldRejectReferencedGuidWhenExactTypeDoesNotMatch()
        {
            string rootFolder =
                "Assets/DataVisualizerAssetGuidDiscoveryTests_" + Guid.NewGuid().ToString("N");
            string assetPath = rootFolder + "/Other.asset";

            EnsureFolderExists(rootFolder);
            OtherEditorOnlyCreationData asset =
                ScriptableObject.CreateInstance<OtherEditorOnlyCreationData>();

            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(rootFolder);
                    AssetDatabase.Refresh();
                });
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                string[] mergedGuids = AssetGuidDiscovery.MergeCandidates(
                    typeof(UnindexedAssetGuidDiscoveryData),
                    Array.Empty<string>(),
                    new[] { assetGuid },
                    null,
                    out _
                );

                Assert.IsEmpty(mergedGuids);
            }
        }

        [Test]
        public void ShouldDeduplicateResolvedGuidsWhenAddingToExistingSet()
        {
            string rootFolder =
                "Assets/DataVisualizerAssetGuidDiscoveryTests_" + Guid.NewGuid().ToString("N");
            string assetPath = rootFolder + "/Created.asset";

            EnsureFolderExists(rootFolder);
            EditorOnlyCreationData asset =
                ScriptableObject.CreateInstance<EditorOnlyCreationData>();

            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(rootFolder);
                    AssetDatabase.Refresh();
                });
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                HashSet<string> destination = new(StringComparer.OrdinalIgnoreCase);
                int addedCount = AssetGuidDiscovery.AddResolvedGuids(
                    typeof(EditorOnlyCreationData),
                    new[] { assetGuid, assetGuid.ToUpperInvariant() },
                    destination
                );

                CollectionAssert.AreEqual(new[] { assetGuid }, destination);
                Assert.AreEqual(1, addedCount);
                Assert.AreEqual(
                    0,
                    AssetGuidDiscovery.AddResolvedGuids(
                        typeof(EditorOnlyCreationData),
                        new[] { assetGuid },
                        destination
                    )
                );
            }
        }

        [Test]
        public void ShouldRejectDerivedAssetWhenBaseTypeIsRequested()
        {
            string rootFolder =
                "Assets/DataVisualizerAssetGuidDiscoveryTests_" + Guid.NewGuid().ToString("N");
            string assetPath = rootFolder + "/Derived.asset";

            EnsureFolderExists(rootFolder);
            DerivedEditorOnlyCreationData asset =
                ScriptableObject.CreateInstance<DerivedEditorOnlyCreationData>();

            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(rootFolder);
                    AssetDatabase.Refresh();
                });
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                Assert.IsFalse(
                    AssetGuidDiscovery.TryResolveAssetGuidForType(
                        assetGuid,
                        typeof(EditorOnlyCreationData),
                        out string resolvedPath
                    )
                );
                Assert.IsNull(resolvedPath);
            }
        }

        [Test]
        public void ShouldRejectSubassetWhenRequestedTypeIsNotMainAssetType()
        {
            string rootFolder =
                "Assets/DataVisualizerAssetGuidDiscoveryTests_" + Guid.NewGuid().ToString("N");
            string assetPath = rootFolder + "/Main.asset";

            EnsureFolderExists(rootFolder);
            EditorOnlyCreationData mainAsset =
                ScriptableObject.CreateInstance<EditorOnlyCreationData>();
            EditorOnlyCreationSubasset subasset =
                ScriptableObject.CreateInstance<EditorOnlyCreationSubasset>();

            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(rootFolder);
                    AssetDatabase.Refresh();
                });
                AssetDatabase.CreateAsset(mainAsset, assetPath);
                AssetDatabase.AddObjectToAsset(subasset, mainAsset);
                AssetDatabase.SaveAssets();

                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                Assert.IsFalse(
                    AssetGuidDiscovery.TryResolveAssetGuidForType(
                        assetGuid,
                        typeof(EditorOnlyCreationSubasset),
                        out string resolvedPath
                    )
                );
                Assert.IsNull(resolvedPath);
            }
        }
    }
}
