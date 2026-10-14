namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Unity;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    /*
        Pins the relevance decision's semantics after #226: the decision reads the
        imported asset's type identity instead of loading the object, so a tracked
        data type and the settings asset stay relevant, while untracked, missing,
        and non-asset paths stay irrelevant. These are the semantics the
        LoadAssetAtPath-based decision provided; the switch to the type-only query
        must not move them.
    */
    public sealed class AssetProcessorRelevanceTests
    {
        private static string CreateFixtureFolder()
        {
            string folder = "Assets/DataVisualizerRelevanceTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            return folder;
        }

        private static void CreateAsset(ScriptableObject asset, string assetPath)
        {
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
        }

        private static void DeleteFixtureFolder(string folder)
        {
            AssetDatabase.DeleteAsset(folder);
            AssetDatabase.Refresh();
        }

        [TestCase("Assets/Some/Deleted.prefab")]
        [TestCase("Assets/Some/Missing.asset")]
        public void ShouldIgnorePathsOutsideTrackedAssets(string path)
        {
            HashSet<Type> relevantTypes = new() { typeof(TestDataObject) };
            Assert.IsFalse(DataVisualizerAssetProcessor.IsRelevantAsset(relevantTypes, path));
        }

        [Test]
        public void ShouldDetectTrackedTypeFromTypeIdentity()
        {
            string folder = CreateFixtureFolder();
            try
            {
                string assetPath = $"{folder}/Tracked.asset";
                CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), assetPath);
                HashSet<Type> relevantTypes = new() { typeof(TestDataObject) };
                Assert.IsTrue(DataVisualizerAssetProcessor.IsRelevantAsset(relevantTypes, assetPath));
            }
            finally
            {
                DeleteFixtureFolder(folder);
            }
        }

        [Test]
        public void ShouldIgnoreAssetOfUntrackedType()
        {
            string folder = CreateFixtureFolder();
            try
            {
                string assetPath = $"{folder}/Untracked.asset";
                CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), assetPath);
                HashSet<Type> relevantTypes = new() { typeof(PlayModeDataObject) };
                Assert.IsFalse(DataVisualizerAssetProcessor.IsRelevantAsset(relevantTypes, assetPath));
            }
            finally
            {
                DeleteFixtureFolder(folder);
            }
        }

        [Test]
        public void ShouldDetectSettingsAssetWithoutTrackedTypes()
        {
            string folder = CreateFixtureFolder();
            try
            {
                string assetPath = $"{folder}/Settings.asset";
                CreateAsset(ScriptableObject.CreateInstance<DataVisualizerSettings>(), assetPath);
                Assert.IsTrue(DataVisualizerAssetProcessor.IsRelevantAsset(new HashSet<Type>(), assetPath));
            }
            finally
            {
                DeleteFixtureFolder(folder);
            }
        }
    }
}
