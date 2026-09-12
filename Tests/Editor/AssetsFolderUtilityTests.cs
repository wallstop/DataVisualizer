namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class AssetsFolderUtilityTests
    {
        private const string ProjectAssetsPath = "/project/Assets";

        private static TestCaseData Case(string name, string selected, string expected)
        {
            return new TestCaseData(selected, expected).SetName(name);
        }

        private static IEnumerable<TestCaseData> Cases()
        {
            yield return Case("Selected_equals_assets", ProjectAssetsPath, "Assets");

            yield return Case(
                "Subfolder_returns_prefixed_path",
                ProjectAssetsPath + "/Game",
                "Assets/Game"
            );

            yield return Case(
                "Nested_subfolder_returns_prefixed_path",
                ProjectAssetsPath + "/Game/Data",
                "Assets/Game/Data"
            );

            yield return Case("Outside_assets_fails", "/elsewhere/Assets", null);

            yield return Case("Sibling_prefix_fails", "/project/AssetsBackup", null);

            yield return Case(
                "Case_insensitive_sibling_prefix_fails",
                "/project/ASSETSBackup",
                null
            );

            yield return Case("Nested_sibling_prefix_fails", "/project/AssetsBackup/Fonts", null);

            yield return Case(
                "Case_insensitive_prefix_matches",
                "/project/assets/Game",
                "Assets/Game"
            );

            yield return Case("Case_insensitive_equal_matches", "/PROJECT/ASSETS", "Assets");
        }

        [Test]
        [TestCaseSource(nameof(Cases))]
        public void ShouldReturnRelativePathWhenSelectedPathIsValid(
            string selectedAbsolutePath,
            string expectedRelativePath
        )
        {
            bool succeeded = AssetsFolderUtility.TryGetAssetsRelativePath(
                selectedAbsolutePath,
                ProjectAssetsPath,
                out string relativePath
            );

            Assert.AreEqual(expectedRelativePath != null, succeeded);
            Assert.AreEqual(expectedRelativePath, relativePath);
        }

        [Test]
        public void ShouldReturnFalseWhenSelectedPathIsNull()
        {
            bool succeeded = AssetsFolderUtility.TryGetAssetsRelativePath(
                null,
                ProjectAssetsPath,
                out string relativePath
            );

            Assert.IsFalse(succeeded);
            Assert.IsNull(relativePath);
        }

        [Test]
        public void ShouldReturnFalseWhenSelectedPathIsEmpty()
        {
            bool succeeded = AssetsFolderUtility.TryGetAssetsRelativePath(
                string.Empty,
                ProjectAssetsPath,
                out string relativePath
            );

            Assert.IsFalse(succeeded);
            Assert.IsNull(relativePath);
        }
    }
}
