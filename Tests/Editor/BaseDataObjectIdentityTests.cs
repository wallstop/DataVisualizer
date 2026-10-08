namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;

    public sealed class BaseDataObjectIdentityTests
    {
        private const string AssetPathPrefix = "Assets/DataVisualizerBaseDataObjectIdentityTest";

        private static IEnumerable<TestCaseData> AuthoredIdCases()
        {
            yield return new TestCaseData("f1c91b4f-c0d3-4f97-9937-3994689d3b6f").SetName(
                "ShouldPreserveHyphenatedAuthoredIdWhenValidated"
            );
            yield return new TestCaseData("6eb4def1eeb18784c817551d7d7537b6").SetName(
                "ShouldPreserveCanonicalShapedAuthoredIdWhenValidated"
            );
        }

        private static IEnumerable<TestCaseData> EmptyIdCases()
        {
            yield return new TestCaseData(string.Empty).SetName(
                "ShouldFillEmptySerializedIdFromMetaGuidWhenValidated"
            );
            yield return new TestCaseData(" ").SetName(
                "ShouldFillWhitespaceSerializedIdFromMetaGuidWhenValidated"
            );
        }

        /*
         * Regression coverage for issue #137. _assetGuid is persisted identity
         * that consumers key saves and databases on. Validation fills an empty
         * id from the .meta GUID and never rewrites a non-empty authored id.
         */

        [TestCaseSource(nameof(AuthoredIdCases))]
        public void ShouldPreserveAuthoredIdWhenValidated(string authoredId)
        {
            TestDataObject dataObject = ScriptableObject.CreateInstance<TestDataObject>();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(AssetPathPrefix + ".asset");

            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    if (dataObject != null && !AssetDatabase.Contains(dataObject))
                    {
                        Object.DestroyImmediate(dataObject);
                    }
                    AssetDatabase.Refresh();
                });
                AssetDatabase.CreateAsset(dataObject, assetPath);
                AssetDatabase.SaveAssets();
                string canonicalGuid = AssetDatabase.AssetPathToGUID(assetPath);
                Assert.AreNotEqual(authoredId, canonicalGuid);

                SerializedObject serializedObject = new(dataObject);
                serializedObject.FindProperty("_assetGuid").stringValue = authoredId;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();

                dataObject.InvokeValidation();
                Assert.AreEqual(authoredId, dataObject.Id);

                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                TestDataObject reloadedDataObject = AssetDatabase.LoadAssetAtPath<TestDataObject>(
                    assetPath
                );

                Assert.AreEqual(authoredId, reloadedDataObject.Id);
            }
        }

        [TestCaseSource(nameof(EmptyIdCases))]
        public void ShouldFillEmptyIdFromMetaGuidWhenValidated(string serializedId)
        {
            TestDataObject dataObject = ScriptableObject.CreateInstance<TestDataObject>();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(AssetPathPrefix + ".asset");

            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    if (dataObject != null && !AssetDatabase.Contains(dataObject))
                    {
                        Object.DestroyImmediate(dataObject);
                    }
                    AssetDatabase.Refresh();
                });
                AssetDatabase.CreateAsset(dataObject, assetPath);
                AssetDatabase.SaveAssets();
                string canonicalGuid = AssetDatabase.AssetPathToGUID(assetPath);
                Assert.That(canonicalGuid, Is.Not.Empty);

                SerializedObject serializedObject = new(dataObject);
                serializedObject.FindProperty("_assetGuid").stringValue = serializedId;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                dataObject.InvokeValidation();
                Assert.AreEqual(canonicalGuid, dataObject.Id);

                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                TestDataObject reloadedDataObject = AssetDatabase.LoadAssetAtPath<TestDataObject>(
                    assetPath
                );

                Assert.AreEqual(canonicalGuid, reloadedDataObject.Id);
            }
        }
    }
}
