namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using WallstopStudios.DataVisualizer.Editor.Unity;
    using CollisionA = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.First.Data;
    using CollisionB = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.Second.Data;

    public sealed class SelectionPersistenceTests
    {
        [Test]
        public void Should_FindNamespaceGroup_When_TypeElementIsNestedInNamespace()
        {
            VisualElement namespaceList = new() { name = "namespace-list" };
            VisualElement namespaceGroup = new() { name = "namespace-group" };
            namespaceGroup.AddToClassList(StyleConstants.NamespaceItemClass);
            VisualElement typeContainer = new() { name = "types-container" };
            VisualElement typeItem = new() { name = "type-item" };
            typeItem.AddToClassList(StyleConstants.TypeItemClass);

            namespaceList.Add(namespaceGroup);
            namespaceGroup.Add(typeContainer);
            typeContainer.Add(typeItem);

            Assert.AreSame(
                namespaceGroup,
                DataVisualizer.FindAncestorNamespaceGroup(typeItem, namespaceList)
            );
        }

        [Test]
        public void Should_NotReturnObjectRow_When_FindingNamespaceGroup()
        {
            VisualElement namespaceList = new() { name = "namespace-list" };
            VisualElement namespaceGroup = new() { name = "namespace-group" };
            namespaceGroup.AddToClassList(StyleConstants.NamespaceItemClass);
            VisualElement objectRow = new() { name = "object-row" };
            objectRow.AddToClassList("object-item");
            VisualElement typeItem = new() { name = "type-item" };
            typeItem.AddToClassList(StyleConstants.TypeItemClass);

            namespaceList.Add(namespaceGroup);
            namespaceGroup.Add(objectRow);
            objectRow.Add(typeItem);

            Assert.AreSame(
                namespaceGroup,
                DataVisualizer.FindAncestorNamespaceGroup(typeItem, namespaceList)
            );
        }

        [Test]
        public void Should_ReturnFirstNamespaceKey_When_SavedNamespaceIsUnavailable()
        {
            Dictionary<string, int> namespaceOrder = new()
            {
                ["Gamma"] = 2,
                ["Alpha"] = 0,
                ["Beta"] = 1,
            };

            Assert.AreEqual("Alpha", DataVisualizer.FindFirstNamespaceKeyByOrder(namespaceOrder));
        }

        [Test]
        public void Should_UseOrdinalNamespaceKeyTieBreaker_When_NamespaceOrderCollides()
        {
            Dictionary<string, int> namespaceOrder = new() { ["Beta"] = 0, ["Alpha"] = 0 };

            Assert.AreEqual("Alpha", DataVisualizer.FindFirstNamespaceKeyByOrder(namespaceOrder));
        }

        [Test]
        public void Should_ResolveSavedTypeByFullName_When_SavedNamespaceIsStale()
        {
            Type firstType = typeof(CollisionA.OrderCollisionData);
            Type secondType = typeof(CollisionB.OrderCollisionData);
            Dictionary<string, List<Type>> typesByNamespace = new()
            {
                ["First"] = new List<Type> { firstType },
                ["Second"] = new List<Type> { secondType },
            };
            Dictionary<string, int> namespaceOrder = new() { ["First"] = 0, ["Second"] = 1 };

            Type resolvedType = DataVisualizer.ResolveSelectedTypeByFullName(
                typesByNamespace,
                namespaceOrder,
                "Missing",
                secondType.FullName
            );

            Assert.AreSame(secondType, resolvedType);
            Assert.AreEqual(firstType.Name, secondType.Name);
        }

        [Test]
        public void Should_RemoveStoredObjectSelection_When_GuidIsCleared()
        {
            DataVisualizerUserState userState = new();
            const string typeFullName = "Example.Type";
            const string objectGuid = "0123456789abcdef0123456789abcdef";

            Assert.IsTrue(userState.SetLastObjectForType(typeFullName, objectGuid));
            Assert.AreEqual(objectGuid, userState.GetLastObjectForType(typeFullName));

            Assert.IsFalse(
                userState.SetLastObjectForType(typeFullName, objectGuid),
                "duplicate saved object selection should be unchanged"
            );

            Assert.IsTrue(userState.SetLastObjectForType(typeFullName, null));
            Assert.IsNull(userState.GetLastObjectForType(typeFullName));
            Assert.IsFalse(
                userState.SetLastObjectForType(typeFullName, null),
                "clearing an absent saved object selection should be unchanged"
            );
        }

        [Test]
        public void Should_ResolveSavedObjectGuidOnlyForExactType()
        {
            string folderName =
                "DataVisualizerSelectionPersistenceTests_" + Guid.NewGuid().ToString("N");
            string folderPath = "Assets/" + folderName;
            string assetPath = folderPath + "/Selection.asset";

            AssetDatabase.CreateFolder("Assets", folderName);
            SelectionPersistenceGuidData asset =
                ScriptableObject.CreateInstance<SelectionPersistenceGuidData>();

            try
            {
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);

                Assert.IsTrue(
                    DataVisualizer.TryResolveAssetGuidForType(
                        assetGuid,
                        typeof(SelectionPersistenceGuidData),
                        out string resolvedPath
                    )
                );
                Assert.AreEqual(assetPath, resolvedPath);
                Assert.IsFalse(
                    DataVisualizer.TryResolveAssetGuidForType(
                        assetGuid,
                        typeof(OtherSelectionPersistenceGuidData),
                        out _
                    )
                );
            }
            finally
            {
                AssetDatabase.DeleteAsset(folderPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Should_IncludeDirectlyResolvedSavedGuid_When_FindAssetsMissesIt()
        {
            string folderName =
                "DataVisualizerSelectionPersistenceTests_" + Guid.NewGuid().ToString("N");
            string folderPath = "Assets/" + folderName;
            string assetPath = folderPath + "/Selection.asset";

            AssetDatabase.CreateFolder("Assets", folderName);
            SelectionPersistenceGuidData asset =
                ScriptableObject.CreateInstance<SelectionPersistenceGuidData>();

            try
            {
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                string[] includedGuids = DataVisualizer.IncludeResolvedSavedObjectGuid(
                    typeof(SelectionPersistenceGuidData),
                    assetGuid,
                    Array.Empty<string>(),
                    out string normalizedSavedObjectGuid
                );

                CollectionAssert.AreEqual(new[] { assetGuid }, includedGuids);
                Assert.AreEqual(assetGuid, normalizedSavedObjectGuid);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folderPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Should_NormalizeSavedGuid_When_FindAssetsReturnsDifferentCasing()
        {
            string folderName =
                "DataVisualizerSelectionPersistenceTests_" + Guid.NewGuid().ToString("N");
            string folderPath = "Assets/" + folderName;
            string assetPath = folderPath + "/Selection.asset";

            AssetDatabase.CreateFolder("Assets", folderName);
            SelectionPersistenceGuidData asset =
                ScriptableObject.CreateInstance<SelectionPersistenceGuidData>();

            try
            {
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                string upperGuid = assetGuid.ToUpperInvariant();
                string[] includedGuids = DataVisualizer.IncludeResolvedSavedObjectGuid(
                    typeof(SelectionPersistenceGuidData),
                    upperGuid,
                    new[] { assetGuid },
                    out string normalizedSavedObjectGuid
                );

                CollectionAssert.AreEqual(new[] { assetGuid }, includedGuids);
                Assert.AreEqual(assetGuid, normalizedSavedObjectGuid);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folderPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Should_TreatDeletedAssetPathAsRelevant_When_PathIsAsset()
        {
            Assert.IsTrue(
                DataVisualizerAssetProcessor.IsDeletedAssetPathRelevant("Assets/Deleted.asset")
            );
            Assert.IsFalse(
                DataVisualizerAssetProcessor.IsDeletedAssetPathRelevant("Assets/Deleted.prefab")
            );
        }
    }

    public sealed class SelectionPersistenceGuidData : ScriptableObject { }

    public sealed class OtherSelectionPersistenceGuidData : ScriptableObject { }
}
