namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Search;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class SearchObservationTests
    {
        private static DataVisualizerWindow CreateTestWindow(TestCleanupScope cleanup)
        {
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            window.CreateGUI();
            return window;
        }

        private static void PreserveHostState(TestCleanupScope cleanup)
        {
            DocsImageCapture.WindowPreferenceSnapshot preferences = DocsImageCapture.WindowPreferenceSnapshot.Capture();
            cleanup.Defer(preferences.Restore);
            UnityEngine.Object[] selection = Selection.objects;
            cleanup.Defer(() => Selection.objects = selection);
            string path = Path.Combine(Application.persistentDataPath, "DataVisualizerUserState.json");
            byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : null;
            cleanup.Defer(() =>
            {
                if (original != null)
                {
                    File.WriteAllBytes(path, original);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            });
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(DataVisualizerSettings)))
            {
                DataVisualizerSettings settings = AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings == null)
                {
                    continue;
                }
                string json = EditorJsonUtility.ToJson(settings);
                cleanup.Defer(() =>
                {
                    if (settings != null)
                    {
                        EditorJsonUtility.FromJsonOverwrite(json, settings);
                        EditorUtility.SetDirty(settings);
                        AssetDatabase.SaveAssetIfDirty(settings);
                    }
                });
            }
        }

        private static IEnumerator CheckMetadataPreview(bool keyboard, bool deleteBeforeActivation)
        {
            using TestCleanupScope cleanup = new();
            PreserveHostState(cleanup);
            string marker = "MetadataPreview" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + marker;
            AssetDatabase.CreateFolder("Assets", marker);
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            OperationLifecycleData asset = ScriptableObject.CreateInstance<OperationLifecycleData>();
            string path = folder + "/" + marker + ".asset";
            AssetDatabase.CreateAsset(asset, path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            DataVisualizerWindow window = CreateTestWindow(cleanup);
            for (int tick = 0; tick < 1000 && !window.IsSearchIndexComplete; tick++)
            {
                yield return null;
            }
            Assert.That(window.IsSearchIndexComplete, Is.True);
            TextField field = window.rootVisualElement.Q<TextField>("global-search-field");
            field.value = marker;
            for (int tick = 0; tick < 1000 && !window.IsActiveSearchComplete; tick++)
            {
                window.AdvanceSearchQuery();
                yield return null;
            }
            Assert.That(window.IsActiveSearchComplete, Is.True);
            Assert.That(window.ActiveSearchPreviewRowCount, Is.EqualTo(1));
            VisualElement row = window.rootVisualElement.Q<VisualElement>("result-item");
            Assert.That(row != null);
            Assert.That(row.userData, Is.InstanceOf<AssetSearchDocument>(), "Quick previews must retain copied text, not Unity objects");
            AssetSearchDocument document = (AssetSearchDocument)row.userData;
            Assert.That(document.Guid, Is.EqualTo(guid));
            Assert.That(document.Name, Is.EqualTo(marker));
            Assert.That(document.ExactType, Is.EqualTo(typeof(OperationLifecycleData)));
            if (deleteBeforeActivation)
            {
                Assert.That(AssetDatabase.DeleteAsset(path));
            }
            else
            {
                Assert.That(AssetDatabase.MoveAsset(path, folder + "/Moved.asset"), Is.Empty);
            }
            if (keyboard)
            {
                using (KeyDownEvent down = KeyDownEvent.GetPooled('\0', KeyCode.DownArrow, EventModifiers.None))
                {
                    down.target = field;
                    field.SendEvent(down);
                }
                Assert.That(row.ClassListContains("search-result-item--highlighted"), Is.True);
                using KeyDownEvent enter = KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None);
                enter.target = field;
                field.SendEvent(enter);
            }
            else
            {
                using PointerDownEvent pointer = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 });
                pointer.target = row;
                row.SendEvent(pointer);
            }
            Assert.That(field.value, Is.EqualTo(deleteBeforeActivation ? marker : string.Empty));
            if (!deleteBeforeActivation)
            {
                Assert.That(window.ActiveSearchText == null);
                Assert.That(document.Guid, Is.EqualTo(AssetDatabase.AssetPathToGUID(folder + "/Moved.asset")));
            }
        }

        [UnityTest]
        public IEnumerator ShouldRenderMetadataPreviewAndActivateMovedAssetByKeyboard()
        {
            return CheckMetadataPreview(keyboard: true, deleteBeforeActivation: false);
        }

        [UnityTest]
        public IEnumerator ShouldRenderMetadataPreviewAndActivateMovedAssetByPointer()
        {
            return CheckMetadataPreview(keyboard: false, deleteBeforeActivation: false);
        }

        [UnityTest]
        public IEnumerator ShouldKeepUnavailableMetadataPreviewOpenWhenAssetIsDeleted()
        {
            return CheckMetadataPreview(keyboard: false, deleteBeforeActivation: true);
        }

        [UnityTest]
        public IEnumerator ShouldObserveZeroMatchesAndRejectClearedOrDismissedQueries()
        {
            using TestCleanupScope cleanup = new();
            PreserveHostState(cleanup);
            DataVisualizerWindow window = CreateTestWindow(cleanup);
            Assert.That(window.IsActiveSearchComplete, Is.False);
            TextField field = window.rootVisualElement.Q<TextField>("global-search-field");
            string absent = Guid.NewGuid().ToString("N");
            field.value = absent;
            for (int tick = 0; tick < 1000 && !window.IsActiveSearchComplete; tick++)
            {
                window.AdvanceSearchQuery();
                yield return null;
            }
            Assert.That(window.IsActiveSearchComplete, Is.True);
            Assert.That(window.ActiveSearchText, Is.EqualTo(absent));
            Assert.That(window.ActiveSearchMatchCount, Is.Zero);
            Assert.That(window.ActiveSearchPreviewRowCount, Is.Zero);
            Assert.That(window.ActiveSearchReachedLimit, Is.False);
            Assert.That(window.ActiveSearchScannedCount, Is.EqualTo(window.ActiveSearchRecordCount));
            int generation = window.ActiveSearchGeneration;
            field.value = string.Empty;
            Assert.That(window.IsActiveSearchComplete, Is.False);
            Assert.That(window.ActiveSearchText == null);
            Assert.That(generation < window.ActiveSearchGeneration);
            field.value = absent;
            for (int tick = 0; tick < 1000 && !window.IsActiveSearchComplete; tick++)
            {
                window.AdvanceSearchQuery();
                yield return null;
            }
            Assert.That(window.IsActiveSearchComplete, Is.True);
            yield return null;
            using (KeyDownEvent escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            {
                escape.target = window.rootVisualElement;
                window.rootVisualElement.SendEvent(escape);
            }
            yield return null;
            Assert.That(window.IsActiveSearchComplete, Is.False);
            Assert.That(window.ActiveSearchText == null);
        }
    }
}
