namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;

    public sealed class BaseDataObjectIdentityTests
    {
        private const string AssetPathPrefix = "Assets/DataVisualizerBaseDataObjectIdentityTest";

        [Test]
        public void ShouldRestoreCanonicalAssetGuidWhenSerializedGuidIsCorrupted()
        {
            TestDataObject dataObject = ScriptableObject.CreateInstance<TestDataObject>();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(AssetPathPrefix + ".asset");

            try
            {
                AssetDatabase.CreateAsset(dataObject, assetPath);
                AssetDatabase.SaveAssets();
                string canonicalGuid = AssetDatabase.AssetPathToGUID(assetPath);

                SerializedObject serializedObject = new(dataObject);
                serializedObject.FindProperty("_assetGuid").stringValue = "corrupted-guid";
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                dataObject.InvokeValidation();
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                TestDataObject reloadedDataObject = AssetDatabase.LoadAssetAtPath<TestDataObject>(
                    assetPath
                );
                SerializedObject reloadedSerializedObject = new(reloadedDataObject);

                Assert.AreEqual(canonicalGuid, reloadedDataObject.Id);
                Assert.AreEqual(
                    canonicalGuid,
                    reloadedSerializedObject.FindProperty("_assetGuid").stringValue
                );
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                if (dataObject != null && !AssetDatabase.Contains(dataObject))
                {
                    Object.DestroyImmediate(dataObject);
                }
                AssetDatabase.Refresh();
            }
        }
    }
}
