namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class AssetGuidDiscoveryTests
    {
        [Test]
        public void Should_FindCreatedAsset_When_TypeFilterDoesNotReturnIt()
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

            try
            {
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                string[] discoveredGuids = AssetGuidDiscovery.FindCandidates(
                    typeof(EditorOnlyCreationData),
                    rootFolder
                );

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
            finally
            {
                AssetDatabase.DeleteAsset(rootFolder);
                AssetDatabase.Refresh();
            }
        }

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
    }

    public sealed class EditorOnlyCreationData : ScriptableObject { }
}
