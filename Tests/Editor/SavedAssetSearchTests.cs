namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class SavedAssetSearchTests
    {
        [Test]
        public void ShouldMigrateSnapshotsBothWaysWithoutSharingMutableLabels()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            DataVisualizerUserState source = new();
            source.savedSearches.Add(
                new SavedAssetSearch
                {
                    name = "Localized 搜索",
                    query = "x\ty z",
                    typeIdentity = typeof(TestDataObject).AssemblyQualifiedName,
                    labels = new TypeLabelFilterConfig
                    {
                        andLabels = new() { "A" },
                        orLabels = new() { "B" },
                        combinationType = LabelCombinationType.Or,
                    },
                }
            );
            settings.HydrateFrom(source);
            source.savedSearches[0].labels.andLabels.Clear();
            DataVisualizerUserState target = new();
            target.HydrateFrom(settings);
            DataVisualizerUserState restored = DataVisualizerUserState.FromJson(JsonUtility.ToJson(target));
            Assert.That(restored.savedSearches[0].labels.andLabels, Is.EqualTo(new[] { "A" }));
            Assert.That(restored.savedSearches[0].labels.orLabels, Is.EqualTo(new[] { "B" }));
            Assert.That(restored.savedSearches[0].labels.combinationType, Is.EqualTo(LabelCombinationType.Or));
            Assert.That(restored.savedSearches[0].query, Is.EqualTo("x\ty z"));
            Assert.That(restored.savedSearches[0].typeIdentity, Is.EqualTo(typeof(TestDataObject).AssemblyQualifiedName));
            Assert.That(restored.savedSearches[0].name, Is.EqualTo("Localized 搜索"));
            target.savedSearches[0].labels.orLabels.Clear();
            DataVisualizerUserState independent = new();
            independent.HydrateFrom(settings);
            Assert.That(independent.savedSearches[0].labels.orLabels, Is.EqualTo(new[] { "B" }));
            DataVisualizerUserState legacy = DataVisualizerUserState.FromJson("{}");
            settings.HydrateFrom(legacy);
            target.HydrateFrom(settings);
            Assert.That(target.savedSearches, Is.Empty);
        }

        [TestCase("Missing.Type, Missing.Assembly", 1)]
        [TestCase("", 1)]
        [TestCase("System.String, mscorlib", 1)]
        [TestCase("", 2)]
        public void ShouldRejectUnavailableScopesWithoutBroadening(string identity, int version)
        {
            SavedAssetSearch entry = new() { typeIdentity = identity, version = version };
            Assert.Throws<InvalidOperationException>(() => entry.StartQuery());
        }

        [Test]
        public void ShouldApplyRoundTrippedSearchWithExactScopeWithoutChangingAssets()
        {
            using TestCleanupScope cleanup = new();
            string folder = "Assets/SavedSearchTests-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            EditorOnlyCreationData asset = ScriptableObject.CreateInstance<EditorOnlyCreationData>();
            AssetDatabase.CreateAsset(asset, folder + "/Needle.asset");
            AssetDatabase.SetLabels(asset, new[] { "A" });
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<DerivedEditorOnlyCreationData>(), folder + "/Derived.asset");
            AssetDatabase.SaveAssets();
            byte[] before = File.ReadAllBytes(folder + "/Needle.asset");
            SavedAssetSearch entry = new()
            {
                query = "absent Needle",
                typeIdentity = typeof(EditorOnlyCreationData).AssemblyQualifiedName,
                labels = new TypeLabelFilterConfig
                {
                    andLabels = new() { "A" },
                    combinationType = LabelCombinationType.Or,
                },
            };
            entry = JsonUtility.FromJson<SavedAssetSearch>(JsonUtility.ToJson(entry));
            using EditorAssetQuery query = entry.StartQuery();
            Assert.That(query.IsComplete, Is.False);
            for (int i = 0; i < 10000 && !query.IsComplete; i++)
            {
                query.Advance(10);
            }
            Assert.That(query.IsComplete, Is.True);
            Assert.That(query.ResultCount, Is.EqualTo(1));
            Assert.That(query.GetPage(0, 10)[0].Guid, Is.EqualTo(AssetDatabase.AssetPathToGUID(folder + "/Needle.asset")));
            Assert.That(File.ReadAllBytes(folder + "/Needle.asset"), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(asset), Is.False);
        }

        [Test]
        public void ShouldSaveOnlySettingsWhenSearchesUseProjectPersistence()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizer owner = ScriptableObject.CreateInstance<DataVisualizer>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(owner));
            owner.CopySavedSearches();
            string[] settingsGuids = AssetDatabase.FindAssets("t:" + nameof(DataVisualizerSettings));
            DataVisualizerSettings settings = AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(AssetDatabase.GUIDToAssetPath(settingsGuids[0]));
            string original = JsonUtility.ToJson(settings);
            cleanup.Defer(() =>
            {
                JsonUtility.FromJsonOverwrite(original, settings);
                settings.MarkDirty();
                AssetDatabase.SaveAssetIfDirty(settings);
            });
            settings.persistStateInSettingsAsset = true;
            string path = "Assets/SavedSearchUnsaved-" + Guid.NewGuid().ToString("N") + ".asset";
            cleanup.Defer(() => AssetDatabase.DeleteAsset(path));
            EditorOnlyCreationData asset = ScriptableObject.CreateInstance<EditorOnlyCreationData>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssetIfDirty(asset);
            byte[] before = File.ReadAllBytes(path);
            asset.name = "Unsaved user change";
            EditorUtility.SetDirty(asset);
            owner.SaveSearches(
                new()
                {
                    new SavedAssetSearch { name = "Project", typeIdentity = typeof(TestDataObject).AssemblyQualifiedName },
                }
            );
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(asset), Is.True);
        }

        [Test]
        public void ShouldHostToolsInTheBrowserAndRestoreItsControlsOnClose()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizer owner = ScriptableObject.CreateInstance<DataVisualizer>();
            cleanup.Defer(owner.Close);
            owner.Show();
            owner.CreateGUI();
            var original = owner.CopySavedSearches();
            cleanup.Defer(() => owner.SaveSearches(original));
            owner.SaveSearches(
                new()
                {
                    new SavedAssetSearch { name = "Waiting", typeIdentity = typeof(TestDataObject).AssemblyQualifiedName },
                }
            );
            Func<bool> busy = AssetGuidDiscovery.AssetDatabaseBusyOverride;
            cleanup.Defer(() => AssetGuidDiscovery.AssetDatabaseBusyOverride = busy);
            int windowCount = Resources.FindObjectsOfTypeAll<EditorWindow>().Length;
            TextField search = owner.rootVisualElement.Q<TextField>("global-search-field");
            search.value = "Retained query";
            SavedAssetSearchPanel first = owner.OpenSavedSearches();
            Assert.That(first.panel, Is.SameAs(owner.rootVisualElement.panel));
            Assert.That(first.ClassListContains("asset-tool-panel"), Is.True);
            Assert.That(first.Q<Label>("saved-search-scope").text, Does.Contain("Retained query"));
            Assert.That(Resources.FindObjectsOfTypeAll<EditorWindow>().Length, Is.EqualTo(windowCount));
            first.Q<DropdownField>("saved-search-list").index = 0;
            AssetGuidDiscovery.AssetDatabaseBusyOverride = () => true;
            first.ApplySelected();
            Label oldStatus = first.Q<Label>("saved-search-status");
            Assert.That(oldStatus.text, Does.StartWith("Waiting for editor"));
            string waiting = oldStatus.text;
            owner.OpenAssetValidation();
            AssetGuidDiscovery.AssetDatabaseBusyOverride = () => false;
            first.AdvanceQuery();
            Assert.That(oldStatus.text, Is.EqualTo(waiting));
            first.Dispose();
            Assert.That(first.parent == null);
            Assert.That(owner.rootVisualElement.Query<AssetToolPanel>().ToList().Count, Is.EqualTo(1));
            Assert.That(owner.rootVisualElement.Q<AssetValidationPanel>().panel, Is.SameAs(owner.rootVisualElement.panel));
            using (KeyDownEvent escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            {
                escape.target = owner.rootVisualElement.Q<AssetValidationPanel>();
                owner.rootVisualElement.Q<AssetValidationPanel>().SendEvent(escape);
            }
            Assert.That(owner.rootVisualElement.Q<AssetToolPanel>() == null);
            owner.CloseAssetTool();
            Assert.That(search.value, Is.EqualTo("Retained query"));
            owner.OpenSavedSearches();
            Button back = owner.rootVisualElement.Q<Button>("asset-tool-back");
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = back;
                back.SendEvent(submit);
            }
            Assert.That(owner.rootVisualElement.Q<AssetToolPanel>() == null);
            owner.OpenSavedSearches();
            owner.CreateGUI();
            Assert.That(owner.rootVisualElement.Q<AssetToolPanel>() == null);
        }

        [Test]
        public void ShouldCreateRenameDeleteAndRejectDuplicateNamesInTheWorkspace()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizer owner = ScriptableObject.CreateInstance<DataVisualizer>();
            cleanup.Defer(owner.Close);
            owner.Show();
            owner.CreateGUI();
            Assert.That(owner.CaptureSearch().query, Is.Empty);
            TextField query = owner.rootVisualElement.Q<TextField>("global-search-field");
            query.value = "literal";
            query.value = "Search...";
            owner.OpenTypeSearchPopover();
            Assert.That(owner.CaptureSearch().query, Is.EqualTo("Search..."));
            var original = owner.CopySavedSearches();
            cleanup.Defer(() => owner.SaveSearches(original));
            owner.SaveSearches(new());
            SavedAssetSearchPanel workspace = new(owner, new SavedAssetSearch { query = "needle", typeIdentity = typeof(TestDataObject).AssemblyQualifiedName });
            cleanup.Defer(workspace.Dispose);
            owner.rootVisualElement.Add(workspace);
            TextField name = workspace.Q<TextField>("saved-search-name");
            name.value = "One";
            workspace.SaveCurrent();
            Assert.That(owner.CopySavedSearches().Count, Is.EqualTo(1));
            workspace.SaveCurrent();
            Assert.That(owner.CopySavedSearches().Count, Is.EqualTo(1));
            name.value = "Two";
            workspace.RenameSelected();
            Assert.That(owner.CopySavedSearches()[0].name, Is.EqualTo("Two"));
            Assert.That(owner.CopySavedSearches()[0].query, Is.EqualTo("needle"));
            name.value = "Three";
            workspace.SaveCurrent();
            Assert.That(workspace.Q<DropdownField>("saved-search-list").index, Is.EqualTo(1));
            Assert.That(name.value, Is.EqualTo("Three"));
            workspace.DeleteSelected();
            Assert.That(owner.CopySavedSearches().Count, Is.EqualTo(1));
            Assert.That(owner.CopySavedSearches()[0].name, Is.EqualTo("Two"));
            workspace.Q<DropdownField>("saved-search-list").index = 0;
            workspace.DeleteSelected();
            Assert.That(owner.CopySavedSearches(), Is.Empty);
        }
    }
}
