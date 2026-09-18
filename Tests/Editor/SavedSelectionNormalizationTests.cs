namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    /*
        Pins the saved-selection half of the busy-window GUID normalization deferral (issue #36):
        a load inside the AssetDatabase-busy window keeps the persisted saved selection and
        queues a re-normalization, and the pump clears it after the database settles only when
        the selection is genuinely invalid.
    */
    public sealed class SavedSelectionNormalizationTests
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
                "Assets/DataVisualizerSavedSelectionNormalizationTests_"
                + Guid.NewGuid().ToString("N");
            EnsureFolderExists(folder);
            return folder;
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
            Assert.That(field != null, $"The {fieldName} field must exist.");
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
            Assert.That(field != null, $"The {fieldName} field must exist.");
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
            Assert.That(method != null, $"The {methodName} method must exist.");
            return method.Invoke(window, arguments);
        }

        private static DataVisualizerWindow CreateWindow(
            TestCleanupScope cleanup,
            out DataVisualizerSettings settings
        )
        {
            DataVisualizerSettings created =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(created));
            created.persistStateInSettingsAsset = true;

            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
            SetPrivateField(window, "_settings", created);
            settings = created;
            return window;
        }

        private static string ReadSavedSelection(DataVisualizerWindow window, string typeFullName)
        {
            return (string)InvokePrivate(window, "GetLastSelectedObjectGuidForType", typeFullName);
        }

        private static void WriteSavedSelection(
            DataVisualizerWindow window,
            string typeFullName,
            string objectGuid
        )
        {
            InvokePrivate(window, "SetLastSelectedObjectGuidForType", typeFullName, objectGuid);
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
        public void ShouldKeepSavedSelectionWhenLoadRunsWhileAssetDatabaseIsBusy()
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
                cleanup.Defer(() => AssetGuidDiscovery.AssetDatabaseBusyOverride = null);

                DataVisualizerWindow window = CreateWindow(cleanup, out DataVisualizerSettings _);
                AssetGuidDiscovery.AssetDatabaseBusyOverride = () => true;
                string typeFullName = typeof(TestDataObject).FullName;
                WriteSavedSelection(window, typeFullName, "stale-saved-guid");

                InvokePrivate(window, "LoadObjectTypesAsync", typeof(TestDataObject), false);

                Assert.AreEqual(
                    "stale-saved-guid",
                    ReadSavedSelection(window, typeFullName),
                    "a busy-window load must keep the persisted saved selection"
                );
                CollectionAssert.Contains(
                    ReadPrivateField<HashSet<Type>>(
                        window,
                        "_pendingSavedSelectionNormalizationTypes"
                    ),
                    typeof(TestDataObject)
                );
                Assert.That(
                    ReadPrivateField<IVisualElementScheduledItem>(
                        window,
                        "_savedSelectionNormalizationTask"
                    ) != null,
                    "a queued normalization must schedule the pump"
                );
            }
        }

        [Test]
        public void ShouldClearInvalidSavedSelectionAfterAssetDatabaseSettles()
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
                cleanup.Defer(() => AssetGuidDiscovery.AssetDatabaseBusyOverride = null);

                DataVisualizerWindow window = CreateWindow(cleanup, out DataVisualizerSettings _);
                string typeFullName = typeof(TestDataObject).FullName;
                WriteSavedSelection(window, typeFullName, "stale-saved-guid");

                bool isBusy = true;
                AssetGuidDiscovery.AssetDatabaseBusyOverride = () => isBusy;
                InvokePrivate(window, "LoadObjectTypesAsync", typeof(TestDataObject), false);
                Assert.AreEqual(
                    "stale-saved-guid",
                    ReadSavedSelection(window, typeFullName),
                    "a busy-window load must keep the persisted saved selection"
                );

                isBusy = false;
                InvokePrivate(window, "ProcessSavedSelectionNormalizations");

                Assert.That(
                    ReadSavedSelection(window, typeFullName) == null,
                    "an invalid saved selection must be cleared once the AssetDatabase settles"
                );
                Assert.IsEmpty(
                    ReadPrivateField<HashSet<Type>>(
                        window,
                        "_pendingSavedSelectionNormalizationTypes"
                    )
                );
            }
        }

        [Test]
        public void ShouldKeepValidSavedSelectionAfterAssetDatabaseSettles()
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
                cleanup.Defer(() => AssetGuidDiscovery.AssetDatabaseBusyOverride = null);

                DataVisualizerWindow window = CreateWindow(cleanup, out DataVisualizerSettings _);

                string assetPath = folder + "/First.asset";
                AssetDatabase.CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    assetPath
                );
                AssetDatabase.SaveAssets();
                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);

                string typeFullName = typeof(TestDataObject).FullName;
                WriteSavedSelection(window, typeFullName, assetGuid);

                bool isBusy = true;
                AssetGuidDiscovery.AssetDatabaseBusyOverride = () => isBusy;
                InvokePrivate(window, "LoadObjectTypesAsync", typeof(TestDataObject), false);
                Assert.AreEqual(
                    assetGuid,
                    ReadSavedSelection(window, typeFullName),
                    "a busy-window load must keep the persisted saved selection"
                );

                isBusy = false;
                InvokePrivate(window, "ProcessSavedSelectionNormalizations");

                Assert.AreEqual(
                    assetGuid,
                    ReadSavedSelection(window, typeFullName),
                    "a valid saved selection must survive the deferred re-normalization"
                );
                Assert.IsEmpty(
                    ReadPrivateField<HashSet<Type>>(
                        window,
                        "_pendingSavedSelectionNormalizationTypes"
                    )
                );
            }
        }

        [Test]
        public void ShouldResumeSavedSelectionNormalizationAfterPlayMode()
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
                cleanup.Defer(() => AssetGuidDiscovery.AssetDatabaseBusyOverride = null);

                DataVisualizerWindow window = CreateWindow(cleanup, out DataVisualizerSettings _);
                string typeFullName = typeof(TestDataObject).FullName;
                WriteSavedSelection(window, typeFullName, "stale-saved-guid");

                bool isBusy = true;
                AssetGuidDiscovery.AssetDatabaseBusyOverride = () => isBusy;
                InvokePrivate(window, "LoadObjectTypesAsync", typeof(TestDataObject), false);

                SuspendForPlayMode(window);
                Assert.That(
                    ReadPrivateField<IVisualElementScheduledItem>(
                        window,
                        "_savedSelectionNormalizationTask"
                    ) == null,
                    "suspending for play mode must stop the pump"
                );
                CollectionAssert.Contains(
                    ReadPrivateField<HashSet<Type>>(
                        window,
                        "_pendingSavedSelectionNormalizationTypes"
                    ),
                    typeof(TestDataObject),
                    "suspending for play mode must keep the queued normalization"
                );

                ResumeFromPlayMode(window);
                Assert.That(
                    ReadPrivateField<IVisualElementScheduledItem>(
                        window,
                        "_savedSelectionNormalizationTask"
                    ) != null,
                    "resuming must re-kick the pump for the queued normalization"
                );

                isBusy = false;
                InvokePrivate(window, "ProcessSavedSelectionNormalizations");
                Assert.That(ReadSavedSelection(window, typeFullName) == null);
            }
        }
    }
}
