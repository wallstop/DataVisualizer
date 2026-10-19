namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using Debug = UnityEngine.Debug;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    // Owns one disposable dataset; callers advance it between editor updates.
    internal sealed class PerfFixtureStaging : IDisposable
    {
        internal string Folder { get; }
        internal int TargetCount { get; }
        internal int CreatedCount { get; private set; }
        internal bool IsComplete { get; private set; }
        internal double CreationMilliseconds { get; private set; }
        internal double RefreshMilliseconds { get; private set; }
        internal double TotalMilliseconds => _clock.Elapsed.TotalMilliseconds;
        internal double LongestCreateMilliseconds { get; private set; }
        internal double LongestSliceMilliseconds { get; private set; }

        private readonly Stopwatch _clock = new();
        private string _folderGuid;
        private bool _disposed;
        private bool _markerWritten;

        internal PerfFixtureStaging(int count)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            TargetCount = count;
            Folder = "Assets/PerfBaselineFixtures-" + Guid.NewGuid().ToString("N");
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

            if (budgetMilliseconds <= 0 || maximumItems < 1)
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

            int items = 0;
            while (CreatedCount < TargetCount && items < maximumItems && (items == 0 || slice.Elapsed.TotalMilliseconds < budgetMilliseconds))
            {
                using TestCleanupScope cleanup = new();
                PlayModeDataObject asset = ScriptableObject.CreateInstance<PlayModeDataObject>();
                cleanup.Defer(() =>
                {
                    if (asset != null && !AssetDatabase.Contains(asset))
                    {
                        UnityEngine.Object.DestroyImmediate(asset);
                    }
                });
                asset.name = PerfBaseline.FixtureName(CreatedCount);
                Stopwatch create = Stopwatch.StartNew();
                AssetDatabase.CreateAsset(asset, $"{Folder}/{asset.name}.asset");
                double elapsed = create.Elapsed.TotalMilliseconds;
                CreationMilliseconds += elapsed;
                LongestCreateMilliseconds = Math.Max(LongestCreateMilliseconds, elapsed);
                if (!AssetDatabase.Contains(asset))
                {
                    throw new InvalidOperationException("Fixture creation did not persist an asset: " + asset.name);
                }

                CreatedCount++;
                items++;
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
