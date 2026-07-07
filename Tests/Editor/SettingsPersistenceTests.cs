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
        [Test]
        public void Should_MarkSettingsDirty_When_SelectActiveObjectChanges()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            try
            {
                settings.selectActiveObject = false;
                EditorUtility.ClearDirty(settings);

                Assert.IsFalse(EditorUtility.IsDirty(settings));
                Assert.IsTrue(settings.SetSelectActiveObject(true));
                Assert.IsTrue(settings.selectActiveObject);
                Assert.IsTrue(EditorUtility.IsDirty(settings));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Should_NotMarkSettingsDirty_When_SelectActiveObjectIsUnchanged()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            try
            {
                settings.selectActiveObject = true;
                EditorUtility.ClearDirty(settings);

                Assert.IsFalse(settings.SetSelectActiveObject(true));
                Assert.IsTrue(settings.selectActiveObject);
                Assert.IsFalse(EditorUtility.IsDirty(settings));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Should_MarkSettingsDirty_When_SelectActiveObjectPreferenceChangesThroughWindowHelper()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            try
            {
                settings.selectActiveObject = false;
                EditorUtility.ClearDirty(settings);

                Assert.IsTrue(DataVisualizer.ApplySelectActiveObjectPreference(settings, true));
                Assert.IsTrue(settings.selectActiveObject);
                Assert.IsTrue(EditorUtility.IsDirty(settings));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Should_ReportDirtyOnlyForActualCollapseStateChanges_When_UsingSettingsStore()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            try
            {
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
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Should_ReportDirtyOnlyForActualCollapseStateChanges_When_UsingUserStateStore()
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
        public void Should_MigrateLegacySelectedTypeName_When_LoadingUserStateJson(
            string json,
            string expectedTypeFullName
        )
        {
            DataVisualizerUserState userState = DataVisualizerUserState.FromJson(json);

            Assert.AreEqual(expectedTypeFullName, userState.lastSelectedTypeFullName);
        }

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
    }
}
