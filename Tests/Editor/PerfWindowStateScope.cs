namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Data;

    // Benchmark windows write cloned state to an owned file, never to the host's state stores.
    internal sealed class PerfWindowStateScope : IDisposable
    {
        internal DataVisualizerSettings Settings { get; }
        internal DataVisualizerUserState UserState { get; }
        internal string FilePath { get; }

        private readonly TestCleanupScope _cleanup = new();

        internal PerfWindowStateScope(DataVisualizerSettings settings, DataVisualizerUserState userState, string outputDirectory)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            UserState = DataVisualizerUserState.FromJson(JsonUtility.ToJson(userState ?? new DataVisualizerUserState()));
            if (settings.persistStateInSettingsAsset)
                UserState.HydrateFrom(settings);
            FilePath = Path.GetFullPath(Path.Combine(outputDirectory, "window-state-" + Guid.NewGuid().ToString("N") + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            _cleanup.Defer(() =>
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            });
            _cleanup.Defer(() =>
            {
                if (Settings != null)
                    UnityEngine.Object.DestroyImmediate(Settings);
            });
            Settings = UnityEngine.Object.Instantiate(settings);
            Settings.persistStateInSettingsAsset = false;
        }

        public void Dispose()
        {
            _cleanup.Dispose();
        }
    }
}
