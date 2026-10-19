namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Data;

    internal sealed class PerfWindowStateScopeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ShouldDeepCopyTheActiveStateStoreAndDeleteOnlyOwnedFiles(bool persistInSettings)
        {
            string output = "Temp/PerfWindowStateTests-" + Guid.NewGuid().ToString("N");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => Directory.Delete(output, recursive: true));
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            DataVisualizerUserState source = SettingsPersistenceTests.CreatePopulatedUserState();
            settings.HydrateFrom(source);
            settings.persistStateInSettingsAsset = persistInSettings;
            string originalSettings = JsonUtility.ToJson(settings);
            string originalUserState = JsonUtility.ToJson(source);
            DataVisualizerUserState inactive = new() { lastSelectedNamespaceKey = "inactive" };
            using PerfWindowStateScope scope = new(settings, persistInSettings ? inactive : source, output);
            SettingsPersistenceTests.AssertPopulatedUserState(scope.UserState);
            Assert.That(scope.Settings != settings);
            Assert.That(scope.Settings.persistStateInSettingsAsset, Is.False);
            scope.UserState.managedTypeNames.Clear();
            scope.UserState.objectOrders.Clear();
            scope.Settings.themeGuid = "benchmark-only";
            File.WriteAllText(scope.FilePath, JsonUtility.ToJson(scope.UserState));
            string unrelated = Path.Combine(output, "preexisting.json");
            File.WriteAllText(unrelated, "preserve");
            scope.Dispose();
            scope.Dispose();
            Assert.That(scope.Settings == null);
            Assert.That(File.Exists(scope.FilePath), Is.False);
            Assert.AreEqual("preserve", File.ReadAllText(unrelated));
            Assert.AreEqual(originalSettings, JsonUtility.ToJson(settings));
            Assert.AreEqual(originalUserState, JsonUtility.ToJson(source));
        }
    }
}
