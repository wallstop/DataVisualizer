namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorAssetSelectionTests
    {
        private static ScriptableObject[] CreateAssets(TestCleanupScope cleanup)
        {
            string folder = "Assets/ProcessorSelection-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            ScriptableObject[] assets = new ScriptableObject[3];
            for (int i = 0; i < assets.Length; i++)
            {
                assets[i] = ScriptableObject.CreateInstance<TestDataObject>();
                AssetDatabase.CreateAsset(assets[i], folder + "/" + i + ".asset");
            }
            AssetDatabase.SaveAssets();
            return assets;
        }

        [Test]
        public void ShouldRetainHiddenSelectionAcrossSerializationAndRestoreCanonicalOrder()
        {
            using TestCleanupScope cleanup = new();
            ScriptableObject[] assets = CreateAssets(cleanup);
            EditorAssetSelection selection = new();
            selection.SetType(typeof(TestDataObject));
            selection.ReplaceVisible(assets, new[] { assets[0], assets[2] });
            selection.ReplaceVisible(new[] { assets[1], assets[2] }, new[] { assets[1], assets[2] });
            Assert.That(selection.Count, Is.EqualTo(3), "hidden first asset remains explicit");
            EditorAssetSelection restored = JsonUtility.FromJson<EditorAssetSelection>(JsonUtility.ToJson(selection));
            Assert.That(restored.GetVisibleIndices(assets), Is.EqualTo(new[] { 0, 1, 2 }));
            AssetObjectCache cache = new(new List<ScriptableObject>());
            foreach (ScriptableObject asset in assets)
            {
                cache.Upsert(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)), asset, sorted: false);
            }
            Assert.That(restored.GetVisibleIndices(assets, cache), Is.EqualTo(new[] { 0, 1, 2 }));
            ScriptableObject[] reordered = { assets[2], assets[0], assets[1] };
            string[] guids = restored.CopyGuids(reordered, cache);
            for (int i = 0; i < guids.Length; i++)
            {
                Assert.That(guids[i], Is.EqualTo(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(reordered[i]))));
            }
            restored.SetType(typeof(EditorOnlyCreationData));
            Assert.That(restored.Count, Is.Zero, "type changes cannot retain another type's selection");
        }

        [Test]
        public void ShouldPreserveAnExplicitEmptySelectionAcrossSerializationAndSameTypeReload()
        {
            EditorAssetSelection selection = new();
            selection.SetType(typeof(TestDataObject));
            Assert.That(selection.HasSelectionIntent, Is.False);
            selection.Clear();
            EditorAssetSelection restored = JsonUtility.FromJson<EditorAssetSelection>(JsonUtility.ToJson(selection));
            restored.SetType(typeof(TestDataObject));
            Assert.That(restored.Count, Is.Zero);
            Assert.That(restored.HasSelectionIntent, Is.True);
            restored.SetType(typeof(EditorOnlyCreationData));
            Assert.That(restored.HasSelectionIntent, Is.False);
        }

        [Test]
        public void ShouldKeepDeletedIdentitiesAndRejectInsteadOfBroadeningScope()
        {
            using TestCleanupScope cleanup = new();
            ScriptableObject[] assets = CreateAssets(cleanup);
            EditorAssetSelection selection = new();
            selection.SetType(typeof(TestDataObject));
            selection.ReplaceVisible(assets, new[] { assets[0], assets[2] });
            string deleted = AssetDatabase.GetAssetPath(assets[2]);
            AssetDatabase.DeleteAsset(deleted);
            Assert.That(selection.CopyGuids(new[] { assets[0], assets[1] }).Length, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => new EditorProcessorOperation(new SelectionProbeProcessor<int>(), typeof(TestDataObject), selection.CopyGuids(assets)));
            selection.Clear();
            Assert.That(selection.Count, Is.Zero);
        }

        [Test]
        public void ShouldSyncSameActiveObjectWithoutMovingTheViewport()
        {
            using TestCleanupScope cleanup = new();
            ScriptableObject[] assets = CreateAssets(cleanup);
            DataVisualizer owner = ScriptableObject.CreateInstance<DataVisualizer>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(owner));
            LayoutTestWindow host = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(host.Close);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            List<ScriptableObject> filtered = (List<ScriptableObject>)typeof(DataVisualizer).GetField("_filteredObjects", flags).GetValue(owner);
            filtered.Clear();
            filtered.Add(assets[0]);
            for (int i = 1; i < 100; i++)
            {
                filtered.Add(assets[1]);
            }
            ListView list = new()
            {
                itemsSource = filtered,
                fixedItemHeight = 30,
                selectionType = SelectionType.Multiple,
                makeItem = () => new Label(),
                bindItem = (element, index) => ((Label)element).text = index.ToString(),
                style = { height = 200 },
            };
            host.rootVisualElement.Add(list);
            host.ShowUtility();
            typeof(DataVisualizer).GetField("_objectListView", flags).SetValue(owner, list);
            typeof(DataVisualizer).GetField("_selectedObject", flags).SetValue(owner, assets[0]);
            EditorAssetSelection selection = (EditorAssetSelection)typeof(DataVisualizer).GetField("processorSelection", flags).GetValue(owner);
            selection.SetType(typeof(TestDataObject));
            selection.SetSingle(assets[0]);
            EditorSurfaceCapture.SettleLayout(host);
            list.ScrollToItem(99);
            EditorSurfaceCapture.SettleLayout(host);
            float before = list.Q<ScrollView>().scrollOffset.y;
            Assert.That(before, Is.GreaterThan(0));
            typeof(DataVisualizer).GetMethod("SetActiveObject", flags).Invoke(owner, new object[] { assets[0], true });
            EditorSurfaceCapture.SettleLayout(host);
            Assert.That(list.selectedIndices, Is.EqualTo(new[] { 0 }), "same-object refresh must still synchronize highlights");
            Assert.That(list.Q<ScrollView>().scrollOffset.y, Is.EqualTo(before).Within(1), "background refresh must preserve the viewport");
        }

        [TestCase("Dx", 1180f)]
        [TestCase("Classic", 700f)]
        public void ShouldFreezePreviewWhenSelectionChangesAndCancelWhenPanelDetaches(string themeName, float width)
        {
            using TestCleanupScope cleanup = new();
            ScriptableObject[] assets = CreateAssets(cleanup);
            EditorAssetSelection selection = new();
            selection.SetType(typeof(TestDataObject));
            selection.ReplaceVisible(assets, new[] { assets[0], assets[2] });
            using EditorProcessorOperation operation = new(new SelectionProbeProcessor<int>(), typeof(TestDataObject), selection.CopyGuids(assets));
            selection.SetSingle(assets[1]);
            Assert.That(operation.Targets.Count, Is.EqualTo(2));
            Assert.That(selection.Count, Is.EqualTo(1));
            DataVisualizer owner = ScriptableObject.CreateInstance<DataVisualizer>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(owner));
            owner.ShowUtility();
            owner.CreateGUI();
            owner.position = new Rect(40, 40, width, 780);
            DataVisualizerThemeSelection theme = new();
            cleanup.Defer(() => theme.Apply(null, null));
            theme.Apply(
                owner.rootVisualElement,
                AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>("Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/" + themeName + ".asset")
            );
            ListView list = owner.rootVisualElement.Q<ListView>("object-listview");
            Assert.That(list.selectionType, Is.EqualTo(SelectionType.Multiple));
            ProcessorOperationPanel panel = owner.OpenProcessorPreview(new SelectionProbeProcessor<TestDataObject>(), typeof(TestDataObject), selection.CopyGuids(assets));
            Assert.That(panel.Q<ListView>("processor-targets").itemsSource.Count, Is.EqualTo(1));
            Assert.That(panel.Q<Button>("processor-confirm").enabledSelf, Is.True);
            EditorSurfaceCapture.SettleLayout(owner);
            Button confirm = panel.Q<Button>("processor-confirm");
            Assert.That(0 < confirm.worldBound.height, Is.True);
            Assert.That(confirm.worldBound.xMax <= panel.worldBound.xMax, Is.True);
            TextField target = panel.Q<ListView>("processor-targets").Q<TextField>();
            Assert.That(target != null, Is.True);
            Assert.That(target.isReadOnly, Is.True);
            Assert.That(target.focusable, Is.True);
            panel.RemoveFromHierarchy();
            Assert.That(panel.Operation.Status, Is.EqualTo("cancelled"));
        }

        public sealed class SelectionProbeProcessor<T> : IDataProcessor
        {
            public string Name => "Trim descriptions";
            public string Description => "Explicit selection regression";
            public IEnumerable<Type> Accepts => new[] { typeof(T) };

            public void Process(Type type, IEnumerable<ScriptableObject> objects)
            {
                foreach (ScriptableObject asset in objects)
                {
                    if (asset is BaseDataObject data)
                    {
                        data.Description = data.Description?.Trim();
                        EditorUtility.SetDirty(data);
                    }
                }
            }
        }
    }
}
