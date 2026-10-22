namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using Debug = UnityEngine.Debug;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    // Owns one disposable dataset; callers advance it between editor updates.
    internal sealed class PerfFixtureStaging : IDisposable
    {
        internal string Folder { get; }
        internal int TargetCount { get; }
        internal IReadOnlyList<Type> FixtureTypes { get; }
        internal int CreatedCount { get; private set; }
        internal bool IsComplete { get; private set; }
        internal double CreationMilliseconds { get; private set; }
        internal double RefreshMilliseconds { get; private set; }
        internal double BatchImportMilliseconds { get; private set; }
        internal double InitializationMilliseconds { get; private set; }
        internal double LongestBatchImportMilliseconds { get; private set; }
        internal double TotalMilliseconds => _clock.Elapsed.TotalMilliseconds;
        internal double LongestCreateMilliseconds { get; private set; }
        internal double LongestSliceMilliseconds { get; private set; }

        private readonly Stopwatch _clock = new();
        private readonly ReusableDisposalScope<PerfFixtureStaging> _importScopes = new(EndImportBatch);
        private readonly Action<ScriptableObject, string> _createAsset;
        private bool _importBatchActive;
        private Exception _importFailure;
        private string _folderGuid;
        private bool _disposed;
        private bool _markerWritten;

        internal PerfFixtureStaging(int count, Action<ScriptableObject, string> createAsset = null, Type[] fixtureTypes = null)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            Type[] types = fixtureTypes == null ? new[] { typeof(PlayModeDataObject) } : (Type[])fixtureTypes.Clone();
            if (types.Length == 0 || count < types.Length)
            {
                throw new ArgumentException("Each fixture type needs at least one asset.", nameof(fixtureTypes));
            }
            HashSet<Type> unique = new();
            foreach (Type type in types)
            {
                if (!EditorTypeCatalog.IsLoadableType(type) || !unique.Add(type))
                {
                    throw new ArgumentException("Use distinct eligible ScriptableObject fixture types.", nameof(fixtureTypes));
                }
            }
            FixtureTypes = Array.AsReadOnly(types);
            _createAsset = createAsset ?? AssetDatabase.CreateAsset;
            TargetCount = count;
            Folder = "Assets/PerfBaselineFixtures-" + Guid.NewGuid().ToString("N");
        }

        private static void EndImportBatch(PerfFixtureStaging staging)
        {
            if (!staging._importBatchActive)
            {
                return;
            }
            staging._importBatchActive = false;
            Stopwatch flush = Stopwatch.StartNew();
            try
            {
                AssetDatabase.StopAssetEditing();
            }
            catch (Exception error)
            {
                staging._importFailure = error;
            }
            double elapsed = flush.Elapsed.TotalMilliseconds;
            staging.BatchImportMilliseconds += elapsed;
            staging.LongestBatchImportMilliseconds = Math.Max(staging.LongestBatchImportMilliseconds, elapsed);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _clock.Stop();
            try
            {
                bool ownsFolder =
                    !string.IsNullOrEmpty(_folderGuid)
                    && string.Equals(AssetDatabase.AssetPathToGUID(Folder, AssetPathToGUIDOptions.OnlyExistingAssets), _folderGuid, StringComparison.Ordinal);
                // Unity may reuse a deleted folder's GUID for a replacement at the same path.
                string marker = Folder + "/.perf-owner";
                bool ownsContents = !_markerWritten || (File.Exists(marker) && string.Equals(File.ReadAllText(marker), Folder, StringComparison.Ordinal));
                if (ownsFolder && ownsContents)
                {
                    if (!AssetDatabase.DeleteAsset(Folder))
                    {
                        Debug.LogError("[PerfBaseline] Could not delete owned fixtures: " + Folder);
                    }
                }
            }
            catch (Exception error)
            {
                Debug.LogError("[PerfBaseline] Fixture cleanup failed: " + error.Message);
            }
        }

        internal void Advance(double budgetMilliseconds = 4, int maximumItems = 10)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PerfFixtureStaging));
            }

            if (double.IsNaN(budgetMilliseconds) || double.IsInfinity(budgetMilliseconds) || budgetMilliseconds <= 0 || maximumItems < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            }

            if (IsComplete)
            {
                return;
            }

            Stopwatch slice = Stopwatch.StartNew();
            if (_folderGuid == null)
            {
                _clock.Start();
                if (AssetDatabase.IsValidFolder(Folder) || System.IO.Directory.Exists(Folder))
                {
                    throw new InvalidOperationException("The fixture folder already exists: " + Folder);
                }

                _folderGuid = AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(Folder));
                if (string.IsNullOrEmpty(_folderGuid))
                {
                    throw new InvalidOperationException("Could not create the fixture folder: " + Folder);
                }

                File.WriteAllText(Folder + "/.perf-owner", Folder);
                _markerWritten = true;
            }

            if (CreatedCount == TargetCount)
            {
                return;
            }
            int firstCreated = CreatedCount;
            int items = 0;
            using (TestCleanupScope cleanup = new())
            {
                using (ReusableDisposalLease<PerfFixtureStaging> import = _importScopes.Acquire(this))
                {
                    // Stop imports in this call, including failures; never hold editing across editor ticks.
                    _importBatchActive = true;
                    AssetDatabase.StartAssetEditing();
                    while (CreatedCount < TargetCount && items < maximumItems && (items == 0 || slice.Elapsed.TotalMilliseconds < budgetMilliseconds))
                    {
                        Stopwatch initializeAsset = Stopwatch.StartNew();
                        Type fixtureType = FixtureTypes[CreatedCount % FixtureTypes.Count];
                        ScriptableObject asset = ScriptableObject.CreateInstance(fixtureType);
                        cleanup.Defer(() =>
                        {
                            if (asset != null && !AssetDatabase.Contains(asset))
                            {
                                UnityEngine.Object.DestroyImmediate(asset);
                            }
                        });
                        asset.name = PerfBaseline.FixtureName(CreatedCount);
                        if (asset is BaseDataObject data)
                        {
                            data.Description = PerfBaseline.FixtureSearchText(CreatedCount);
                            data.BeforeCreate();
                            data._assetGuid = PerfBaseline.FixtureId(CreatedCount);
                        }
                        if (asset is OtherEditorOnlyCreationData ordinary)
                        {
                            ordinary.SearchText = PerfBaseline.FixtureSearchText(CreatedCount);
                        }
                        InitializationMilliseconds += initializeAsset.Elapsed.TotalMilliseconds;
                        Stopwatch create = Stopwatch.StartNew();
                        _createAsset(asset, $"{Folder}/{asset.name}.asset");
                        double elapsed = create.Elapsed.TotalMilliseconds;
                        CreationMilliseconds += elapsed;
                        LongestCreateMilliseconds = Math.Max(LongestCreateMilliseconds, elapsed);
                        CreatedCount++;
                        items++;
                    }
                }
                if (_importFailure != null)
                {
                    throw new InvalidOperationException("The fixture import batch failed.", _importFailure);
                }
                Stopwatch initialization = Stopwatch.StartNew();
                for (int index = firstCreated; index < CreatedCount; index++)
                {
                    string path = $"{Folder}/{PerfBaseline.FixtureName(index)}.asset";
                    ScriptableObject asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                    if (asset == null || asset.GetType() != FixtureTypes[index % FixtureTypes.Count])
                    {
                        throw new InvalidOperationException("Fixture import did not persist the expected type: " + path);
                    }
                    if (asset is BaseDataObject data)
                    {
                        data.AfterCreate();
                        if (!string.Equals(data.Id, PerfBaseline.FixtureId(index), StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Fixture import changed its authored identity: " + path);
                        }
                    }
                }
                InitializationMilliseconds += initialization.Elapsed.TotalMilliseconds;
            }
            LongestSliceMilliseconds = Math.Max(LongestSliceMilliseconds, slice.Elapsed.TotalMilliseconds);
            // Refresh is a separate tick so cancellation can run after the final creation.
        }

        internal void Refresh()
        {
            if (_disposed || CreatedCount != TargetCount)
            {
                throw new InvalidOperationException("Only a fully staged dataset can be refreshed.");
            }

            if (IsComplete)
            {
                return;
            }

            Stopwatch refresh = Stopwatch.StartNew();
            AssetDatabase.Refresh();
            RefreshMilliseconds = refresh.Elapsed.TotalMilliseconds;
            _clock.Stop();
            IsComplete = true;
        }
    }
}
