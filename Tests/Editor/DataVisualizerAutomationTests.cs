namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Automation;

    public sealed class DataVisualizerAutomationTests
    {
        [Test]
        public void Should_RoundTripConfigurationJson_WithoutWindow()
        {
            DataVisualizerConfiguration source = new()
            {
                dataFolderPath = "Assets/Gameplay\\",
                persistStateInSettingsAsset = true,
                selectActiveObject = true,
            };

            string json = UnityEngine.JsonUtility.ToJson(source);
            DataVisualizerConfiguration copy =
                UnityEngine.JsonUtility.FromJson<DataVisualizerConfiguration>(json);

            Assert.AreEqual(source.dataFolderPath, copy.dataFolderPath);
            Assert.IsTrue(copy.persistStateInSettingsAsset);
            Assert.IsTrue(copy.selectActiveObject);
        }

        [Test]
        public void Should_RejectConfigurationOutsideAssets()
        {
            DataVisualizerConfiguration configuration = new() { dataFolderPath = "Packages/Data" };

            DataVisualizerOperationResult result = DataVisualizerAutomation.ApplyConfiguration(
                configuration
            );

            Assert.IsFalse(result.succeeded);
            StringAssert.Contains("Assets", result.diagnostic);
        }

        [Test]
        public void Should_RejectMalformedConfigurationJson()
        {
            DataVisualizerOperationResult result = DataVisualizerAutomation.ImportConfigurationJson(
                "{"
            );

            Assert.IsFalse(result.succeeded);
            StringAssert.Contains("malformed", result.diagnostic);
        }

        [Test]
        public void Should_RejectMissingAssetGuid()
        {
            DataVisualizerOperationResult result = DataVisualizerAutomation.SelectAsset(
                string.Empty
            );

            Assert.IsFalse(result.succeeded);
            StringAssert.Contains("GUID", result.diagnostic);
        }

        [Test]
        public void Should_RejectInvalidMetadataPageSize()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                DataVisualizerAutomation.QueryAssetMetadata(null, 0, 0)
            );
        }
    }
}
