namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;

    public sealed class SettingsPersistenceTests
    {
        private static void AssertCollapseStateDirtySemantics(
            Func<string, bool, bool> setCollapsed,
            Func<string, bool> removeCollapsed
        )
        {
            const string namespaceKey = "Gameplay";

            Assert.IsTrue(setCollapsed(namespaceKey, false), "first write stores expanded state");
            Assert.IsFalse(
                setCollapsed(namespaceKey, false),
                "duplicate expanded write is unchanged"
            );
            Assert.IsTrue(setCollapsed(namespaceKey, true), "changed collapse state is dirty");
            Assert.IsFalse(
                setCollapsed(namespaceKey, true),
                "duplicate collapsed write is unchanged"
            );
            Assert.IsTrue(removeCollapsed(namespaceKey), "removing stored state is dirty");
            Assert.IsFalse(removeCollapsed(namespaceKey), "removing absent state is unchanged");
        }

        [Test]
        public void ShouldMarkSettingsDirtyWhenSelectActiveObjectChanges()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
                settings.selectActiveObject = false;
                EditorUtility.ClearDirty(settings);

                Assert.IsFalse(EditorUtility.IsDirty(settings));
                Assert.IsTrue(settings.SetSelectActiveObject(true));
                Assert.IsTrue(settings.selectActiveObject);
                Assert.IsTrue(EditorUtility.IsDirty(settings));
            }
        }

        [Test]
        public void ShouldNotMarkSettingsDirtyWhenSelectActiveObjectIsUnchanged()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
                settings.selectActiveObject = true;
                EditorUtility.ClearDirty(settings);

                Assert.IsFalse(settings.SetSelectActiveObject(true));
                Assert.IsTrue(settings.selectActiveObject);
                Assert.IsFalse(EditorUtility.IsDirty(settings));
            }
        }

        [Test]
        public void ShouldMarkSettingsDirtyWhenSelectActiveObjectPreferenceChangesThroughWindowHelper()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
                settings.selectActiveObject = false;
                EditorUtility.ClearDirty(settings);

                Assert.IsTrue(DataVisualizer.ApplySelectActiveObjectPreference(settings, true));
                Assert.IsTrue(settings.selectActiveObject);
                Assert.IsTrue(EditorUtility.IsDirty(settings));
            }
        }

        [Test]
        public void ShouldReportDirtyOnlyForActualCollapseStateChangesWhenUsingSettingsStore()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
                const string namespaceKey = "Gameplay";

                EditorUtility.ClearDirty(settings);
                Assert.IsTrue(
                    settings.SetNamespaceCollapsed(namespaceKey, false),
                    "first write stores expanded state"
                );
                Assert.IsTrue(EditorUtility.IsDirty(settings));

                EditorUtility.ClearDirty(settings);
                Assert.IsFalse(
                    settings.SetNamespaceCollapsed(namespaceKey, false),
                    "duplicate expanded write is unchanged"
                );
                Assert.IsFalse(EditorUtility.IsDirty(settings));

                Assert.IsTrue(
                    settings.SetNamespaceCollapsed(namespaceKey, true),
                    "changed collapse state is dirty"
                );
                Assert.IsTrue(EditorUtility.IsDirty(settings));

                EditorUtility.ClearDirty(settings);
                Assert.IsFalse(
                    settings.SetNamespaceCollapsed(namespaceKey, true),
                    "duplicate collapsed write is unchanged"
                );
                Assert.IsFalse(EditorUtility.IsDirty(settings));

                Assert.IsTrue(
                    settings.RemoveNamespaceCollapseState(namespaceKey),
                    "removing stored state is dirty"
                );
                Assert.IsTrue(EditorUtility.IsDirty(settings));

                EditorUtility.ClearDirty(settings);
                Assert.IsFalse(
                    settings.RemoveNamespaceCollapseState(namespaceKey),
                    "removing absent state is unchanged"
                );
                Assert.IsFalse(EditorUtility.IsDirty(settings));
            }
        }

        [Test]
        public void ShouldReportDirtyOnlyForActualCollapseStateChangesWhenUsingUserStateStore()
        {
            DataVisualizerUserState userState = new();

            AssertCollapseStateDirtySemantics(
                userState.SetNamespaceCollapsed,
                userState.RemoveNamespaceCollapseState
            );
        }

        [TestCase(
            "{\"lastSelectedTypeName\":\"Example.Namespace.LegacyData\"}",
            "Example.Namespace.LegacyData"
        )]
        [TestCase(
            "{\"lastSelectedTypeName\":\"Example.Namespace.LegacyData\",\"lastSelectedTypeFullName\":\"Example.Namespace.CurrentData\"}",
            "Example.Namespace.CurrentData"
        )]
        [TestCase(
            "{\"lastSelectedTypeName\":\"Example.Namespace.LegacyData\",\"lastSelectedTypeFullName\":\"\"}",
            "Example.Namespace.LegacyData"
        )]
        [TestCase(
            "{\"lastSelectedNamespaceKey\":\"\\\"lastSelectedTypeFullName\\\"\",\"lastSelectedTypeName\":\"Example.Namespace.LegacyData\"}",
            "Example.Namespace.LegacyData"
        )]
        public void ShouldMigrateLegacySelectedTypeNameWhenLoadingUserStateJson(
            string json,
            string expectedTypeFullName
        )
        {
            DataVisualizerUserState userState = DataVisualizerUserState.FromJson(json);

            Assert.AreEqual(expectedTypeFullName, userState.lastSelectedTypeFullName);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("{")]
        [TestCase("{\"lastSelectedTypeFullName\":}")]
        public void ShouldReturnNullWithoutThrowingWhenLoadingInvalidUserStateJson(string json)
        {
            Assert.IsNull(DataVisualizerUserState.FromJson(json));
        }

        [Test]
        public void ShouldDeserializeCurrentFieldsWhenLoadingValidUserStateJson()
        {
            const string json =
                "{\"lastSelectedNamespaceKey\":\"Gameplay\",\"lastSelectedTypeFullName\":\"Example.CurrentData\"}";

            DataVisualizerUserState userState = DataVisualizerUserState.FromJson(json);

            Assert.AreEqual("Gameplay", userState.lastSelectedNamespaceKey);
            Assert.AreEqual("Example.CurrentData", userState.lastSelectedTypeFullName);
        }
    }
}
