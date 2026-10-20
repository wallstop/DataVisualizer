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
        public static string CaptureDocumentation(string outputPath)
        {
            using TestCleanupScope cleanup = new();
            DataVisualizer owner = ScriptableObject.CreateInstance<DataVisualizer>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(owner));
            var original = owner.CopySavedSearches();
            cleanup.Defer(() => owner.SaveSearches(original));
            owner.SaveSearches(new());
            SavedAssetSearchWindow window = SavedAssetSearchWindow.Open(
                owner,
                new SavedAssetSearch
                {
                    query = "Armor",
                    typeIdentity = typeof(AssetValidationTestData).AssemblyQualifiedName,
                    labels = new TypeLabelFilterConfig
                    {
                        andLabels = new() { "Reviewed", "Urgent" },
                    },
                }
            );
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(100, 100, 660, 520);
            window.rootVisualElement.Q<TextField>("saved-search-name").value = "Reviewed urgent armor";
            window.SaveCurrent();
            window.rootVisualElement.Q<DropdownField>("saved-search-list").index = 0;
            EditorSurfaceCapture.SettleLayout(window);
            return EditorSurfaceCapture.Capture(window, outputPath).OutputPath;
        }

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
        public void ShouldCreateRenameDeleteAndRejectDuplicateNamesInTheWindow()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizer owner = ScriptableObject.CreateInstance<DataVisualizer>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(owner));
            var original = owner.CopySavedSearches();
            cleanup.Defer(() => owner.SaveSearches(original));
            owner.SaveSearches(new());
            SavedAssetSearchWindow window = SavedAssetSearchWindow.Open(
                owner,
                new SavedAssetSearch { query = "needle", typeIdentity = typeof(TestDataObject).AssemblyQualifiedName }
            );
            cleanup.Defer(window.Close);
            TextField name = window.rootVisualElement.Q<TextField>("saved-search-name");
            name.value = "One";
            window.SaveCurrent();
            Assert.That(owner.CopySavedSearches().Count, Is.EqualTo(1));
            window.rootVisualElement.Q<DropdownField>("saved-search-list").index = 0;
            window.SaveCurrent();
            Assert.That(owner.CopySavedSearches().Count, Is.EqualTo(1));
            name.value = "Two";
            window.RenameSelected();
            Assert.That(owner.CopySavedSearches()[0].name, Is.EqualTo("Two"));
            Assert.That(owner.CopySavedSearches()[0].query, Is.EqualTo("needle"));
            name.value = "Three";
            window.SaveCurrent();
            Assert.That(window.rootVisualElement.Q<DropdownField>("saved-search-list").index, Is.EqualTo(1));
            Assert.That(name.value, Is.EqualTo("Three"));
            window.DeleteSelected();
            Assert.That(owner.CopySavedSearches().Count, Is.EqualTo(1));
            Assert.That(owner.CopySavedSearches()[0].name, Is.EqualTo("Two"));
            window.rootVisualElement.Q<DropdownField>("saved-search-list").index = 0;
            window.DeleteSelected();
            Assert.That(owner.CopySavedSearches(), Is.Empty);
        }
    }
}
