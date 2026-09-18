namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    /*
        Pins the benign interactions recorded as silent by the #36 minimal-log review: foreign
        drops over label drop targets and stale removal confirmations are idempotent no-ops, so
        they must not emit user-facing warnings during normal operation.
    */
    public sealed class ConsoleNoiseRegressionTests
    {
        private const string ForeignDropWarningFragment = "Invalid drag data received";
        private const string NamespaceRemovalWarningFragment = "No change detected for namespace";
        private const string TypeRemovalWarningFragment = "was not found in managed list";
        private const string DraggedLabelTextKey = "DraggedLabelText";
        private const string SourceSectionKey = "SourceSection";
        private const string StaleSentinelTypeName = "Stale.Sentinel.FakeType";

        private static DataVisualizerWindow CreateWindowWithController(
            TestCleanupScope cleanup,
            object controller
        )
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            settings.persistStateInSettingsAsset = true;
            SetPrivateField(
                settings,
                "managedTypeNames",
                new List<string> { StaleSentinelTypeName }
            );

            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
            SetPrivateField(window, "_settings", settings);
            SetPrivateField(window, "_namespaceController", controller);
            return window;
        }

        private static object CreateControllerWithManagedTypes(params Type[] managedTypes)
        {
            Dictionary<string, List<Type>> managedTypeLists = new();
            foreach (Type managedType in managedTypes)
            {
                string namespaceKey = GetNamespaceKey(managedType);
                if (!managedTypeLists.TryGetValue(namespaceKey, out List<Type> types))
                {
                    types = new List<Type>();
                    managedTypeLists.Add(namespaceKey, types);
                }

                types.Add(managedType);
            }

            return Activator.CreateInstance(
                NamespaceControllerType(),
                managedTypeLists,
                new Dictionary<string, int>()
            );
        }

        private static int CountManagedTypes(object controller)
        {
            Dictionary<string, List<Type>> managedTypeLists = ReadPrivateField<
                Dictionary<string, List<Type>>
            >(controller, "_managedTypes");
            int count = 0;
            foreach (List<Type> types in managedTypeLists.Values)
            {
                count += types.Count;
            }

            return count;
        }

        private static string GetNamespaceKey(Type type)
        {
            MethodInfo method = NamespaceControllerType()
                .GetMethod(
                    "GetNamespaceKey",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                );
            Assert.That(method != null, "The GetNamespaceKey method must exist.");
            return (string)method.Invoke(null, new object[] { type });
        }

        private static void InvokeControllerPrivate(
            object controller,
            string methodName,
            params object[] arguments
        )
        {
            MethodInfo method = controller
                .GetType()
                .GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );
            Assert.That(method != null, $"The {methodName} method must exist.");
            method.Invoke(controller, arguments);
        }

        private static object LabelFilterSectionValue(string name)
        {
            Type sectionType = typeof(DataVisualizerWindow).Assembly.GetType(
                "WallstopStudios.DataVisualizer.Editor.LabelFilterSection"
            );
            Assert.That(sectionType != null, "The LabelFilterSection type must exist.");
            return Enum.Parse(sectionType, name);
        }

        private static Type NamespaceControllerType()
        {
            Type controllerType = typeof(DataVisualizerWindow).Assembly.GetType(
                "WallstopStudios.DataVisualizer.Editor.NamespaceController"
            );
            Assert.That(controllerType != null, "The NamespaceController type must exist.");
            return controllerType;
        }

        private static VisualElement CreateAndAttachDropTarget(
            LayoutTestWindow host,
            TestCleanupScope cleanup
        )
        {
            VisualElement dropTarget = new() { name = "console-noise-drop-target" };
            host.rootVisualElement.Add(dropTarget);
            cleanup.Defer(() => dropTarget.RemoveFromHierarchy());
            return dropTarget;
        }

        private static DataVisualizerSettings ReadSettings(DataVisualizerWindow window)
        {
            return ReadPrivateField<DataVisualizerSettings>(window, "_settings");
        }

        private static T ReadPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target
                .GetType()
                .GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );
            Assert.That(field != null, $"The {fieldName} field must exist.");
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target
                .GetType()
                .GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );
            Assert.That(field != null, $"The {fieldName} field must exist.");
            field.SetValue(target, value);
        }

        private static void InvokePrivate(
            DataVisualizerWindow window,
            string methodName,
            params object[] arguments
        )
        {
            MethodInfo method = typeof(DataVisualizerWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            Assert.That(method != null, $"The {methodName} method must exist.");
            method.Invoke(window, arguments);
        }

        [Test]
        public void ShouldSilentlyIgnoreForeignDropOnLabelDropTarget()
        {
            LayoutTestWindow host = EditorWindow.GetWindow<LayoutTestWindow>(
                "Data Visualizer Console Noise Anchor",
                false
            );
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => host.Close());

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                VisualElement dropTarget = CreateAndAttachDropTarget(host, cleanup);
                InvokePrivate(
                    window,
                    "SetupDropTarget",
                    dropTarget,
                    LabelFilterSectionValue("AND")
                );

                DragAndDrop.PrepareStartDrag();
                using (ConsoleCapture capture = new())
                using (DragPerformEvent dragPerformEvent = DragPerformEvent.GetPooled())
                {
                    dragPerformEvent.target = dropTarget;
                    dropTarget.SendEvent(dragPerformEvent);
                    Assert.IsFalse(
                        capture.Contains(ForeignDropWarningFragment),
                        "a foreign drop over a label drop target must not log a warning"
                    );
                }
            }
        }

        [Test]
        public void ShouldApplyPackageLabelDropWithoutLogging()
        {
            LayoutTestWindow host = EditorWindow.GetWindow<LayoutTestWindow>(
                "Data Visualizer Console Noise Anchor",
                false
            );
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => host.Close());
                cleanup.Defer(() => DragAndDrop.PrepareStartDrag());

                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
                settings.persistStateInSettingsAsset = true;

                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                Type selectedType = typeof(EditorOnlyCreationData);
                SetPrivateField(window, "_settings", settings);
                SetPrivateField(
                    window,
                    "_namespaceController",
                    CreateControllerWithManagedTypes(selectedType)
                );
                SetPrivateField(
                    ReadPrivateField<object>(window, "_namespaceController"),
                    "_selectedType",
                    selectedType
                );

                const string labelText = "ConsoleNoiseLabel";
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(DraggedLabelTextKey, labelText);
                DragAndDrop.SetGenericData(SourceSectionKey, "Available");

                VisualElement dropTarget = CreateAndAttachDropTarget(host, cleanup);
                InvokePrivate(
                    window,
                    "SetupDropTarget",
                    dropTarget,
                    LabelFilterSectionValue("AND")
                );

                using (ConsoleCapture capture = new())
                using (DragPerformEvent dragPerformEvent = DragPerformEvent.GetPooled())
                {
                    dragPerformEvent.target = dropTarget;
                    dropTarget.SendEvent(dragPerformEvent);
                    Assert.IsFalse(
                        capture.Contains(ForeignDropWarningFragment),
                        "a package label drop must not log a warning"
                    );
                }

                IList configs = ReadPrivateField<IList>(ReadSettings(window), "labelFilterConfigs");
                object matchingConfig = null;
                foreach (object candidate in configs)
                {
                    string typeFullName = ReadPrivateField<string>(candidate, "typeFullName");
                    if (
                        string.Equals(typeFullName, selectedType.FullName, StringComparison.Ordinal)
                    )
                    {
                        matchingConfig = candidate;
                        break;
                    }
                }

                Assert.That(
                    matchingConfig != null,
                    "the label filter config must exist after the drop"
                );
                List<string> andLabels = ReadPrivateField<List<string>>(
                    matchingConfig,
                    "andLabels"
                );
                Assert.IsTrue(
                    andLabels.Contains(labelText),
                    "dropping the label onto the AND section must add it to the AND filter"
                );
            }
        }

        [Test]
        public void ShouldSilentlyIgnoreStaleNamespaceRemovalConfirm()
        {
            string namespaceKey = GetNamespaceKey(typeof(EditorOnlyCreationData));
            using (TestCleanupScope cleanup = new())
            {
                DataVisualizerWindow window = CreateWindowWithController(
                    cleanup,
                    CreateControllerWithManagedTypes(typeof(EditorOnlyCreationData))
                );

                using (ConsoleCapture capture = new())
                {
                    InvokeControllerPrivate(
                        ReadPrivateField<object>(window, "_namespaceController"),
                        "HandleRemoveNamespaceTypesConfirmed",
                        window,
                        namespaceKey,
                        new List<Type> { typeof(DerivedEditorOnlyCreationData) }
                    );

                    Assert.IsFalse(
                        capture.Contains(NamespaceRemovalWarningFragment),
                        "a stale namespace removal confirm must not log a warning"
                    );
                }

                Assert.AreEqual(
                    1,
                    CountManagedTypes(ReadPrivateField<object>(window, "_namespaceController")),
                    "a stale removal confirm must leave the managed list unchanged"
                );
                List<string> untouchedManagedTypes = ReadPrivateField<List<string>>(
                    ReadSettings(window),
                    "managedTypeNames"
                );
                Assert.AreEqual(
                    1,
                    untouchedManagedTypes.Count,
                    "a stale removal confirm must not persist a managed list"
                );
                Assert.AreEqual(
                    StaleSentinelTypeName,
                    untouchedManagedTypes[0],
                    "a stale removal confirm must leave the persisted list unchanged"
                );
            }
        }

        [Test]
        public void ShouldStillRemoveManagedTypeOnNamespaceRemovalConfirm()
        {
            string namespaceKey = GetNamespaceKey(typeof(EditorOnlyCreationData));
            using (TestCleanupScope cleanup = new())
            {
                DataVisualizerWindow window = CreateWindowWithController(
                    cleanup,
                    CreateControllerWithManagedTypes(typeof(EditorOnlyCreationData))
                );

                using (ConsoleCapture capture = new())
                {
                    InvokeControllerPrivate(
                        ReadPrivateField<object>(window, "_namespaceController"),
                        "HandleRemoveNamespaceTypesConfirmed",
                        window,
                        namespaceKey,
                        new List<Type> { typeof(EditorOnlyCreationData) }
                    );

                    Assert.IsFalse(
                        capture.Contains(NamespaceRemovalWarningFragment),
                        "an active removal confirm must not log a warning"
                    );
                }

                List<string> persistedManagedTypes = ReadPrivateField<List<string>>(
                    ReadSettings(window),
                    "managedTypeNames"
                );
                Assert.That(persistedManagedTypes != null, "the active removal must persist");
                Assert.IsEmpty(
                    persistedManagedTypes,
                    "the active removal confirm must remove the type from the persisted list"
                );
            }
        }

        [Test]
        public void ShouldSilentlyIgnoreStaleTypeRemovalConfirm()
        {
            using (TestCleanupScope cleanup = new())
            {
                DataVisualizerWindow window = CreateWindowWithController(
                    cleanup,
                    CreateControllerWithManagedTypes()
                );

                using (ConsoleCapture capture = new())
                {
                    InvokeControllerPrivate(
                        ReadPrivateField<object>(window, "_namespaceController"),
                        "HandleRemoveTypeConfirmed",
                        window,
                        typeof(EditorOnlyCreationData)
                    );

                    Assert.IsFalse(
                        capture.Contains(TypeRemovalWarningFragment),
                        "a stale type removal confirm must not log a warning"
                    );
                }

                List<string> untouchedManagedTypes = ReadPrivateField<List<string>>(
                    ReadSettings(window),
                    "managedTypeNames"
                );
                Assert.AreEqual(
                    1,
                    untouchedManagedTypes.Count,
                    "a stale type removal confirm must not persist a managed list"
                );
                Assert.AreEqual(
                    StaleSentinelTypeName,
                    untouchedManagedTypes[0],
                    "a stale type removal confirm must leave the persisted list unchanged"
                );
            }
        }

        [Test]
        public void ShouldStillRemoveManagedTypeOnTypeRemovalConfirm()
        {
            using (TestCleanupScope cleanup = new())
            {
                DataVisualizerWindow window = CreateWindowWithController(
                    cleanup,
                    CreateControllerWithManagedTypes(typeof(EditorOnlyCreationData))
                );

                using (ConsoleCapture capture = new())
                {
                    InvokeControllerPrivate(
                        ReadPrivateField<object>(window, "_namespaceController"),
                        "HandleRemoveTypeConfirmed",
                        window,
                        typeof(EditorOnlyCreationData)
                    );

                    Assert.IsFalse(
                        capture.Contains(TypeRemovalWarningFragment),
                        "an active removal confirm must not log a warning"
                    );
                }

                List<string> persistedManagedTypes = ReadPrivateField<List<string>>(
                    ReadSettings(window),
                    "managedTypeNames"
                );
                Assert.That(persistedManagedTypes != null, "the active removal must persist");
                Assert.IsEmpty(
                    persistedManagedTypes,
                    "the active removal confirm must remove the type from the persisted list"
                );
            }
        }

        /*
            Editor-assembly internal names are mirrored as strings because InternalsVisibleTo is
            not honored for the editor/test assembly pair (see ObjectIdExtensions.cs).
        */
        private static class LabelFilterSectionNames
        {
            public const string And = "AND";
            public const string Available = "Available";
        }
    }
}
