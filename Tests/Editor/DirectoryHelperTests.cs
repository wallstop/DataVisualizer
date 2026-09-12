namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.DataVisualizer.Helper;

    public sealed class DirectoryHelperTests
    {
        private const string TempRootFolder = "Assets/DataVisualizerDirectoryHelperTests";

        private static string ProjectRoot()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            if (0 < projectRoot.Length && projectRoot[projectRoot.Length - 1] == '/')
            {
                projectRoot = projectRoot[..^1];
            }

            return projectRoot;
        }

        private static TestCaseData Case(string name, string absolutePath, string expected)
        {
            return new TestCaseData(absolutePath, expected).SetName(name);
        }

        private static IEnumerable<TestCaseData> Cases()
        {
            string projectRoot = ProjectRoot();
            string outsideRoot = projectRoot[..^1] + "Elsewhere";

            yield return Case(
                "Inside_assets_returns_relative_path",
                projectRoot + "/Assets/Game/Data/Styles.uss",
                "Assets/Game/Data/Styles.uss"
            );

            yield return Case(
                "Inside_packages_returns_relative_path",
                projectRoot
                    + "/Packages/com.wallstop-studios.data-visualizer/Editor/Styles/DataVisualizerStyles.uss",
                "Packages/com.wallstop-studios.data-visualizer/Editor/Styles/DataVisualizerStyles.uss"
            );

            yield return Case(
                "Windows_backslashes_inside_assets_returns_relative_path",
                projectRoot + @"\Assets\Game\Styles.uss",
                "Assets/Game/Styles.uss"
            );

            yield return Case(
                "Case_insensitive_prefix_returns_original_suffix",
                projectRoot.ToLowerInvariant() + "/Assets/Game",
                "Assets/Game"
            );

            yield return Case(
                "Case_insensitive_exact_root_fails",
                projectRoot.ToUpperInvariant(),
                string.Empty
            );

            yield return Case(
                "Case_insensitive_inside_assets_returns_relative_path",
                projectRoot + "/assets/game/data/styles.uss",
                "assets/game/data/styles.uss"
            );

            yield return Case(
                "Sibling_prefix_fails",
                projectRoot + "Backups/File.uss",
                string.Empty
            );

            yield return Case(
                "Case_insensitive_sibling_prefix_fails",
                projectRoot + "bAcKuPs/File.uss",
                string.Empty
            );

            yield return Case(
                "Nested_sibling_prefix_fails",
                projectRoot + "Backups/Nested/File.uss",
                string.Empty
            );

            yield return Case("Outside_project_fails", outsideRoot + "/File.uss", string.Empty);

            yield return Case("Null_path_fails", null, string.Empty);

            yield return Case("Empty_path_fails", string.Empty, string.Empty);

            yield return Case("Whitespace_path_fails", "   ", string.Empty);

            yield return Case("Exact_project_root_fails", projectRoot, string.Empty);

            yield return Case(
                "Project_root_with_trailing_separator_fails",
                projectRoot + "/",
                string.Empty
            );
        }

        private static IEnumerable<TestCaseData> OutsideAssetsCases()
        {
            yield return new TestCaseData("NotAssets/Folder").SetName("Outside_assets_fails");
            yield return new TestCaseData("AssetsBackup/Folder").SetName("Sibling_prefix_fails");
            yield return new TestCaseData("assets/Folder").SetName("Lowercase_assets_fails");
        }

        [Test]
        [TestCaseSource(nameof(Cases))]
        public void ShouldReturnExpectedPathWhenConvertingAbsolutePath(
            string absolutePath,
            string expectedRelativePath
        )
        {
            string relativePath = DirectoryHelper.AbsoluteToUnityRelativePath(absolutePath);

            Assert.AreEqual(expectedRelativePath, relativePath);
        }

        [Test]
        [TestCaseSource(nameof(OutsideAssetsCases))]
        public void ShouldRejectFolderCreationWhenPathIsOutsideAssets(string relativeDirectoryPath)
        {
            LogAssert.Expect(
                LogType.Error,
                $"Attempted to create directory outside of Assets: '{relativeDirectoryPath}'"
            );

            ArgumentException thrown = Assert.Throws<ArgumentException>(() =>
                DirectoryHelper.EnsureDirectoryExists(relativeDirectoryPath)
            );

            Assert.AreEqual(nameof(relativeDirectoryPath), thrown.ParamName);
        }

        [Test]
        public void ShouldCreateMissingNestedFoldersWhenPathIsInsideAssets()
        {
            string nestedFolder = TempRootFolder + "/Parents/Leaf";
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(TempRootFolder);
                    AssetDatabase.Refresh();
                });

                DirectoryHelper.EnsureDirectoryExists(nestedFolder);

                Assert.IsTrue(AssetDatabase.IsValidFolder(TempRootFolder));
                Assert.IsTrue(AssetDatabase.IsValidFolder(TempRootFolder + "/Parents"));
                Assert.IsTrue(AssetDatabase.IsValidFolder(nestedFolder));
            }
        }

        [Test]
        public void ShouldKeepExistingFolderWhenPathIsInsideAssets()
        {
            string existingFolder = TempRootFolder + "/Existing";
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(TempRootFolder);
                    AssetDatabase.Refresh();
                });

                DirectoryHelper.EnsureDirectoryExists(existingFolder);
                DirectoryHelper.EnsureDirectoryExists(existingFolder);

                Assert.IsTrue(AssetDatabase.IsValidFolder(existingFolder));
            }
        }

        [Test]
        public void ShouldAcceptAssetsRootWithoutCreatingFolders()
        {
            Assert.DoesNotThrow(() => DirectoryHelper.EnsureDirectoryExists("Assets"));
            Assert.DoesNotThrow(() => DirectoryHelper.EnsureDirectoryExists("assets"));
        }
    }
}
