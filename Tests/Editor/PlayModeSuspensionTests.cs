namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class PlayModeSuspensionTests
    {
        private static FieldInfo _instanceField;
        private static FieldInfo _needsRefreshField;
        private static MethodInfo _scheduleRefreshMethod;

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
                "Assets/DataVisualizerPlayModeSuspensionTests_" + Guid.NewGuid().ToString("N");
            EnsureFolderExists(folder);
            return folder;
        }

        private static string CreateAsset(ScriptableObject asset, string assetPath)
        {
            AssetDatabase.CreateAsset(asset, assetPath);
            return AssetDatabase.AssetPathToGUID(assetPath);
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

        private static bool ReadNeedsRefresh(DataVisualizerWindow window)
        {
            if (_needsRefreshField == null)
            {
                _needsRefreshField = typeof(DataVisualizerWindow).GetField(
                    "_needsRefresh",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
            }

            Assert.IsNotNull(_needsRefreshField, "The _needsRefresh field must exist.");
            return (bool)_needsRefreshField.GetValue(window);
        }

        private static void InvokeScheduleRefresh(DataVisualizerWindow window)
        {
            if (_scheduleRefreshMethod == null)
            {
                _scheduleRefreshMethod = typeof(DataVisualizerWindow).GetMethod(
                    "ScheduleRefresh",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
            }

            Assert.IsNotNull(_scheduleRefreshMethod, "The ScheduleRefresh method must exist.");
            _scheduleRefreshMethod.Invoke(window, null);
        }

        [Test]
        public void ShouldStopPumpingWhileSuspendedAndCompleteOnResume()
        {
            string folder = CreateFixtureFolder();
            try
            {
                string firstGuid = CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    folder + "/First.asset"
                );
                string secondGuid = CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    folder + "/Second.asset"
                );
                string thirdGuid = CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    folder + "/Third.asset"
                );
                AssetDatabase.SaveAssets();

                AssetGuidTypeIndex index = new();
                index.Rebuild();
                Assert.IsFalse(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsFalse(index.ProcessPendingSlice(0d));
                Assert.IsFalse(index.IsComplete);

                int knownBeforeSuspend = index.GetKnownGuids(typeof(TestDataObject)).Length;
                index.Suspend();
                Assert.IsTrue(index.IsSuspended);
                Assert.IsFalse(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsFalse(index.IsComplete);
                Assert.AreEqual(
                    knownBeforeSuspend,
                    index.GetKnownGuids(typeof(TestDataObject)).Length
                );

                int completedCount = 0;
                index.IndexCompleted += () => completedCount++;
                index.Resume();
                Assert.IsFalse(index.IsSuspended);
                Assert.IsTrue(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsTrue(index.IsComplete);

                string[] guids = index.GetKnownGuids(typeof(TestDataObject));
                Assert.AreEqual(3, guids.Length);
                CollectionAssert.Contains(guids, firstGuid);
                CollectionAssert.Contains(guids, secondGuid);
                CollectionAssert.Contains(guids, thirdGuid);
                Assert.AreEqual(1, completedCount);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void ShouldDeferAssetChangesWhileSuspendedAndReconcileOnResume()
        {
            string folder = CreateFixtureFolder();
            try
            {
                string firstGuid = CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    folder + "/First.asset"
                );
                AssetDatabase.SaveAssets();

                AssetGuidTypeIndex index = new();
                index.Rebuild();
                Assert.IsFalse(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsTrue(index.ProcessPendingSlice(double.PositiveInfinity));
                CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), firstGuid);

                string importedPath = folder + "/ImportedWhileSuspended.asset";
                string importedGuid = CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    importedPath
                );
                AssetDatabase.SaveAssets();

                index.Suspend();
                Assert.IsFalse(
                    index.ApplyAssetChanges(
                        new[] { importedPath },
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>()
                    )
                );
                CollectionAssert.DoesNotContain(
                    index.GetKnownGuids(typeof(TestDataObject)),
                    importedGuid
                );

                int completedCount = 0;
                index.IndexCompleted += () => completedCount++;
                index.Resume();
                Assert.IsTrue(index.ProcessPendingSlice(double.PositiveInfinity));

                string[] guids = index.GetKnownGuids(typeof(TestDataObject));
                CollectionAssert.Contains(guids, firstGuid);
                CollectionAssert.Contains(guids, importedGuid);
                Assert.AreEqual(1, completedCount);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void ShouldReconcileDeferredDeletionOnResume()
        {
            string folder = CreateFixtureFolder();
            try
            {
                string firstGuid = CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    folder + "/First.asset"
                );
                string secondGuid = CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    folder + "/Second.asset"
                );
                AssetDatabase.SaveAssets();

                AssetGuidTypeIndex index = new();
                index.Rebuild();
                Assert.IsFalse(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsTrue(index.ProcessPendingSlice(double.PositiveInfinity));
                CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), firstGuid);
                CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), secondGuid);

                string secondPath = folder + "/Second.asset";
                index.Suspend();
                AssetDatabase.DeleteAsset(secondPath);
                Assert.IsFalse(
                    index.ApplyAssetChanges(
                        Array.Empty<string>(),
                        new[] { secondPath },
                        Array.Empty<string>(),
                        Array.Empty<string>()
                    )
                );
                CollectionAssert.Contains(index.GetKnownGuids(typeof(TestDataObject)), secondGuid);

                int completedCount = 0;
                index.IndexCompleted += () => completedCount++;
                index.Resume();
                Assert.IsTrue(index.ProcessPendingSlice(double.PositiveInfinity));

                string[] guids = index.GetKnownGuids(typeof(TestDataObject));
                CollectionAssert.Contains(guids, firstGuid);
                CollectionAssert.DoesNotContain(guids, secondGuid);
                Assert.AreEqual(1, completedCount);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void ShouldNotFireIndexCompletedWhenResumeHasNothingDeferred()
        {
            string folder = CreateFixtureFolder();
            try
            {
                CreateAsset(
                    ScriptableObject.CreateInstance<TestDataObject>(),
                    folder + "/First.asset"
                );
                AssetDatabase.SaveAssets();

                AssetGuidTypeIndex index = new();
                index.Rebuild();
                Assert.IsFalse(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsTrue(index.ProcessPendingSlice(double.PositiveInfinity));
                Assert.IsTrue(index.IsComplete);

                int completedCount = 0;
                index.IndexCompleted += () => completedCount++;
                index.Suspend();
                index.Resume();

                Assert.IsFalse(index.IsSuspended);
                Assert.IsTrue(index.IsComplete);
                Assert.AreEqual(0, completedCount);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void ShouldSuspendWindowBackgroundWorkWhenPlayModeEnters()
        {
            object previousInstance = ReadSharedInstance();
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            try
            {
                window.HandlePlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
                Assert.IsTrue(AssetGuidTypeIndex.Shared.IsSuspended);

                InvokeScheduleRefresh(window);
                Assert.IsTrue(ReadNeedsRefresh(window));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                RestoreSharedInstance(previousInstance);
                AssetGuidTypeIndex.Shared.Cancel();
            }
        }

        [Test]
        public void ShouldResumeWindowWorkAndCoalesceRefreshWhenPlayModeExits()
        {
            object previousInstance = ReadSharedInstance();
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            try
            {
                window.HandlePlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
                InvokeScheduleRefresh(window);
                Assert.IsTrue(ReadNeedsRefresh(window));

                window.HandlePlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
                Assert.IsFalse(AssetGuidTypeIndex.Shared.IsSuspended);
                Assert.IsTrue(ReadNeedsRefresh(window));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                RestoreSharedInstance(previousInstance);
                AssetGuidTypeIndex.Shared.Cancel();
            }
        }

        [Test]
        public void ShouldIgnoreEnteredEditModeWhenWindowWasNeverSuspended()
        {
            object previousInstance = ReadSharedInstance();
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            try
            {
                window.HandlePlayModeStateChanged(PlayModeStateChange.EnteredEditMode);

                Assert.IsFalse(AssetGuidTypeIndex.Shared.IsSuspended);
                Assert.IsFalse(ReadNeedsRefresh(window));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                RestoreSharedInstance(previousInstance);
                AssetGuidTypeIndex.Shared.Cancel();
            }
        }

        [Test]
        public void ShouldReleaseSharedSuspensionWhenWindowClosesWhileSuspended()
        {
            object previousInstance = ReadSharedInstance();
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            try
            {
                window.HandlePlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
                Assert.IsTrue(AssetGuidTypeIndex.Shared.IsSuspended);

                /*
                    Closing the window during play must not leak the suspension into the next
                    session: no EnteredEditMode callback will arrive for this window.
                */
                UnityEngine.Object.DestroyImmediate(window);
                window = null;

                Assert.IsFalse(AssetGuidTypeIndex.Shared.IsSuspended);
            }
            finally
            {
                if (window != null)
                {
                    UnityEngine.Object.DestroyImmediate(window);
                }

                RestoreSharedInstance(previousInstance);
                AssetGuidTypeIndex.Shared.Cancel();
            }
        }
    }
}
