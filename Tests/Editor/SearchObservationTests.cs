namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
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

        private static IEnumerator CheckCatalogArrowPersistence(bool persistInSettings)
        {
            using TestCleanupScope cleanup = new();
            PreserveHostState(cleanup);
            DataVisualizerWindow window = CreateTestWindow(cleanup);
            DataVisualizerSettings settings = AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(
                AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:" + nameof(DataVisualizerSettings))[0])
            );
            settings.persistStateInSettingsAsset = persistInSettings;
            string statePath = Path.Combine(Application.persistentDataPath, "DataVisualizerUserState.json");
            foreach (bool namespaceMove in new[] { true, false })
            {
                foreach (bool toTop in new[] { true, false })
                {
                    yield return null;
                    VisualElement namespaces = window.rootVisualElement.Q<VisualElement>("namespace-list");
                    VisualElement container = namespaceMove
                        ? namespaces
                        : namespaces
                            .Children()
                            .Select(group => group.Children().FirstOrDefault(child => child.name.StartsWith("types-container-", StringComparison.Ordinal)))
                            .FirstOrDefault(types => types != null && 1 < types.childCount);
                    Assert.That(container != null, "The test needs at least two catalog entries");
                    Assert.That(container.childCount, Is.GreaterThan(1));
                    VisualElement moved = container[toTop ? container.childCount - 1 : 0];
                    object identity = moved.userData;
                    string namespaceKey = namespaceMove ? null : (string)container.userData;
                    Button button = moved.Q<Button>(toTop ? "go-up-button" : "go-down-button");
                    using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
                    {
                        submit.target = button;
                        button.SendEvent(submit);
                    }
                    Assert.That(container[toTop ? 0 : container.childCount - 1].userData, Is.EqualTo(identity));
                    List<string> expected = container.Children().Select(child => namespaceMove ? (string)child.userData : ((Type)child.userData).FullName).ToList();
                    DataVisualizerUserState saved = persistInSettings ? new DataVisualizerUserState() : DataVisualizerUserState.FromJson(File.ReadAllText(statePath));
                    if (persistInSettings)
                    {
                        saved.HydrateFrom(settings);
                    }
                    Assert.That(namespaceMove ? saved.namespaceOrder : saved.typeOrders.Find(entry => entry.namespaceKey == namespaceKey).typeNames, Is.EqualTo(expected));
                    EditorSurfaceCapture.CloseWindow(window);
                    window = CreateTestWindow(cleanup);
                    yield return null;
                    VisualElement reopened = window.rootVisualElement.Q<VisualElement>(namespaceMove ? "namespace-list" : "types-container-" + namespaceKey);
                    Assert.That(
                        reopened.Children().Select(child => namespaceMove ? (string)child.userData : ((Type)child.userData).FullName),
                        Is.EqualTo(expected),
                        "Arrow order must survive a window reopen"
                    );
                }
            }
        }

        private static IEnumerator CheckKeyboardNamespaceCollapse(bool persistInSettings)
        {
            using TestCleanupScope cleanup = new();
            PreserveHostState(cleanup);
            DataVisualizerWindow window = CreateTestWindow(cleanup);
            DataVisualizerSettings settings = AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(
                AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:" + nameof(DataVisualizerSettings))[0])
            );
            settings.persistStateInSettingsAsset = persistInSettings;
            yield return null;
            VisualElement group = window
                .rootVisualElement.Q("namespace-list")
                .Children()
                .First(candidate => !candidate.Query<VisualElement>(className: "type-item").ToList().Any(item => item.ClassListContains("selected")));
            string key = (string)group.userData;
            Button toggle = group.Q<Button>("namespace-indicator-" + key);
            Assert.That(toggle != null, "Namespace collapse must expose a focusable button");
            Assert.That(toggle.focusable, Is.True);
            VisualElement types = group.Q("types-container-" + key);
            VisualElement selected = window.rootVisualElement.Query<VisualElement>(className: "type-item").ToList().FirstOrDefault(item => item.ClassListContains("selected"));
            int namespacePointerEvents = 0;
            group.RegisterCallback<PointerDownEvent>(_ => namespacePointerEvents++);
            using (PointerDownEvent pointer = PointerDownEvent.GetPooled())
            {
                toggle.SendEvent(pointer);
            }
            Assert.That(namespacePointerEvents, Is.Zero, "Collapse must not begin a namespace drag");
            foreach (KeyCode code in new[] { KeyCode.LeftArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.RightArrow })
            {
                toggle.Focus();
                using KeyDownEvent keyEvent = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code });
                keyEvent.target = toggle;
                toggle.SendEvent(keyEvent);
                Assert.That(types.style.display.value, Is.EqualTo(code == KeyCode.LeftArrow ? DisplayStyle.None : DisplayStyle.Flex));
                Assert.That(selected == null || selected.ClassListContains("selected"), Is.True, "Collapsing must preserve the selected type");
                Assert.That(window.rootVisualElement.focusController.focusedElement, Is.EqualTo(toggle));
            }
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = toggle;
                toggle.SendEvent(submit);
            }
            Assert.That(types.style.display.value, Is.EqualTo(DisplayStyle.None), "Submit must toggle once");
            string statePath = Path.Combine(Application.persistentDataPath, "DataVisualizerUserState.json");
            DataVisualizerUserState saved = persistInSettings ? new DataVisualizerUserState() : DataVisualizerUserState.FromJson(File.ReadAllText(statePath));
            if (persistInSettings)
            {
                saved.HydrateFrom(settings);
            }
            Assert.That(saved.namespaceCollapseStates.Find(entry => entry.namespaceKey == key).isCollapsed, Is.True);
            EditorSurfaceCapture.CloseWindow(window);
            window = CreateTestWindow(cleanup);
            yield return null;
            Assert.That(window.rootVisualElement.Q("types-container-" + key).style.display.value, Is.EqualTo(DisplayStyle.None));
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
        public IEnumerator ShouldCollapseNamespacesByKeyboardInUserState()
        {
            return CheckKeyboardNamespaceCollapse(false);
        }

        [UnityTest]
        public IEnumerator ShouldCollapseNamespacesByKeyboardInSettings()
        {
            return CheckKeyboardNamespaceCollapse(true);
        }

        [UnityTest]
        public IEnumerator ShouldPersistCatalogArrowsInUserState()
        {
            return CheckCatalogArrowPersistence(false);
        }

        [UnityTest]
        public IEnumerator ShouldPersistCatalogArrowsInSettings()
        {
            return CheckCatalogArrowPersistence(true);
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
        public IEnumerator ShouldKeepTheQueryGenerationWhenRefreshHasNoNewMetadata()
        {
            using TestCleanupScope cleanup = new();
            PreserveHostState(cleanup);
            DataVisualizerWindow window = CreateTestWindow(cleanup);
            for (int tick = 0; tick < 1000 && !window.IsSearchIndexComplete; tick++)
            {
                yield return null;
            }
            Assert.That(window.IsSearchIndexComplete, Is.True);
            TextField field = window.rootVisualElement.Q<TextField>("global-search-field");
            field.value = "AbsentUnchangedSnapshot" + Guid.NewGuid().ToString("N");
            int generation = window.ActiveSearchGeneration;
            window.RefreshActiveSearch();
            Assert.That(window.ActiveSearchGeneration, Is.EqualTo(generation), "An unchanged in-flight snapshot needs no follow-up query");
            for (int tick = 0; tick < 1000 && !window.IsActiveSearchComplete; tick++)
            {
                window.AdvanceSearchQuery(0);
                yield return null;
            }
            Assert.That(window.IsActiveSearchComplete, Is.True);
            Assert.That(window.ActiveSearchGeneration, Is.EqualTo(generation));
            for (int refresh = 0; refresh < 10; refresh++)
            {
                window.RefreshActiveSearch();
                window.AdvanceSearchQuery(0);
                Assert.That(window.ActiveSearchGeneration, Is.EqualTo(generation), "An unchanged completed snapshot must stay published");
                Assert.That(window.IsActiveSearchComplete, Is.True);
            }
            string marker = field.value;
            string folder = "Assets/UnchangedSearch" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<OperationLifecycleData>(), folder + "/" + marker + ".asset");
            for (int tick = 0; tick < 1000 && (!window.IsActiveSearchComplete || window.ActiveSearchMatchCount != 1); tick++)
            {
                window.AdvanceSearchQuery();
                yield return null;
            }
            Assert.That(window.IsActiveSearchComplete, Is.True);
            Assert.That(window.ActiveSearchMatchCount, Is.EqualTo(1), "New metadata must refresh an already completed query");
            Assert.That(generation < window.ActiveSearchGeneration);
            generation = window.ActiveSearchGeneration;
            window.RefreshActiveSearch(discardInFlight: true);
            Assert.That(generation < window.ActiveSearchGeneration, "Explicit invalidation must still replace the query");
            field.value = string.Empty;
            generation = window.ActiveSearchGeneration;
            window.RefreshActiveSearch(discardInFlight: true);
            Assert.That(window.ActiveSearchText == null);
            Assert.That(window.ActiveSearchGeneration, Is.EqualTo(generation));
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
