namespace WallstopStudios.DataVisualizer.Editor.Automation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    public enum DataVisualizerAutomationRequestKind
    {
        ReadConfiguration = 0,
        ApplyConfiguration = 1,
        ImportConfiguration = 2,
        ExportConfiguration = 3,
        DiscoverManagedTypes = 4,
        QueryAssetMetadata = 5,
        RefreshAssets = 6,
        SelectAsset = 7,
        OpenAsset = 8,
        PreviewAssetOperation = 9,
        ApplyAssetOperation = 10,
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
        public DataVisualizerAutomationRequestKind operation;
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

            if (!TryValidateRequestEnvelope(json, out string envelopeDiagnostic))
            {
                return FailureResult("Automation request JSON is malformed: " + envelopeDiagnostic);
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

            DataVisualizerAutomationResult result = new()
            {
                requestId = request.requestId,
                operation = request.operation,
            };
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

            if (!Enum.IsDefined(typeof(DataVisualizerAutomationRequestKind), request.operation))
            {
                return FailureResult(
                    request.requestId,
                    $"The requested automation operation is unsupported: {(int)request.operation}."
                );
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
            if (string.IsNullOrWhiteSpace(resultPath))
            {
                EditorApplication.Exit(2);
                return;
            }

            if (string.IsNullOrWhiteSpace(requestPath))
            {
                DataVisualizerAutomationResult result = FailureResult(
                    "-dataVisualizerRequest is required."
                );
                File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
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

        private static bool TryValidateRequestEnvelope(string json, out string diagnostic)
        {
            diagnostic = string.Empty;
            int index = 0;
            HashSet<string> properties = new(StringComparer.Ordinal);
            if (!TryConsume(json, ref index, '{'))
            {
                diagnostic = "Automation request JSON must be a top-level object.";
                return false;
            }

            bool afterComma = false;
            while (true)
            {
                SkipWhitespace(json, ref index);
                if (TryConsume(json, ref index, '}'))
                {
                    if (afterComma)
                    {
                        diagnostic = "Automation request JSON cannot contain a trailing comma.";
                        return false;
                    }
                    break;
                }
                afterComma = false;

                if (!TryReadPropertyName(json, ref index, out string propertyName))
                {
                    diagnostic = "Automation request JSON contains an invalid property name.";
                    return false;
                }
                if (!properties.Add(propertyName))
                {
                    diagnostic = $"Automation request JSON repeats property '{propertyName}'.";
                    return false;
                }
                if (!IsKnownProperty(propertyName))
                {
                    diagnostic =
                        $"Automation request JSON contains unknown property '{propertyName}'.";
                    return false;
                }
                if (!TryConsume(json, ref index, ':'))
                {
                    diagnostic =
                        $"Automation request JSON property '{propertyName}' is missing a value.";
                    return false;
                }
                JsonValueKind valueKind = ReadValueKind(json, ref index);
                if (valueKind == JsonValueKind.Invalid)
                {
                    diagnostic =
                        $"Automation request JSON property '{propertyName}' has an invalid value.";
                    return false;
                }
                if (
                    (propertyName == "schemaVersion" || propertyName == "operation")
                    && valueKind != JsonValueKind.Integer
                )
                {
                    diagnostic =
                        $"Automation request property '{propertyName}' must be an integer.";
                    return false;
                }
                if (propertyName == "requestId" && valueKind != JsonValueKind.String)
                {
                    diagnostic = "Automation request property 'requestId' must be a string.";
                    return false;
                }

                SkipWhitespace(json, ref index);
                if (TryConsume(json, ref index, '}'))
                {
                    break;
                }
                if (!TryConsume(json, ref index, ','))
                {
                    diagnostic = "Automation request JSON must separate properties with commas.";
                    return false;
                }
                afterComma = true;
            }

            string[] required = { "schemaVersion", "requestId", "operation" };
            foreach (string propertyName in required)
            {
                if (!properties.Contains(propertyName))
                {
                    diagnostic =
                        "Automation request JSON must contain schemaVersion, requestId, and operation.";
                    return false;
                }
            }
            SkipWhitespace(json, ref index);
            if (index != json.Length)
            {
                diagnostic = "Automation request JSON contains trailing content.";
                return false;
            }
            return true;
        }

        private enum JsonValueKind
        {
            Invalid,
            String,
            Integer,
            Number,
            Object,
            Array,
            Literal,
        }

        private static readonly HashSet<string> KnownProperties = new(StringComparer.Ordinal)
        {
            "schemaVersion",
            "requestId",
            "operation",
            "configuration",
            "configurationJson",
            "assemblyQualifiedTypeName",
            "page",
            "pageSize",
            "searchFolder",
            "guid",
            "assetOperation",
        };

        private static bool IsKnownProperty(string propertyName)
        {
            return KnownProperties.Contains(propertyName);
        }

        private static JsonValueKind ReadValueKind(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length)
            {
                return JsonValueKind.Invalid;
            }
            char valueStart = json[index];
            if (valueStart == '"')
            {
                return TryReadString(json, ref index)
                    ? JsonValueKind.String
                    : JsonValueKind.Invalid;
            }
            if (valueStart == '{')
            {
                return TrySkipComposite(json, ref index, '{', '}')
                    ? JsonValueKind.Object
                    : JsonValueKind.Invalid;
            }
            if (valueStart == '[')
            {
                return TrySkipComposite(json, ref index, '[', ']')
                    ? JsonValueKind.Array
                    : JsonValueKind.Invalid;
            }
            int start = index;
            while (index < json.Length && !",}] \t\r\n".Contains(json[index]))
            {
                index++;
            }
            string literal = json.Substring(start, index - start);
            if (literal == "true" || literal == "false" || literal == "null")
            {
                return JsonValueKind.Literal;
            }
            if (
                long.TryParse(
                    literal,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _
                )
            )
            {
                return JsonValueKind.Integer;
            }
            return double.TryParse(
                literal,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out _
            )
                ? JsonValueKind.Number
                : JsonValueKind.Invalid;
        }

        private static bool TryReadPropertyName(string json, ref int index, out string propertyName)
        {
            propertyName = null;
            int start = index;
            if (!TryReadString(json, ref index))
            {
                return false;
            }
            string encoded = json.Substring(start, index - start);
            try
            {
                propertyName = JsonUtility
                    .FromJson<StringValue>("{\"value\":" + encoded + "}")
                    ?.value;
            }
            catch (ArgumentException)
            {
                return false;
            }
            return !string.IsNullOrEmpty(propertyName);
        }

        [Serializable]
        private sealed class StringValue
        {
            public string value;
        }

        private static bool TryReadString(string json, ref int index)
        {
            if (index >= json.Length || json[index++] != '"')
            {
                return false;
            }
            bool escaped = false;
            while (index < json.Length)
            {
                char character = json[index++];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    return true;
                }
                else if (character < ' ')
                {
                    return false;
                }
            }
            return false;
        }

        private static bool TrySkipComposite(string json, ref int index, char opening, char closing)
        {
            if (index >= json.Length || json[index++] != opening)
            {
                return false;
            }
            int depth = 1;
            while (index < json.Length && depth > 0)
            {
                if (json[index] == '"' && !TryReadString(json, ref index))
                {
                    return false;
                }
                else if (json[index] == opening)
                {
                    depth++;
                    index++;
                }
                else if (json[index] == closing)
                {
                    depth--;
                    index++;
                }
                else
                {
                    index++;
                }
            }
            return depth == 0;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }
        }

        private static bool TryConsume(string json, ref int index, char expected)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length || json[index] != expected)
            {
                return false;
            }
            index++;
            return true;
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
