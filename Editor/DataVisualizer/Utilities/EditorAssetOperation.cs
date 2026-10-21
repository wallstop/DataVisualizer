namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>Explicit main-asset operations. Preview reads metadata only; apply yields between assets.</summary>
    public sealed class EditorAssetOperation : IDisposable
    {
        private const int MaximumTargets = 1000;

        public string Status { get; private set; } = "preview";
        public string Code { get; private set; } = "preview";
        public string Detail { get; private set; }
        public bool IsFinished => Status != "preview" && Status != "running";
        public int ProcessedCount { get; private set; }
        public int SucceededCount { get; private set; }
        public double LongestSliceMilliseconds { get; private set; }
        public string TypeIdentity => _type.AssemblyQualifiedName;
        public IReadOnlyList<EditorAssetOperationTarget> Targets { get; }
        public IReadOnlyList<string> ConfirmationPaths { get; }

        private readonly Type _type;
        private readonly EditorAssetOperationKind _kind;
        private readonly List<EditorAssetOperationTarget> _targets = new();
        private readonly ReusableDisposalScope<EditorAssetOperation> _calls = new(static owner => owner.Run(exit: true));
        private readonly ReusableDisposalScope<ScriptableObject> _temporaryObjects = new(static asset =>
        {
            if (asset != null && !AssetDatabase.Contains(asset))
            {
                Object.DestroyImmediate(asset);
            }
        });
        private bool _running;
        private bool _cancelRequested;

        public EditorAssetOperation(EditorAssetOperationKind kind, Type type, IReadOnlyList<string> assetGuids, IReadOnlyList<string> destinationPaths = null)
        {
            if (
                type == null
                || !typeof(ScriptableObject).IsAssignableFrom(type)
                || type.IsAbstract
                || type.IsGenericType
                || typeof(EditorWindow).IsAssignableFrom(type)
                || typeof(Editor).IsAssignableFrom(type)
            )
            {
                throw new ArgumentException("Specify a concrete exact ScriptableObject type.", nameof(type));
            }
            if (
                kind != EditorAssetOperationKind.Create
                && kind != EditorAssetOperationKind.Clone
                && kind != EditorAssetOperationKind.Rename
                && kind != EditorAssetOperationKind.Move
                && kind != EditorAssetOperationKind.Delete
            )
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            bool create = kind == EditorAssetOperationKind.Create;
            int count = create ? destinationPaths?.Count ?? 0 : assetGuids?.Count ?? 0;
            if (
                count < 1
                || MaximumTargets < count
                || (create && (assetGuids?.Count ?? 0) != 0)
                || (kind == EditorAssetOperationKind.Delete ? (destinationPaths?.Count ?? 0) != 0 : destinationPaths == null || destinationPaths.Count != count)
            )
            {
                throw new ArgumentException("Use 1-1000 explicit targets and one destination per non-delete target. Create accepts no source GUIDs.");
            }
            _type = type;
            _kind = kind;
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> destinations = new(StringComparer.OrdinalIgnoreCase);
            List<string> confirmations = new();
            for (int i = 0; i < count; i++)
            {
                string guid = create ? string.Empty : assetGuids[i];
                if (!create && (!System.Guid.TryParseExact(guid, "N", out _) || !seen.Add(guid)))
                {
                    throw new ArgumentException("Source GUIDs must be unique 32-hex-digit Unity identities.");
                }
                guid = guid.ToLowerInvariant();
                string path = create ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
                string destination = kind == EditorAssetOperationKind.Delete ? string.Empty : destinationPaths[i];
                if (!create && !IsExactSource(path))
                {
                    throw new ArgumentException("Source is not a writable Assets main asset of the exact type: " + guid);
                }
                if (kind != EditorAssetOperationKind.Delete)
                {
                    ValidateDestination(destination);
                    if (
                        !destinations.Add(destination)
                        || (
                            kind == EditorAssetOperationKind.Rename
                            && (
                                !string.Equals(DirectoryOf(path), DirectoryOf(destination), StringComparison.Ordinal)
                                || !string.Equals(Path.GetExtension(path), Path.GetExtension(destination), StringComparison.Ordinal)
                            )
                        )
                    )
                    {
                        throw new ArgumentException("Destinations must be unique; rename must keep the source folder and extension spelling.");
                    }
                }
                _targets.Add(new EditorAssetOperationTarget(guid, path, destination));
                if (!create)
                {
                    confirmations.Add(path);
                }
                if (destination.Length != 0)
                {
                    confirmations.Add(destination);
                }
            }
            Targets = _targets.AsReadOnly();
            ConfirmationPaths = confirmations.AsReadOnly();
        }

        private static string DirectoryOf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? string.Empty : path.Substring(0, slash);
        }

        private static string PhysicalPath(string assetPath)
        {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
        }

        private static void ValidateDestination(string path)
        {
            if (
                string.IsNullOrEmpty(path)
                || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || path.Contains('\\')
                || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                || path.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) != -1
            )
            {
                throw new ArgumentException("Use an explicit canonical Assets .asset destination.");
            }
            if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(path)))
            {
                throw new ArgumentException("The destination must have an asset name.");
            }
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || !string.Equals(segment, segment.Trim(), StringComparison.Ordinal))
                {
                    throw new ArgumentException("Destination segments must be nonempty names without traversal or surrounding whitespace.");
                }
            }
            EditorConfigurationService.ValidateDataFolder(DirectoryOf(path));
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) || File.Exists(PhysicalPath(path)) || Directory.Exists(PhysicalPath(path)))
            {
                throw new ArgumentException("The destination already exists: " + path);
            }
        }

        public void Apply() => Run(apply: true);

        public void Advance(double budgetMilliseconds = 4)
        {
            if (double.IsNaN(budgetMilliseconds) || double.IsInfinity(budgetMilliseconds) || budgetMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            }
            Run(budgetMilliseconds: budgetMilliseconds);
        }

        public void Cancel() => Run(cancel: true);

        public void Dispose() => Cancel();

        private bool IsExactSource(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.StartsWith("Assets/", StringComparison.Ordinal)
                && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                && AssetDatabase.GetMainAssetTypeAtPath(path) == _type
                && AssetDatabase.IsOpenForEdit(path);
        }

        private bool IsCurrent(EditorAssetOperationTarget target)
        {
            if (
                _kind != EditorAssetOperationKind.Create
                && (!string.Equals(AssetDatabase.GUIDToAssetPath(target.Guid), target.Path, StringComparison.Ordinal) || !IsExactSource(target.Path))
            )
            {
                return false;
            }
            if (_kind != EditorAssetOperationKind.Delete)
            {
                ValidateDestination(target.DestinationPath);
            }
            return true;
        }

        private void Run(bool apply = false, bool cancel = false, bool exit = false, double budgetMilliseconds = 4)
        {
            if (exit)
            {
                _running = false;
                return;
            }
            if (cancel)
            {
                _cancelRequested = true;
            }
            if (_running || IsFinished)
            {
                return;
            }
            if (_cancelRequested)
            {
                Status = "cancelled";
                Code = "cancelled";
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (apply || Status == "running")
                {
                    Status = "rejected";
                    Code = "play_mode";
                }
                return;
            }
            if (apply && Status == "preview")
            {
                Status = "running";
                Code = "ok";
                return;
            }
            if (Status != "running" || AssetGuidDiscovery.IsAssetDatabaseBusy())
            {
                return;
            }
            using ReusableDisposalLease<EditorAssetOperation> call = _calls.Acquire(this);
            _running = true;
            Stopwatch clock = Stopwatch.StartNew();
            do
            {
                EditorAssetOperationTarget target = _targets[ProcessedCount];
                bool current;
                string readError = null;
                try
                {
                    current = IsCurrent(target);
                }
                catch (ArgumentException)
                {
                    current = false;
                }
                catch (Exception error)
                {
                    current = false;
                    readError = error.Message;
                }
                if (!current)
                {
                    _targets[ProcessedCount] = new EditorAssetOperationTarget(
                        target.Guid,
                        target.Path,
                        target.DestinationPath,
                        code: readError == null ? "stale_target" : "read_failed",
                        detail: readError
                    );
                }
                else
                {
                    try
                    {
                        string resultGuid = Execute(target);
                        _targets[ProcessedCount] = new EditorAssetOperationTarget(target.Guid, target.Path, target.DestinationPath, resultGuid, "applied");
                        SucceededCount++;
                    }
                    catch (Exception error)
                    {
                        _targets[ProcessedCount] = new EditorAssetOperationTarget(target.Guid, target.Path, target.DestinationPath, code: "outcome_unknown", detail: error.Message);
                    }
                }
                ProcessedCount++;
                if (_cancelRequested || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Status = "cancelled";
                    Code = "cancelled";
                    break;
                }
            } while (ProcessedCount < _targets.Count && clock.Elapsed.TotalMilliseconds < budgetMilliseconds);
            LongestSliceMilliseconds = Math.Max(LongestSliceMilliseconds, clock.Elapsed.TotalMilliseconds);
            if (ProcessedCount == _targets.Count)
            {
                Status = SucceededCount == ProcessedCount ? "complete" : "failed";
                Code = Status == "complete" ? "ok" : "partial_failure";
                Detail = Status == "failed" ? "Inspect failed targets and disk before retrying. No rollback is provided." : null;
            }
        }

        private string Execute(EditorAssetOperationTarget target)
        {
            if (_kind == EditorAssetOperationKind.Delete)
            {
                if (!AssetDatabase.DeleteAsset(target.Path))
                {
                    throw new InvalidOperationException("Unity did not confirm deletion.");
                }
                if (File.Exists(PhysicalPath(target.Path)))
                {
                    throw new InvalidOperationException("The deleted path still exists after callbacks.");
                }
                return target.Guid;
            }
            ScriptableObject source = _kind == EditorAssetOperationKind.Create ? null : AssetDatabase.LoadMainAssetAtPath(target.Path) as ScriptableObject;
            if (_kind != EditorAssetOperationKind.Create && (source == null || source.GetType() != _type))
            {
                throw new InvalidOperationException("The exact source could not be loaded.");
            }
            if (_kind == EditorAssetOperationKind.Create || _kind == EditorAssetOperationKind.Clone)
            {
                ScriptableObject instance = _kind == EditorAssetOperationKind.Create ? ScriptableObject.CreateInstance(_type) : Object.Instantiate(source);
                using ReusableDisposalLease<ScriptableObject> temporary = _temporaryObjects.Acquire(instance);
                if (_kind == EditorAssetOperationKind.Create && instance is ICreatable creatable)
                {
                    creatable.BeforeCreate();
                }
                if (_kind == EditorAssetOperationKind.Clone && instance is IDuplicable duplicable)
                {
                    duplicable.BeforeClone(source);
                }
                if (instance == null)
                {
                    throw new InvalidOperationException("A lifecycle callback destroyed the new instance.");
                }
                EnsureCurrent(target);
                AssetDatabase.CreateAsset(instance, target.DestinationPath);
                AssetDatabase.SaveAssetIfDirty(instance);
                if (_kind == EditorAssetOperationKind.Create && instance is ICreatable created)
                {
                    created.AfterCreate();
                }
                if (_kind == EditorAssetOperationKind.Clone && instance is IDuplicable cloned)
                {
                    cloned.AfterClone(source);
                }
                VerifyResult(instance, target.DestinationPath);
                EditorUtility.SetDirty(instance);
                AssetDatabase.SaveAssetIfDirty(instance);
                return VerifyResult(instance, target.DestinationPath);
            }
            string name = Path.GetFileNameWithoutExtension(target.DestinationPath);
            if (_kind == EditorAssetOperationKind.Rename && source is IRenamable renamable)
            {
                renamable.BeforeRename(name);
            }
            if (source == null)
            {
                throw new InvalidOperationException("A lifecycle callback destroyed the source.");
            }
            EnsureCurrent(target);
            string error = _kind == EditorAssetOperationKind.Rename ? AssetDatabase.RenameAsset(target.Path, name) : AssetDatabase.MoveAsset(target.Path, target.DestinationPath);
            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException(error);
            }
            AssetDatabase.SaveAssetIfDirty(source);
            if (_kind == EditorAssetOperationKind.Rename && source is IRenamable renamed)
            {
                renamed.AfterRename(name);
            }
            VerifyResult(source, target.DestinationPath);
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssetIfDirty(source);
            string resultGuid = VerifyResult(source, target.DestinationPath);
            if (!string.Equals(resultGuid, target.Guid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A callback replaced the source identity.");
            }
            return resultGuid;
        }

        private string VerifyResult(ScriptableObject asset, string path)
        {
            if (asset == null || asset.GetType() != _type || !string.Equals(AssetDatabase.GetAssetPath(asset), path, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A callback destroyed or moved the result. Inspect disk before retrying.");
            }
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid) || AssetDatabase.GetMainAssetTypeAtPath(path) != _type)
            {
                throw new InvalidOperationException("The resulting exact main asset is unavailable.");
            }
            return guid;
        }

        private void EnsureCurrent(EditorAssetOperationTarget target)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !IsCurrent(target))
            {
                throw new InvalidOperationException("A callback changed the target, destination or Play Mode state. Inspect this target before retrying.");
            }
        }
    }
}
