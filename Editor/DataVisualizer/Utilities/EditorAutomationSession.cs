namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using Data;
    using UnityEngine;

    /// <summary>A disposable, main-thread JSON session over supported editor services.</summary>
    public sealed class EditorAutomationSession : IDisposable
    {
        public bool IsFinished =>
            _errorCode != null
            || _cancelled
            || (
                _processorOperation != null ? _processorOperation.Status == "preview" || _processorOperation.IsFinished : (_query?.IsComplete ?? (_validation?.IsComplete ?? false))
            );
        public bool IsWaitingForEditor => UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || AssetGuidDiscovery.IsAssetDatabaseBusy();
        public int DefaultOffset { get; private set; }
        public int DefaultPageSize { get; private set; } = EditorAutomationRequest.DefaultPageSize;

        private EditorAssetQuery _query;
        private EditorProcessorOperation _processorOperation;
        private EditorAssetValidation _validation;
        private string _errorCode;
        private string _errorDetail;
        private bool _cancelled;
        private string _stopReason;

        private EditorAutomationSession() { }

        public static EditorAutomationSession Start(string json, bool requireSinglePage = false)
        {
            EditorAutomationSession session = new();
            EditorAutomationRequest request;
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    throw new ArgumentException("Request JSON is empty.");
                }
                request = new EditorAutomationRequest();
                JsonUtility.FromJsonOverwrite(json, request);
            }
            catch (ArgumentException error)
            {
                session.Reject("invalid_json", error.Message);
                return session;
            }
            if (request.schemaVersion != 1)
            {
                session.Reject("unsupported_version", "Only schemaVersion 1 is supported.");
                return session;
            }
            if (request.operation != "query" && request.operation != "validate" && request.operation != "previewProcessor" && request.operation != "applyProcessor")
            {
                session.Reject("unsupported_operation", "Supported operations: query, validate, previewProcessor, applyProcessor. Configuration is not implemented.");
                return session;
            }
            if (request.offset < 0 || request.pageSize < 1 || 1000 < request.pageSize)
            {
                session.Reject("invalid_page", "Use a nonnegative offset and pageSize from 1 to 1000.");
                return session;
            }
            session.DefaultOffset = request.offset;
            session.DefaultPageSize = request.pageSize;
            try
            {
                if (request.operation == "previewProcessor" || request.operation == "applyProcessor")
                {
                    if (requireSinglePage && request.operation == "applyProcessor" && (request.offset != 0 || request.pageSize < (request.assetGuids?.Length ?? 0)))
                    {
                        session.Reject("invalid_page", "File apply must return every target in one page. Use offset 0 and pageSize at least the scope count (maximum 1000).");
                        return session;
                    }
                    session.StartProcessor(request);
                }
                else if (!string.IsNullOrEmpty(request.processorIdentity) || (request.confirmPaths?.Length ?? 0) != 0)
                {
                    throw new ArgumentException("Processor fields are only accepted by processor operations.");
                }
                else if (request.operation == "validate")
                {
                    if (
                        (request.typeIdentities?.Length ?? 0) != 0
                        || !string.IsNullOrEmpty(request.text)
                        || (request.andLabels?.Length ?? 0) != 0
                        || (request.orLabels?.Length ?? 0) != 0
                        || !string.IsNullOrEmpty(request.labelCombination)
                    )
                    {
                        throw new ArgumentException("Validation accepts only explicit assetGuids, without query filters.");
                    }
                    session._validation = new EditorAssetValidation(request.assetGuids);
                }
                else
                {
                    if ((request.assetGuids?.Length ?? 0) != 0 || request.typeIdentities == null || request.typeIdentities.Length == 0)
                    {
                        throw new ArgumentException("Queries require explicit typeIdentities and do not accept assetGuids.");
                    }
                    List<Type> types = new();
                    foreach (string identity in request.typeIdentities)
                    {
                        types.Add(ResolveExactType(identity));
                    }
                    if (!string.IsNullOrEmpty(request.labelCombination) && request.labelCombination != "and" && request.labelCombination != "or")
                    {
                        throw new ArgumentException("labelCombination must be and or or.");
                    }
                    TypeLabelFilterConfig labels = new()
                    {
                        andLabels = new List<string>(request.andLabels ?? Array.Empty<string>()),
                        orLabels = new List<string>(request.orLabels ?? Array.Empty<string>()),
                        combinationType = request.labelCombination == "or" ? LabelCombinationType.Or : LabelCombinationType.And,
                    };
                    session._query = new EditorAssetQuery(types, request.text, labels);
                }
            }
            catch (Exception error)
            {
                session.Reject("invalid_scope", error.Message);
            }
            return session;
        }

        private static Type ResolveExactType(string identity)
        {
            Type type = string.IsNullOrEmpty(identity) ? null : Type.GetType(identity, false);
            if (type == null || !string.Equals(type.AssemblyQualifiedName, identity, StringComparison.Ordinal))
            {
                throw new ArgumentException("Use an available assembly-qualified exact type identity: " + identity);
            }
            return type;
        }

        public void Advance(double budgetMilliseconds = 4)
        {
            if (IsFinished)
            {
                return;
            }
            if (_processorOperation != null)
            {
                _processorOperation.Advance(budgetMilliseconds);
            }
            else if (_query != null)
            {
                _query.Advance(budgetMilliseconds);
            }
            else
            {
                _validation.Advance(budgetMilliseconds);
            }
        }

        public string GetResponseJson()
        {
            return GetResponseJson(DefaultOffset, DefaultPageSize);
        }

        public string GetResponseJson(int offset, int count)
        {
            if (offset < 0 || count < 1 || 1000 < count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (_processorOperation != null && _errorCode == null)
            {
                return GetProcessorResponse(offset, count);
            }
            bool complete = _query?.IsComplete ?? (_validation?.IsComplete ?? false);
            int available = _query?.ResultCount ?? (_validation?.Findings.Count ?? 0);
            EditorAutomationResponse response = new()
            {
                status =
                    _errorCode != null ? "rejected"
                    : complete ? "complete"
                    : _cancelled ? "cancelled"
                    : IsWaitingForEditor ? "waiting"
                    : "running",
                code = _errorCode ?? (_cancelled ? "cancelled" : "ok"),
                detail = _errorDetail ?? _stopReason,
                complete = complete,
                processedCount = _query?.ScannedCount ?? (_validation?.ProcessedCount ?? 0),
                scopeCount = _query != null ? (complete ? _query.ScannedCount : -1) : (_validation?.TotalCount ?? 0),
                availableCount = available,
                offset = offset,
                items = new EditorAutomationItem[Math.Min(count, Math.Max(0, available - offset))],
            };
            if (_query != null)
            {
                EditorAssetRecord[] records = _query.GetPage(offset, count);
                for (int i = 0; i < records.Length; i++)
                {
                    EditorAssetRecord record = records[i];
                    string[] labels = new string[record.Labels.Count];
                    for (int label = 0; label < labels.Length; label++)
                    {
                        labels[label] = record.Labels[label];
                    }
                    response.items[i] = new EditorAutomationItem
                    {
                        guid = record.Guid,
                        path = record.Path,
                        name = record.Name,
                        typeIdentity = record.TypeIdentity,
                        labels = labels,
                        code =
                            record.Error == null ? "ok"
                            : record.Error.StartsWith("read_failed", StringComparison.Ordinal) ? "read_failed"
                            : record.Error,
                        detail = record.Error,
                    };
                }
            }
            else if (_validation != null)
            {
                for (int i = 0; i < response.items.Length; i++)
                {
                    AssetValidationFinding finding = _validation.Findings[offset + i];
                    response.items[i] = new EditorAutomationItem
                    {
                        guid = finding.Guid,
                        path = finding.Path,
                        code = finding.Code,
                        propertyPath = finding.PropertyPath,
                        detail = finding.Detail,
                    };
                }
            }
            return JsonUtility.ToJson(response, true);
        }

        public void Cancel(string reason = "caller_cancelled")
        {
            if (IsFinished)
            {
                return;
            }
            _cancelled = true;
            _stopReason = reason;
            _query?.Dispose();
            _validation?.Dispose();
            _processorOperation?.Dispose();
        }

        public void Dispose()
        {
            Cancel("disposed");
            _query?.Dispose();
            _validation?.Dispose();
            _processorOperation?.Dispose();
        }

        private void StartProcessor(EditorAutomationRequest request)
        {
            if (
                request.typeIdentities == null
                || request.typeIdentities.Length != 1
                || !string.IsNullOrEmpty(request.text)
                || (request.andLabels?.Length ?? 0) != 0
                || (request.orLabels?.Length ?? 0) != 0
                || !string.IsNullOrEmpty(request.labelCombination)
            )
            {
                throw new ArgumentException("Processors require one exact type and explicit assetGuids, without filters.");
            }
            _processorOperation = new EditorProcessorOperation(ResolveExactType(request.processorIdentity), ResolveExactType(request.typeIdentities[0]), request.assetGuids);
            if (request.operation == "previewProcessor")
            {
                if ((request.confirmPaths?.Length ?? 0) != 0)
                {
                    throw new ArgumentException("Preview does not accept confirmation paths.");
                }
                return;
            }
            if (request.confirmPaths == null || request.confirmPaths.Length != _processorOperation.Targets.Count)
            {
                throw new ArgumentException("Apply requires every preview path in target order.");
            }
            for (int i = 0; i < request.confirmPaths.Length; i++)
            {
                if (!string.Equals(request.confirmPaths[i], _processorOperation.Targets[i].Path, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Confirmation paths differ from current targets. Preview again.");
                }
            }
            _processorOperation.Apply();
        }

        private string GetProcessorResponse(int offset, int count)
        {
            bool preview = _processorOperation.Status == "preview";
            bool complete = preview || _processorOperation.Status == "complete";
            EditorAutomationResponse response = new()
            {
                status =
                    complete ? "complete"
                    : _processorOperation.Status == "preparing" ? (IsWaitingForEditor ? "waiting" : "running")
                    : _processorOperation.Status,
                code = preview ? "preview" : _processorOperation.Code,
                detail = preview ? "Preview only. Confirm every target path before apply." : _processorOperation.Detail,
                complete = complete,
                scopeCount = _processorOperation.Targets.Count,
                processedCount = _processorOperation.PreparedCount,
                availableCount = _processorOperation.Targets.Count,
                offset = offset,
                coverage = "Exact processor targets only. Process is indivisible; arbitrary partial changes cannot be rolled back or attributed to individual assets.",
                items = new EditorAutomationItem[Math.Min(count, Math.Max(0, _processorOperation.Targets.Count - offset))],
            };
            for (int i = 0; i < response.items.Length; i++)
            {
                EditorProcessorTarget target = _processorOperation.Targets[offset + i];
                response.items[i] = new EditorAutomationItem
                {
                    guid = target.Guid,
                    path = target.Path,
                    typeIdentity = _processorOperation.TypeIdentity,
                    code = target.Code,
                    detail = target.Detail,
                };
            }
            return JsonUtility.ToJson(response, true);
        }

        private void Reject(string code, string detail)
        {
            _errorCode = code;
            _errorDetail = detail;
        }
    }
}
