namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorStateConfigurationTests
    {
        private static string ClassicGuid => AssetDatabase.AssetPathToGUID("Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/Classic.asset");

        private static (DataVisualizerSettings settings, DataVisualizerUserState state, string settingsPath, string statePath) CreateContext(
            TestCleanupScope cleanup,
            bool projectMode
        )
        {
            string folder = "Assets/StateConfiguration-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() =>
            {
                if (settings != null && !AssetDatabase.Contains(settings))
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
            });
            string settingsPath = folder + "/Settings.asset";
            AssetDatabase.CreateAsset(settings, settingsPath);
            DataVisualizerUserState project = PopulatedState("Project");
            settings.HydrateFrom(project);
            settings.persistStateInSettingsAsset = projectMode;
            AssetDatabase.SaveAssetIfDirty(settings);
            DataVisualizerUserState state = PopulatedState("User");
            string statePath = Path.Combine(Path.GetTempPath(), "StateConfiguration-" + Guid.NewGuid().ToString("N") + ".json");
            cleanup.Defer(() => File.Delete(statePath));
            File.WriteAllText(statePath, JsonUtility.ToJson(state, true));
            return (settings, state, settingsPath, statePath);
        }

        private static DataVisualizerUserState PopulatedState(string name)
        {
            DataVisualizerUserState state = SettingsPersistenceTests.CreatePopulatedUserState();
            state.themeGuid = ClassicGuid;
            state.lastSelectedNamespaceKey = name;
            state.lastSelectedTypeFullName = name + ".Data";
            state.namespaceOrder.Add(name);
            state.objectOrders[0].page = 7;
            state.savedSearches.Add(
                new SavedAssetSearch
                {
                    name = name,
                    query = "text",
                    typeIdentity = typeof(TestDataObject).AssemblyQualifiedName,
                }
            );
            return state;
        }

        private static string Snapshot(DataVisualizerSettings settings)
        {
            DataVisualizerUserState copy = new();
            copy.HydrateFrom(settings);
            return JsonUtility.ToJson(copy);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldMigrateAllStateWithoutAliasingOrSavingUnrelatedAssets(bool projectMode)
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, projectMode);
            string expected = projectMode ? Snapshot(context.settings) : JsonUtility.ToJson(context.state);
            byte[] archive = File.ReadAllBytes(context.statePath);
            TestDataObject unrelated = ScriptableObject.CreateInstance<TestDataObject>();
            cleanup.Defer(() =>
            {
                if (unrelated != null && !AssetDatabase.Contains(unrelated))
                {
                    UnityEngine.Object.DestroyImmediate(unrelated);
                }
            });
            string unrelatedPath = Path.GetDirectoryName(context.settingsPath).Replace('\\', '/') + "/Unrelated.asset";
            AssetDatabase.CreateAsset(unrelated, unrelatedPath);
            AssetDatabase.SaveAssetIfDirty(unrelated);
            byte[] unrelatedBytes = File.ReadAllBytes(unrelatedPath);
            unrelated.name = "Pending unrelated edit";
            EditorUtility.SetDirty(unrelated);
            EditorConfigurationService service = new(context.settings, context.state, context.statePath);
            Assert.That(service.SetPersistenceMode(!projectMode), Is.True);
            Assert.That(service.PersistStateInSettingsAsset, Is.EqualTo(!projectMode));
            Assert.That(projectMode ? JsonUtility.ToJson(context.state) : Snapshot(context.settings), Is.EqualTo(expected));
            Assert.That(EditorUtility.IsDirty(context.settings), Is.False);
            Assert.That(EditorUtility.IsDirty(unrelated), Is.True);
            Assert.That(File.ReadAllBytes(unrelatedPath), Is.EqualTo(unrelatedBytes));
            if (projectMode)
            {
                Assert.That(JsonUtility.ToJson(DataVisualizerUserState.FromJson(File.ReadAllText(context.statePath))), Is.EqualTo(expected));
                context.settings.HydrateFrom(PopulatedState("Changed"));
                Assert.That(JsonUtility.ToJson(context.state), Is.EqualTo(expected));
            }
            else
            {
                Assert.That(File.ReadAllBytes(context.statePath), Is.EqualTo(archive), "Migrating to project state leaves the user archive unchanged.");
                context.state.typeOrders[0].typeNames[0] = "Changed";
                context.state.savedSearches[0].name = "Changed";
                Assert.That(Snapshot(context.settings), Is.EqualTo(expected));
            }
            byte[] settingsBytes = File.ReadAllBytes(context.settingsPath);
            byte[] userBytes = File.ReadAllBytes(context.statePath);
            Assert.That(service.SetPersistenceMode(!projectMode), Is.False);
            Assert.That(File.ReadAllBytes(context.settingsPath), Is.EqualTo(settingsBytes));
            Assert.That(File.ReadAllBytes(context.statePath), Is.EqualTo(userBytes));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldPersistThemeOnlyToTheActiveStoreAndResetToDefault(bool projectMode)
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, projectMode);
            EditorConfigurationService service = new(context.settings, context.state, context.statePath);
            byte[] settingsBytes = File.ReadAllBytes(context.settingsPath);
            byte[] userBytes = File.ReadAllBytes(context.statePath);
            Assert.That(service.SetThemeGuid(DataVisualizerThemeSelection.DefaultThemeGuid.ToUpperInvariant()), Is.True);
            Assert.That(service.ThemeGuid, Is.EqualTo(DataVisualizerThemeSelection.DefaultThemeGuid));
            Assert.That(projectMode ? context.state.themeGuid : context.settings.themeGuid, Is.EqualTo(ClassicGuid));
            Assert.That(File.ReadAllBytes(projectMode ? context.statePath : context.settingsPath), Is.EqualTo(projectMode ? userBytes : settingsBytes));
            Assert.That(service.SetThemeGuid(DataVisualizerThemeSelection.DefaultThemeGuid), Is.False);
            Assert.That(service.SetThemeGuid(string.Empty), Is.True);
            Assert.That(service.ThemeGuid, Is.Empty);
            Assert.That(DataVisualizerThemeSelection.Resolve(service.ThemeGuid) != null, Is.True);
        }

        [Test]
        public void ShouldApplyAllPreferencesAndMigrationOnceWithoutSavingOtherPendingEdits()
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, false);
            EditorConfigurationService service = new(context.settings, context.state, context.statePath);
            Assert.That(service.Apply("Assets", true, true, DataVisualizerThemeSelection.DefaultThemeGuid), Is.True);
            Assert.That(service.DataFolderPath, Is.EqualTo("Assets"));
            Assert.That(service.SelectActiveObject, Is.True);
            Assert.That(service.PersistStateInSettingsAsset, Is.True);
            Assert.That(service.ThemeGuid, Is.EqualTo(DataVisualizerThemeSelection.DefaultThemeGuid));
            Assert.That(context.state.themeGuid, Is.EqualTo(ClassicGuid));
            byte[] before = File.ReadAllBytes(context.settingsPath);
            context.settings.name = "Pending edit";
            EditorUtility.SetDirty(context.settings);
            Assert.That(service.Apply("Assets", true, true, DataVisualizerThemeSelection.DefaultThemeGuid), Is.False);
            Assert.That(EditorUtility.IsDirty(context.settings), Is.True);
            Assert.That(File.ReadAllBytes(context.settingsPath), Is.EqualTo(before));
        }

        [TestCase(null)]
        [TestCase("invalid")]
        [TestCase("00000000000000000000000000000000")]
        public void ShouldRejectInvalidThemesBeforeChangingAnyPreferenceOrStore(string theme)
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, false);
            EditorConfigurationService service = new(context.settings, context.state, context.statePath);
            byte[] settingsBytes = File.ReadAllBytes(context.settingsPath);
            byte[] stateBytes = File.ReadAllBytes(context.statePath);
            Assert.Throws<ArgumentException>(() => service.Apply("Assets", true, true, theme));
            Assert.Throws<ArgumentException>(() => service.SetThemeGuid(AssetDatabase.AssetPathToGUID(context.settingsPath)));
            Assert.That(service.PersistStateInSettingsAsset, Is.False);
            Assert.That(service.SelectActiveObject, Is.False);
            Assert.That(EditorUtility.IsDirty(context.settings), Is.False);
            Assert.That(File.ReadAllBytes(context.settingsPath), Is.EqualTo(settingsBytes));
            Assert.That(File.ReadAllBytes(context.statePath), Is.EqualTo(stateBytes));
        }

        [TestCase("Assets/State.json")]
        [TestCase("assets/State.json")]
        [TestCase("Packages/State.json")]
        [TestCase("ProjectSettings/State.json")]
        [TestCase("State.asset")]
        public void ShouldRejectProtectedOrInvalidUserStatePaths(string path)
        {
            Assert.Throws<ArgumentException>(() => EditorConfigurationService.ValidateUserStatePath(path));
        }

        [Test]
        public void ShouldResolveTheSettingsMainAssetWhenTheFileContainsAnotherSettingsObject()
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, false);
            DataVisualizerSettings sub = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() =>
            {
                if (sub != null && !AssetDatabase.Contains(sub))
                {
                    UnityEngine.Object.DestroyImmediate(sub);
                }
            });
            AssetDatabase.AddObjectToAsset(sub, context.settingsPath);
            AssetDatabase.SetMainObject(context.settings, context.settingsPath);
            EditorUtility.SetDirty(sub);
            AssetDatabase.SaveAssetIfDirty(context.settings);
            EditorConfigurationService service = EditorConfigurationService.FromGuid(AssetDatabase.AssetPathToGUID(context.settingsPath), context.statePath);
            Assert.That(service.SetSelectActiveObject(true), Is.True);
            Assert.That(context.settings.selectActiveObject, Is.True);
            Assert.That(sub.selectActiveObject, Is.False);
        }

        [Test]
        public void ShouldReadExplicitFilesWithoutWritesAndRejectCorruptStateOrAbsentContext()
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, false);
            string guid = AssetDatabase.AssetPathToGUID(context.settingsPath);
            byte[] before = File.ReadAllBytes(context.statePath);
            EditorConfigurationService loaded = EditorConfigurationService.FromGuid(guid, context.statePath);
            Assert.That(loaded.ThemeGuid, Is.EqualTo(ClassicGuid));
            Assert.That(loaded.UserStatePath, Is.EqualTo(Path.GetFullPath(context.statePath)));
            Assert.That(File.ReadAllBytes(context.statePath), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(context.settings), Is.False);
            EditorConfigurationService basic = new(context.settings);
            Assert.Throws<ArgumentException>(() => basic.SetPersistenceMode(true));
            Assert.Throws<ArgumentException>(() => basic.SetThemeGuid(string.Empty));
            File.WriteAllText(context.statePath, "{");
            Assert.Throws<ArgumentException>(() => EditorConfigurationService.FromGuid(guid, context.statePath));
            Assert.That(File.ReadAllText(context.statePath), Is.EqualTo("{"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldRejectReadonlySettingsBeforeChangingEitherStore(bool projectMode)
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, projectMode);
            byte[] settingsBytes = File.ReadAllBytes(context.settingsPath);
            byte[] userBytes = File.ReadAllBytes(context.statePath);
            FileAttributes attributes = File.GetAttributes(context.settingsPath);
            cleanup.Defer(() => File.SetAttributes(context.settingsPath, attributes));
            File.SetAttributes(context.settingsPath, attributes | FileAttributes.ReadOnly);
            EditorConfigurationService service = new(context.settings, context.state, context.statePath);
            EditorConfigurationException preference = Assert.Throws<EditorConfigurationException>(() => service.SetSelectActiveObject(true));
            EditorConfigurationException migration = Assert.Throws<EditorConfigurationException>(() => service.SetPersistenceMode(!projectMode));
            Assert.That(preference.Code, Is.EqualTo("read_only"));
            Assert.That(migration.Code, Is.EqualTo("read_only"));
            Assert.That(service.PersistStateInSettingsAsset, Is.EqualTo(projectMode));
            Assert.That(service.SelectActiveObject, Is.False);
            Assert.That(File.ReadAllBytes(context.settingsPath), Is.EqualTo(settingsBytes));
            Assert.That(File.ReadAllBytes(context.statePath), Is.EqualTo(userBytes));
            Assert.That(EditorUtility.IsDirty(context.settings), Is.False);
        }

        [Test]
        public void ShouldChangeUserThemeWithoutWritingReadonlySharedSettings()
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, false);
            byte[] before = File.ReadAllBytes(context.settingsPath);
            FileAttributes attributes = File.GetAttributes(context.settingsPath);
            cleanup.Defer(() => File.SetAttributes(context.settingsPath, attributes));
            File.SetAttributes(context.settingsPath, attributes | FileAttributes.ReadOnly);
            EditorConfigurationService service = new(context.settings, context.state, context.statePath);
            Assert.That(service.SetThemeGuid(string.Empty), Is.True);
            Assert.That(service.ThemeGuid, Is.Empty);
            Assert.That(DataVisualizerUserState.FromJson(File.ReadAllText(context.statePath)).themeGuid, Is.Empty);
            Assert.That(File.ReadAllBytes(context.settingsPath), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(context.settings), Is.False);
        }

        [Test]
        public void ShouldRejectReadonlyUserStateBeforeMigrationChangesSettingsOrMemory()
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, true);
            EditorConfigurationService service = new(context.settings, context.state, context.statePath);
            string state = JsonUtility.ToJson(context.state);
            byte[] before = File.ReadAllBytes(context.statePath);
            FileAttributes attributes = File.GetAttributes(context.statePath);
            cleanup.Defer(() => File.SetAttributes(context.statePath, attributes));
            File.SetAttributes(context.statePath, attributes | FileAttributes.ReadOnly);
            EditorConfigurationException error = Assert.Throws<EditorConfigurationException>(() => service.SetPersistenceMode(false));
            Assert.That(error.Code, Is.EqualTo("read_only"));
            Assert.That(service.PersistStateInSettingsAsset, Is.True);
            Assert.That(JsonUtility.ToJson(context.state), Is.EqualTo(state));
            Assert.That(File.ReadAllBytes(context.statePath), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(context.settings), Is.False);
        }
    }
}
