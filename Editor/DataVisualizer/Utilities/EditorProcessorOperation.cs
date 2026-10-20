namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Explicit main-asset batch processing. Preview never loads assets or invokes processor methods.</summary>
    public sealed class EditorProcessorOperation : IDisposable
    {
        public string Status { get; private set; } = "preview";
        public string Code { get; private set; } = "ok";
        public string Detail { get; private set; }
        public bool IsFinished => Status != "preview" && Status != "preparing";
        public int PreparedCount { get; private set; }
        public IReadOnlyList<EditorProcessorTarget> Targets { get; }
        public string TypeIdentity => _type.AssemblyQualifiedName;

        private readonly Type _type;
        private readonly List<EditorProcessorTarget> _targets = new();
        private readonly List<ScriptableObject> _loaded = new();
        private IDataProcessor _processor;
        private readonly Type _processorType;
        private bool _running;

        public EditorProcessorOperation(IDataProcessor processor, Type type, IReadOnlyList<string> assetGuids)
            : this(type, assetGuids)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        }

        /// <summary>Creates a preview without constructing the processor. Construction occurs only during apply.</summary>
        public EditorProcessorOperation(Type processorType, Type type, IReadOnlyList<string> assetGuids)
            : this(type, assetGuids)
        {
            if (
                processorType == null
                || !typeof(IDataProcessor).IsAssignableFrom(processorType)
                || processorType.IsAbstract
                || processorType.ContainsGenericParameters
                || (!processorType.IsValueType && processorType.GetConstructor(Type.EmptyTypes) == null)
            )
            {
                throw new ArgumentException("Specify a concrete processor with a public parameterless constructor.", nameof(processorType));
            }
            _processorType = processorType;
        }

        private EditorProcessorOperation(Type type, IReadOnlyList<string> assetGuids)
        {
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
            {
                throw new ArgumentException("Specify one concrete exact ScriptableObject type.", nameof(type));
            }
            if (assetGuids == null || assetGuids.Count == 0)
            {
                throw new ArgumentException("Specify a nonempty explicit GUID scope.", nameof(assetGuids));
            }
            _type = type;
            HashSet<string> seen = new(StringComparer.Ordinal);
            for (int i = 0; i < assetGuids.Count; i++)
            {
                string guid = assetGuids[i];
                if (guid == null || guid.Length != 32 || !System.Guid.TryParseExact(guid, "N", out _) || !seen.Add(guid.ToLowerInvariant()))
                {
                    throw new ArgumentException("Each target must have a unique Unity asset GUID.", nameof(assetGuids));
                }
                string canonicalGuid = guid.ToLowerInvariant();
                string path = AssetDatabase.GUIDToAssetPath(canonicalGuid);
                if (!IsExactTarget(path))
                {
                    throw new ArgumentException("Target is not a writable Assets main asset of the exact type: " + guid, nameof(assetGuids));
                }
                _targets.Add(new EditorProcessorTarget(canonicalGuid, path, "not_applied"));
            }
            Targets = _targets.AsReadOnly();
        }

        /// <summary>Confirms this exact preview. Call Advance to prepare and invoke the batch.</summary>
        public void Apply()
        {
            Run(apply: true);
        }

        /// <summary>Loads targets cooperatively, then invokes Process once with the complete batch.</summary>
        public void Advance(double budgetMilliseconds = 4)
        {
            if (double.IsNaN(budgetMilliseconds) || double.IsInfinity(budgetMilliseconds) || budgetMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            }
            Run(budgetMilliseconds: budgetMilliseconds);
        }

        public void Cancel()
        {
            Run(cancel: true);
        }

        public void Dispose()
        {
            Cancel();
        }

        private bool IsExactTarget(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.StartsWith("Assets/", StringComparison.Ordinal)
                && string.Equals(System.IO.Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase)
                && AssetDatabase.GetMainAssetTypeAtPath(path) == _type
                && AssetDatabase.IsOpenForEdit(path);
        }

        private void Run(bool apply = false, bool cancel = false, double budgetMilliseconds = 4)
        {
            if (IsFinished || _running)
            {
                return;
            }
            if (cancel)
            {
                Finish("cancelled", "cancelled", "No processor invocation occurred.");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (apply || Status == "preparing")
                {
                    Finish("rejected", "play_mode", "Mutation is unavailable during Play Mode transitions.");
                }
                return;
            }
            if (apply && Status == "preview")
            {
                Status = "preparing";
                return;
            }
            if (Status != "preparing" || AssetGuidDiscovery.IsAssetDatabaseBusy())
            {
                return;
            }
            Stopwatch clock = Stopwatch.StartNew();
            do
            {
                if (PreparedCount == _targets.Count)
                {
                    break;
                }
                EditorProcessorTarget target = _targets[PreparedCount];
                try
                {
                    string path = AssetDatabase.GUIDToAssetPath(target.Guid);
                    if (!string.Equals(path, target.Path, StringComparison.Ordinal) || !IsExactTarget(path))
                    {
                        _targets[PreparedCount] = new EditorProcessorTarget(target.Guid, target.Path, "stale_target");
                        Finish("rejected", "stale_target", "Preview changed. Create and confirm a new preview.");
                        return;
                    }
                    ScriptableObject asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                    if (IsFinished)
                    {
                        return;
                    }
                    if (asset == null || asset.GetType() != _type)
                    {
                        Finish("rejected", "asset_unavailable", "A target could not be loaded. No processor invocation occurred.");
                        return;
                    }
                    _loaded.Add(asset);
                    PreparedCount++;
                }
                catch (Exception error)
                {
                    Finish("rejected", "read_failed", error.Message);
                    return;
                }
            } while (clock.Elapsed.TotalMilliseconds < budgetMilliseconds);
            if (PreparedCount != _targets.Count)
            {
                return;
            }
            /*
                Revalidate every identity after cooperative preparation. A previous target may
                have moved, been deleted or changed type while a later target was loading.
            */
            for (int i = 0; i < _targets.Count; i++)
            {
                EditorProcessorTarget target = _targets[i];
                if (_loaded[i] == null || !string.Equals(AssetDatabase.GUIDToAssetPath(target.Guid), target.Path, StringComparison.Ordinal) || !IsExactTarget(target.Path))
                {
                    _targets[i] = new EditorProcessorTarget(target.Guid, target.Path, "stale_target");
                    Finish("rejected", "stale_target", "Preview changed. No processor invocation occurred.");
                    return;
                }
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Finish("rejected", "play_mode", "Mutation is unavailable during Play Mode transitions.");
                return;
            }
            _running = true;
            bool invoked = false;
            try
            {
                if (_processor == null)
                {
                    _processor = (IDataProcessor)Activator.CreateInstance(_processorType);
                }
                bool accepted = false;
                IEnumerable<Type> accepts = _processor.Accepts;
                if (accepts != null)
                {
                    foreach (Type type in accepts)
                    {
                        if (type == _type)
                        {
                            accepted = true;
                            break;
                        }
                    }
                }
                if (!accepted)
                {
                    Finish("rejected", "unsupported_type", "The processor does not accept this exact type.");
                }
                else
                {
                    /* Process is one indivisible unit: splitting it changes third-party semantics. */
                    invoked = true;
                    _processor.Process(_type, _loaded.ToArray());
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    SetOutcomes("applied");
                    Finish("complete", "ok", "The batch returned and assets were saved. No rollback is provided.");
                }
            }
            catch (Exception error)
            {
                if (invoked)
                {
                    SetOutcomes("outcome_unknown");
                    Finish("failed", "processor_failed", error.Message + " Changes may be partial or unsaved. Inspect every target; do not retry blindly.");
                }
                else
                {
                    Finish("rejected", "unsupported_type", error.Message);
                }
            }
            _running = false;
        }

        private void SetOutcomes(string code)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                EditorProcessorTarget target = _targets[i];
                _targets[i] = new EditorProcessorTarget(target.Guid, target.Path, code);
            }
        }

        private void Finish(string status, string code, string detail)
        {
            Status = status;
            Code = code;
            Detail = detail;
            _loaded.Clear();
            _processor = null;
        }
    }
}
