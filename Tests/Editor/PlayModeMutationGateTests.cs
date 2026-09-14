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
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class PlayModeMutationGateTests
    {
        private static FieldInfo _instanceField;

        private static void EnsureFolderExists(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        private static string CreateFixtureFolder()
        {
            string folder =
                "Assets/DataVisualizerMutationGateTests_" + Guid.NewGuid().ToString("N");
            EnsureFolderExists(folder);
            return folder;
        }

        private static ScriptableObject CreateFixtureAsset(string folder, string assetName)
        {
            string assetPath = folder + "/" + assetName;
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), assetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
        }

        private static object ReadSharedInstance()
        {
            if (_instanceField == null)
            {
                _instanceField = typeof(DataVisualizerWindow).GetField(
                    "Instance",
                    BindingFlags.Static | BindingFlags.NonPublic
                );
            }

            return _instanceField?.GetValue(null);
        }

        private static void RestoreSharedInstance(object previousInstance)
        {
            _instanceField?.SetValue(null, previousInstance);
        }

        private static T ReadPrivateField<T>(DataVisualizerWindow window, string fieldName)
        {
            FieldInfo field = typeof(DataVisualizerWindow).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            Assert.IsNotNull(field, $"The {fieldName} field must exist.");
            return (T)field.GetValue(window);
        }

        private static void SetPrivateField(
            DataVisualizerWindow window,
            string fieldName,
            object value
        )
        {
            FieldInfo field = typeof(DataVisualizerWindow).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            Assert.IsNotNull(field, $"The {fieldName} field must exist.");
            field.SetValue(window, value);
        }

        private static object InvokePrivate(
            DataVisualizerWindow window,
            string methodName,
            params object[] arguments
        )
        {
            MethodInfo method = typeof(DataVisualizerWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            Assert.IsNotNull(method, $"The {methodName} method must exist.");
            return method.Invoke(window, arguments);
        }

        private static void SuspendForPlayMode(DataVisualizerWindow window)
        {
            window.HandlePlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
        }

        private static void ResumeFromPlayMode(DataVisualizerWindow window)
        {
            window.HandlePlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
        }

        [Test]
        public void ShouldGateDeleteWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                ScriptableObject asset = CreateFixtureAsset(folder, "First.asset");
                SetPrivateField(window, "_popoverContext", asset);

                SuspendForPlayMode(window);
                InvokePrivate(window, "HandleDeleteConfirmed");
                Assert.IsNotNull(
                    AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/First.asset"),
                    "a delete confirmed while suspended must not delete the asset"
                );

                ResumeFromPlayMode(window);
                InvokePrivate(window, "HandleDeleteConfirmed");
                Assert.IsNull(
                    AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/First.asset"),
                    "the same delete after resume must delete the asset"
                );
            }
        }

        [Test]
        public void ShouldGateCloneWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                ScriptableObject asset = CreateFixtureAsset(folder, "First.asset");

                SuspendForPlayMode(window);
                InvokePrivate(window, "CloneObject", asset);
                Assert.IsNull(
                    AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                        folder + "/First (Clone).asset"
                    ),
                    "cloning must not create an asset while suspended"
                );

                ResumeFromPlayMode(window);
                InvokePrivate(window, "CloneObject", asset);
                Assert.IsNotNull(
                    AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                        folder + "/First (Clone).asset"
                    ),
                    "cloning after resume must create the clone"
                );
            }
        }

        [Test]
        public void ShouldGateRenameWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                CreateFixtureAsset(folder, "First.asset");
                SetPrivateField(window, "_popoverContext", folder + "/First.asset");

                SuspendForPlayMode(window);
                InvokePrivate(
                    window,
                    "HandleRenameConfirmed",
                    new Label(),
                    new TextField { value = "Renamed" },
                    new Label()
                );
                Assert.IsNotNull(
                    AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/First.asset"),
                    "a rename confirmed while suspended must not rename the asset"
                );

                ResumeFromPlayMode(window);
                InvokePrivate(
                    window,
                    "HandleRenameConfirmed",
                    new Label(),
                    new TextField { value = "Renamed" },
                    new Label()
                );
                Assert.IsNull(
                    AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/First.asset"),
                    "the same rename after resume must rename the asset"
                );
                Assert.IsNotNull(
                    AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/Renamed.asset"),
                    "the rename after resume must land at the new path"
                );
            }
        }

        [Test]
        public void ShouldGateLabelRemovalWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                ScriptableObject asset = CreateFixtureAsset(folder, "First.asset");
                AssetDatabase.SetLabels(asset, new[] { "GateProbe" });
                SetPrivateField(window, "_selectedObject", asset);

                SuspendForPlayMode(window);
                InvokePrivate(window, "RemoveLabelFromSelectedAsset", "GateProbe");
                CollectionAssert.Contains(
                    AssetDatabase.GetLabels(asset),
                    "GateProbe",
                    "a label removal while suspended must not change the asset labels"
                );

                ResumeFromPlayMode(window);
                InvokePrivate(window, "RemoveLabelFromSelectedAsset", "GateProbe");
                CollectionAssert.DoesNotContain(
                    AssetDatabase.GetLabels(asset),
                    "GateProbe",
                    "the same label removal after resume must remove the label"
                );
            }
        }

        [Test]
        public void ShouldGateManagedTypeAddWhileSuspendedForPlayMode()
        {
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                Dictionary<string, List<Type>> managedTypes = ReadPrivateField<
                    Dictionary<string, List<Type>>
                >(window, "_scriptableObjectTypes");
                Assert.AreEqual(0, managedTypes.Count);

                List<Type> typesToAdd = new() { typeof(TestDataObject) };

                SuspendForPlayMode(window);
                Assert.IsFalse(
                    (bool)InvokePrivate(window, "AddManagedTypes", typesToAdd),
                    "a managed-type add while suspended must not change the catalog"
                );
                Assert.AreEqual(0, managedTypes.Count);

                ResumeFromPlayMode(window);
                Assert.IsTrue(
                    (bool)InvokePrivate(window, "AddManagedTypes", typesToAdd),
                    "the same managed-type add after resume must change the catalog"
                );
                Assert.AreEqual(1, managedTypes.Count);
            }
        }

        [Test]
        public void ShouldGateTypeSwitchWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                CreateFixtureAsset(folder, "First.asset");
                NamespaceController controller = ReadPrivateField<NamespaceController>(
                    window,
                    "_namespaceController"
                );

                /*
                    SelectType resolves the type's view element before selecting, so the resume
                    control needs the namespace view built; AddManagedTypes seeds the catalog and
                    BuildNamespaceView populates the type-element cache.
                */
                Assert.IsTrue(
                    (bool)InvokePrivate(
                        window,
                        "AddManagedTypes",
                        new List<Type> { typeof(TestDataObject) }
                    )
                );
                InvokePrivate(window, "BuildNamespaceView");

                SuspendForPlayMode(window);
                controller.SelectType(window, typeof(TestDataObject));
                Assert.IsNull(
                    controller.SelectedType,
                    "a type switch while suspended must not select the type"
                );

                ResumeFromPlayMode(window);
                controller.SelectType(window, typeof(TestDataObject));
                Assert.AreEqual(
                    typeof(TestDataObject),
                    controller.SelectedType,
                    "the same type switch after resume must select the type"
                );
            }
        }

        [Test]
        public void ShouldGateManualTypeLoadWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                CreateFixtureAsset(folder, "First.asset");

                SuspendForPlayMode(window);
                InvokePrivate(window, "LoadObjectTypesAsync", typeof(TestDataObject), false);
                Assert.IsFalse(
                    ReadPrivateField<bool>(window, "_isLoadingObjectsAsync"),
                    "a manual type load while suspended must not start the load chain"
                );
                Assert.IsNull(ReadPrivateField<Type>(window, "_asyncLoadTargetType"));
                Assert.AreEqual(
                    0,
                    ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects").Count,
                    "a manual type load while suspended must not load any objects"
                );

                ResumeFromPlayMode(window);
                InvokePrivate(window, "LoadObjectTypesAsync", typeof(TestDataObject), false);
                Assert.AreEqual(
                    1,
                    ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects").Count,
                    "the same manual type load after resume must load the type's objects"
                );
            }
        }

        [Test]
        public void ShouldShowPausedIndicatorAndDisableMutationButtonsWhileSuspendedForPlayMode()
        {
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
                window.CreateGUI();

                Label pausedIndicator = ReadPrivateField<Label>(window, "_pausedIndicator");
                Button createButton = ReadPrivateField<Button>(window, "_createObjectButton");
                Button addTypeButton = ReadPrivateField<Button>(window, "_addTypeButton");
                Button loadDataFolderButton = ReadPrivateField<Button>(
                    window,
                    "_addTypesFromDataFolderButton"
                );
                Button loadScriptFolderButton = ReadPrivateField<Button>(
                    window,
                    "_addTypesFromScriptFolderButton"
                );

                Assert.AreEqual(DisplayStyle.None, pausedIndicator.style.display.value);
                Assert.IsTrue(createButton.enabledSelf);
                Assert.IsTrue(addTypeButton.enabledSelf);
                Assert.IsTrue(loadDataFolderButton.enabledSelf);
                Assert.IsTrue(loadScriptFolderButton.enabledSelf);

                SuspendForPlayMode(window);
                Assert.AreEqual(DisplayStyle.Flex, pausedIndicator.style.display.value);
                Assert.IsFalse(createButton.enabledSelf);
                Assert.IsFalse(addTypeButton.enabledSelf);
                Assert.IsFalse(loadDataFolderButton.enabledSelf);
                Assert.IsFalse(loadScriptFolderButton.enabledSelf);

                ResumeFromPlayMode(window);
                Assert.AreEqual(DisplayStyle.None, pausedIndicator.style.display.value);
                Assert.IsTrue(createButton.enabledSelf);
                Assert.IsTrue(addTypeButton.enabledSelf);
                Assert.IsTrue(loadDataFolderButton.enabledSelf);
                Assert.IsTrue(loadScriptFolderButton.enabledSelf);
            }
        }

        [Test]
        public void ShouldBuildDisabledInspectorContentWhileSuspendedForPlayMode()
        {
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
                window.CreateGUI();

                VisualElement inspectorContainer = ReadPrivateField<VisualElement>(
                    window,
                    "_inspectorContainer"
                );
                Assert.IsTrue(inspectorContainer.enabledSelf);

                SuspendForPlayMode(window);
                InvokePrivate(window, "BuildInspectorView");
                Assert.IsFalse(
                    inspectorContainer.enabledSelf,
                    "inspector content built while suspended must stay disabled"
                );

                ResumeFromPlayMode(window);
                InvokePrivate(window, "BuildInspectorView");
                Assert.IsTrue(
                    inspectorContainer.enabledSelf,
                    "inspector content built after resume must be enabled"
                );
            }
        }
    }
}
