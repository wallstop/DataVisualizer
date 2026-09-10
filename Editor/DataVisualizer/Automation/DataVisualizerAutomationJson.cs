namespace WallstopStudios.DataVisualizer.Editor.Automation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    public enum DataVisualizerAutomationRequestKind
    {
        ReadConfiguration,
        ApplyConfiguration,
        ImportConfiguration,
        ExportConfiguration,
        DiscoverManagedTypes,
        QueryAssetMetadata,
        RefreshAssets,
        SelectAsset,
        OpenAsset,
        PreviewAssetOperation,
        ApplyAssetOperation,
    }

    [Serializable]
    public sealed class DataVisualizerAutomationRequest
    {
        public int schemaVersion = 1;
        public string requestId = Guid.NewGuid().ToString("N");
        public DataVisualizerAutomationRequestKind operation;
        public DataVisualizerConfiguration configuration;
        public string configurationJson;
        public string assemblyQualifiedTypeName;
        public int page;
        public int pageSize = 100;
        public string searchFolder;
        public string guid;
        public DataVisualizerAssetOperationRequest assetOperation;
    }

    [Serializable]
    public sealed class DataVisualizerAutomationResult
    {
        public int schemaVersion = 1;
        public string requestId;
        public bool succeeded;
        public bool complete;
        public string diagnostic;
        public DataVisualizerConfiguration configuration;
        public string configurationJson;
        public List<DataVisualizerTypeDescriptor> managedTypes = new();
        public DataVisualizerAssetMetadataPage metadata;
        public DataVisualizerAssetOperationResult assetOperation;
    }

    public static partial class DataVisualizerAutomation
    {
        public static string ExecuteRequestJson(string json)
        {
            return JsonUtility.ToJson(DispatchRequestJson(json), true);
        }

        public static DataVisualizerAutomationResult DispatchRequestJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return FailureResult("Automation request JSON is required.");
            }

            if (
                !HasJsonProperty(json, "schemaVersion")
                || !HasJsonProperty(json, "requestId")
                || !HasJsonProperty(json, "operation")
            )
            {
                return FailureResult(
                    "Automation request JSON must contain schemaVersion, requestId, and operation."
                );
            }

            try
            {
                DataVisualizerAutomationRequest request =
                    JsonUtility.FromJson<DataVisualizerAutomationRequest>(json);
                return DispatchRequest(request);
            }
            catch (ArgumentException exception)
            {
                return FailureResult($"Automation request JSON is malformed: {exception.Message}");
            }
        }

        public static DataVisualizerAutomationResult DispatchRequest(
            DataVisualizerAutomationRequest request
        )
        {
            if (request == null)
            {
                return FailureResult("An automation request is required.");
            }

            DataVisualizerAutomationResult result = new() { requestId = request.requestId };
            if (request.schemaVersion != 1)
            {
                return FailureResult(
                    request.requestId,
                    $"Unsupported automation schema version: {request.schemaVersion}."
                );
            }

            if (string.IsNullOrWhiteSpace(request.requestId))
            {
                return FailureResult("Automation requestId is required.");
            }

            try
            {
                switch (request.operation)
                {
                    case DataVisualizerAutomationRequestKind.ReadConfiguration:
                        result.configuration = ReadConfiguration();
                        return SucceedResult(result);

                    case DataVisualizerAutomationRequestKind.ApplyConfiguration:
                        return CopyOperationResult(
                            result,
                            ApplyConfiguration(request.configuration)
                        );

                    case DataVisualizerAutomationRequestKind.ImportConfiguration:
                        return CopyOperationResult(
                            result,
                            ImportConfigurationJson(request.configurationJson)
                        );

                    case DataVisualizerAutomationRequestKind.ExportConfiguration:
                        result.configurationJson = ExportConfigurationJson();
                        return SucceedResult(result);

                    case DataVisualizerAutomationRequestKind.DiscoverManagedTypes:
                        result.managedTypes.AddRange(DiscoverManagedTypes());
                        return SucceedResult(result);

                    case DataVisualizerAutomationRequestKind.QueryAssetMetadata:
                        result.metadata = QueryAssetMetadata(
                            request.assemblyQualifiedTypeName,
                            request.page,
                            request.pageSize,
                            request.searchFolder
                        );
                        return SucceedResult(result);

                    case DataVisualizerAutomationRequestKind.RefreshAssets:
                        return CopyOperationResult(result, RefreshAssets());

                    case DataVisualizerAutomationRequestKind.SelectAsset:
                        return CopyOperationResult(result, SelectAsset(request.guid));

                    case DataVisualizerAutomationRequestKind.OpenAsset:
                        return CopyOperationResult(result, OpenAsset(request.guid));

                    case DataVisualizerAutomationRequestKind.PreviewAssetOperation:
                        result.assetOperation = PreviewAssetOperation(request.assetOperation);
                        return CopyAssetOperationResult(result);

                    case DataVisualizerAutomationRequestKind.ApplyAssetOperation:
                        result.assetOperation = ApplyAssetOperation(request.assetOperation);
                        return CopyAssetOperationResult(result);

                    default:
                        return FailureResult(
                            request.requestId,
                            "The requested automation operation is unsupported."
                        );
                }
            }
            catch (ArgumentException exception)
            {
                return FailureResult(result.requestId, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return FailureResult(result.requestId, exception.Message);
            }
            catch (Exception exception)
            {
                return FailureResult(
                    result.requestId,
                    $"Automation operation failed: {exception.Message}"
                );
            }
        }

        public static void ExecuteRequestFile()
        {
            string requestPath = GetCommandLineArgument("-dataVisualizerRequest");
            string resultPath = GetCommandLineArgument("-dataVisualizerResult");
            if (string.IsNullOrWhiteSpace(requestPath) || string.IsNullOrWhiteSpace(resultPath))
            {
                EditorApplication.Exit(2);
                return;
            }

            try
            {
                DataVisualizerAutomationResult result = DispatchRequestJson(
                    File.ReadAllText(requestPath)
                );
                File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
                EditorApplication.Exit(result.succeeded ? 0 : 1);
            }
            catch (Exception exception)
            {
                DataVisualizerAutomationResult result = FailureResult(
                    $"Automation file execution failed: {exception.Message}"
                );
                File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
                EditorApplication.Exit(1);
            }
        }

        private static string GetCommandLineArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }

        private static bool HasJsonProperty(string json, string propertyName)
        {
            return json.Contains($"\"{propertyName}\"", StringComparison.Ordinal);
        }

        private static DataVisualizerAutomationResult CopyOperationResult(
            DataVisualizerAutomationResult result,
            DataVisualizerOperationResult operationResult
        )
        {
            result.succeeded = operationResult.succeeded;
            result.diagnostic = operationResult.diagnostic;
            result.complete = true;
            return result;
        }

        private static DataVisualizerAutomationResult CopyAssetOperationResult(
            DataVisualizerAutomationResult result
        )
        {
            result.succeeded = result.assetOperation.succeeded;
            result.diagnostic = result.assetOperation.diagnostic;
            result.complete = result.assetOperation.complete;
            return result;
        }

        private static DataVisualizerAutomationResult SucceedResult(
            DataVisualizerAutomationResult result
        )
        {
            result.succeeded = true;
            result.complete = true;
            result.diagnostic = string.Empty;
            return result;
        }

        private static DataVisualizerAutomationResult FailureResult(string diagnostic)
        {
            return FailureResult(null, diagnostic);
        }

        private static DataVisualizerAutomationResult FailureResult(
            string requestId,
            string diagnostic
        )
        {
            return new DataVisualizerAutomationResult
            {
                requestId = requestId,
                succeeded = false,
                complete = true,
                diagnostic = diagnostic,
            };
        }
    }
}
