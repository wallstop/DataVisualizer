namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorConfigurationTests
    {
        [Test]
        public void ShouldPersistBothPreferencesWithoutSavingUnrelatedDirtyAssets()
        {
            using TestCleanupScope cleanup = new();
            string folder = "Assets/Configuration-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            string path = folder + "/Settings.asset";
            AssetDatabase.CreateAsset(settings, path);
            TestDataObject unrelated = ScriptableObject.CreateInstance<TestDataObject>();
            string unrelatedPath = folder + "/Unrelated.asset";
            AssetDatabase.CreateAsset(unrelated, unrelatedPath);
            AssetDatabase.SaveAssets();
            byte[] unrelatedBytes = File.ReadAllBytes(unrelatedPath);
            unrelated.name = "Unrelated dirty value";
            EditorUtility.SetDirty(unrelated);
            EditorConfigurationService service = EditorConfigurationService.FromGuid(AssetDatabase.AssetPathToGUID(path));
            Assert.That(service.SettingsPath, Is.EqualTo(path));
            Assert.That(service.SettingsGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(path)));

            Assert.That(service.Apply(folder, true), Is.True);
            Assert.That(service.DataFolderPath, Is.EqualTo(folder));
            Assert.That(service.SelectActiveObject, Is.True);
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
            Assert.That(EditorUtility.IsDirty(unrelated), Is.True);
            Assert.That(File.ReadAllBytes(unrelatedPath), Is.EqualTo(unrelatedBytes));
            string saved = File.ReadAllText(path);
            Assert.That(saved, Does.Contain(folder));
            Assert.That(saved, Does.Contain("selectActiveObject: 1"));

            settings.persistStateInSettingsAsset = true;
            EditorUtility.SetDirty(settings);
            Assert.That(service.Apply(folder, true), Is.False);
            Assert.That(EditorUtility.IsDirty(settings), Is.True, "Idempotent apply does not save other pending edits.");
            Assert.That(File.ReadAllText(path), Is.EqualTo(saved));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("AssetsBackup")]
        [TestCase("Packages")]
        [TestCase("Assets/../Assets")]
        [TestCase("Assets/./")]
        [TestCase("Assets//Missing")]
        [TestCase("Assets\\Missing")]
        [TestCase("Assets/Missing-Configuration-Folder")]
        public void ShouldRejectInvalidFoldersBeforeChangingEitherPreference(string path)
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            EditorUtility.ClearDirty(settings);
            EditorConfigurationService service = new(settings);
            Assert.Throws<ArgumentException>(() => service.Apply(path, true));
            Assert.That(service.DataFolderPath, Is.EqualTo(DataVisualizerSettings.DefaultDataFolderPath));
            Assert.That(service.SelectActiveObject, Is.False);
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
        }

        [TestCase(null)]
        [TestCase("invalid")]
        [TestCase("00000000000000000000000000000000")]
        public void ShouldRejectMissingOrInvalidSettingsGuid(string guid)
        {
            Assert.Throws<ArgumentException>(() => EditorConfigurationService.FromGuid(guid));
        }

        [Test]
        public void ShouldShareSelectionPreferenceWithTheWindowWithoutChangingItsFolder()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            EditorConfigurationService service = new(settings);
            string originalPath = service.DataFolderPath;
            Assert.That(DataVisualizer.ApplySelectActiveObjectPreference(settings, true), Is.True);
            Assert.That(service.SelectActiveObject, Is.True);
            Assert.That(service.DataFolderPath, Is.EqualTo(originalPath));
            EditorUtility.ClearDirty(settings);
            Assert.That(service.SetSelectActiveObject(true), Is.False);
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldResolveFolderPickerSpellingBeforeApplyingStrictConfiguration(bool varyCase)
        {
            using TestCleanupScope cleanup = new();
            string leaf = "PickerCase-" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + leaf;
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", leaf);
            string selected = varyCase ? "Assets/" + leaf.ToLowerInvariant() : folder;
            string canonical = AssetsFolderUtility.GetCanonicalAssetsFolderOrNull(selected);
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(selected)))
            {
                Assert.That(canonical == null, "A case-sensitive database must reject a path it cannot resolve.");
                return;
            }
            Assert.That(canonical, Is.EqualTo(folder));
            EditorConfigurationService.ValidateDataFolder(canonical);
            if (varyCase)
            {
                Assert.Throws<ArgumentException>(() => EditorConfigurationService.ValidateDataFolder(selected), "The headless contract stays strict.");
            }
        }

        [Test]
        public void ShouldValidateAnExistingFolderWithoutChangingSettingsOrDirtyState()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            EditorUtility.ClearDirty(settings);
            EditorConfigurationService.ValidateDataFolder("Assets");
            Assert.That(settings.DataFolderPath, Is.EqualTo(DataVisualizerSettings.DefaultDataFolderPath));
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
            EditorConfigurationService service = new(settings);
            UnityEngine.Object.DestroyImmediate(settings);
            Assert.Throws<ObjectDisposedException>(() => service.SetSelectActiveObject(true));
            Assert.Throws<ArgumentNullException>(() => new EditorConfigurationService(settings));
        }
    }
}
