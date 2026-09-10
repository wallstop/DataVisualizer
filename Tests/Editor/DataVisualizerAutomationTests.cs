namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Automation;
    using WallstopStudios.DataVisualizer.Editor.Data;

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

        [Test]
        public void Should_RejectUnknownMetadataTypeIdentity()
        {
            Assert.Throws<System.ArgumentException>(() =>
                DataVisualizerAutomation.QueryAssetMetadata(
                    "Missing.Type, Missing.Assembly",
                    0,
                    100
                )
            );
        }

        [Test]
        public void Should_RejectAssetPathThatEscapesProjectAssets()
        {
            DataVisualizerConfiguration configuration = new()
            {
                dataFolderPath = "Assets/../../outside",
            };

            DataVisualizerOperationResult result = DataVisualizerAutomation.ApplyConfiguration(
                configuration
            );

            Assert.IsFalse(result.succeeded);
            StringAssert.Contains("inside", result.diagnostic);
        }

        [Test]
        public void Should_RejectEmptyAssetOperationRequest()
        {
            DataVisualizerAssetOperationResult result =
                DataVisualizerAutomation.PreviewAssetOperation(null);

            Assert.IsFalse(result.succeeded);
            Assert.IsTrue(result.complete);
            StringAssert.Contains("request", result.diagnostic);
        }

        [Test]
        public void Should_ReportMissingAssetInPreview()
        {
            DataVisualizerAssetOperationResult result =
                DataVisualizerAutomation.PreviewAssetOperation(
                    new DataVisualizerAssetOperationRequest
                    {
                        operation = DataVisualizerAssetOperationKind.Delete,
                        guids = new[] { "missing-guid" },
                    }
                );

            Assert.IsFalse(result.succeeded);
            Assert.IsTrue(result.complete);
            Assert.AreEqual(1, result.items.Count);
            StringAssert.Contains("not found", result.items[0].diagnostic);
        }

        [Test]
        public void Should_PreflightDuplicateAssetGuidsBeforeMutation()
        {
            DataVisualizerAssetOperationResult result =
                DataVisualizerAutomation.PreviewAssetOperation(
                    new DataVisualizerAssetOperationRequest
                    {
                        operation = DataVisualizerAssetOperationKind.Delete,
                        guids = new[] { "missing-guid", "missing-guid" },
                    }
                );

            Assert.IsFalse(result.succeeded);
            Assert.AreEqual(2, result.items.Count);
            StringAssert.Contains("more than once", result.items[1].diagnostic);
        }

        [Test]
        public void Should_PreviewCreateWithoutCreatingAnAsset()
        {
            DataVisualizerAssetOperationResult result =
                DataVisualizerAutomation.PreviewAssetOperation(
                    new DataVisualizerAssetOperationRequest
                    {
                        operation = DataVisualizerAssetOperationKind.Create,
                        assemblyQualifiedTypeName =
                            typeof(DataVisualizerSettings).AssemblyQualifiedName,
                        destinationFolder = "Assets",
                        value = "AutomationPreviewOnly",
                    }
                );

            Assert.IsTrue(result.succeeded);
            Assert.IsTrue(result.preview);
            Assert.AreEqual(1, result.items.Count);
            Assert.IsTrue(string.IsNullOrWhiteSpace(result.items[0].guid));
            StringAssert.EndsWith("AutomationPreviewOnly.asset", result.items[0].resultingPath);
        }

        [Test]
        public void Should_RejectCloneWithoutExactlyOneSourceGuid()
        {
            DataVisualizerAssetOperationResult result =
                DataVisualizerAutomation.PreviewAssetOperation(
                    new DataVisualizerAssetOperationRequest
                    {
                        operation = DataVisualizerAssetOperationKind.Clone,
                        guids = System.Array.Empty<string>(),
                    }
                );

            Assert.IsFalse(result.succeeded);
            StringAssert.Contains("exactly one", result.diagnostic);
        }

        [Test]
        public void Should_RejectUnsupportedAutomationSchemaVersion()
        {
            DataVisualizerAutomationResult result = DataVisualizerAutomation.DispatchRequestJson(
                "{\"schemaVersion\":2,\"requestId\":\"schema-2\",\"operation\":0}"
            );

            Assert.IsFalse(result.succeeded);
            Assert.IsTrue(result.complete);
            StringAssert.Contains("schema version", result.diagnostic);
            Assert.AreEqual("schema-2", result.requestId);
            Assert.AreEqual(
                DataVisualizerAutomationRequestKind.ReadConfiguration,
                result.operation
            );
        }

        [Test]
        public void Should_ReturnStructuredResultForMalformedAutomationJson()
        {
            string resultJson = DataVisualizerAutomation.ExecuteRequestJson("{");
            DataVisualizerAutomationResult result =
                UnityEngine.JsonUtility.FromJson<DataVisualizerAutomationResult>(resultJson);

            Assert.IsFalse(result.succeeded);
            Assert.IsTrue(result.complete);
            StringAssert.Contains("malformed", result.diagnostic);
        }

        [Test]
        public void Should_RejectUnknownAndDuplicateAutomationProperties()
        {
            DataVisualizerAutomationResult unknown = DataVisualizerAutomation.DispatchRequestJson(
                "{\"schemaVersion\":1,\"requestId\":\"unknown\",\"operation\":0,\"unexpected\":true}"
            );
            Assert.IsFalse(unknown.succeeded);
            StringAssert.Contains("unknown property", unknown.diagnostic);

            DataVisualizerAutomationResult duplicate = DataVisualizerAutomation.DispatchRequestJson(
                "{\"schemaVersion\":1,\"requestId\":\"first\",\"requestId\":\"second\",\"operation\":0}"
            );
            Assert.IsFalse(duplicate.succeeded);
            StringAssert.Contains("repeats property", duplicate.diagnostic);
        }

        [Test]
        public void Should_RejectUnsupportedAutomationOperationValue()
        {
            DataVisualizerAutomationResult result = DataVisualizerAutomation.DispatchRequestJson(
                "{\"schemaVersion\":1,\"requestId\":\"operation-99\",\"operation\":99}"
            );

            Assert.IsFalse(result.succeeded);
            StringAssert.Contains("unsupported", result.diagnostic);
        }
    }
}
