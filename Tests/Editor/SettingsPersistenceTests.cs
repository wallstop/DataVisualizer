namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
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
        [TestCase(
            "{\"lastSelectedTypeName\":\"Example.Namespace.LegacyData\",\"lastSelectedTypeFullName\":\"\"}",
            "Example.Namespace.LegacyData"
        )]
        [TestCase(
            "{\"lastSelectedNamespaceKey\":\"\\\"lastSelectedTypeFullName\\\"\",\"lastSelectedTypeName\":\"Example.Namespace.LegacyData\"}",
            "Example.Namespace.LegacyData"
        )]
        public void Should_MigrateLegacySelectedTypeName_When_LoadingUserStateJson(
            string json,
            string expectedTypeFullName
        )
        {
            DataVisualizerUserState userState = DataVisualizerUserState.FromJson(json);

            Assert.AreEqual(expectedTypeFullName, userState.lastSelectedTypeFullName);
        }

        [TestCase("{")]
        [TestCase("not-json")]
        public void Should_ReturnNull_When_UserStateJsonIsMalformed(string json)
        {
            DataVisualizerUserState userState = null;

            Assert.DoesNotThrow(() => userState = DataVisualizerUserState.FromJson(json));
            Assert.IsNull(userState);
        }

        [Test]
        public void Should_RoundTripPersistedStateWithoutSharingMutableCollections()
        {
            DataVisualizerUserState source = new()
            {
                lastSelectedNamespaceKey = "Gameplay",
                lastSelectedTypeFullName = "Gameplay.EnemyData",
                namespaceOrder = new List<string> { "Gameplay", "UI" },
                typeOrders = new List<NamespaceTypeOrder>
                {
                    new()
                    {
                        namespaceKey = "Gameplay",
                        typeNames = new List<string> { "Gameplay.EnemyData" },
                    },
                },
                lastObjectSelections = new List<LastObjectSelectionEntry>
                {
                    new()
                    {
                        typeFullName = "Gameplay.EnemyData",
                        objectGuid = "0123456789abcdef0123456789abcdef",
                    },
                },
                namespaceCollapseStates = new List<NamespaceCollapseState>
                {
                    new() { namespaceKey = "Gameplay", isCollapsed = true },
                },
                objectOrders = new List<TypeObjectOrder>
                {
                    new()
                    {
                        TypeFullName = "Gameplay.EnemyData",
                        ObjectGuids = new List<string> { "0123456789abcdef0123456789abcdef" },
                    },
                },
                managedTypeNames = new List<string> { "Gameplay.EnemyData" },
                labelFilterConfigs = new List<TypeLabelFilterConfig>
                {
                    new()
                    {
                        typeFullName = "Gameplay.EnemyData",
                        andLabels = new List<string> { "boss" },
                    },
                },
                processorStates = new List<ProcessorState>
                {
                    new() { typeFullName = "Gameplay.EnemyData", isCollapsed = false },
                },
            };

            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            try
            {
                settings.HydrateFrom(source);

                source.namespaceOrder.Add("Mutated");
                source.typeOrders[0].typeNames.Add("Mutated.Type");
                source.lastObjectSelections[0].objectGuid = "fedcba9876543210fedcba9876543210";

                DataVisualizerUserState restored = new();
                restored.HydrateFrom(settings);

                CollectionAssert.AreEqual(new[] { "Gameplay", "UI" }, restored.namespaceOrder);
                CollectionAssert.AreEqual(
                    new[] { "Gameplay.EnemyData" },
                    restored.typeOrders[0].typeNames
                );
                Assert.AreEqual(
                    "0123456789abcdef0123456789abcdef",
                    restored.lastObjectSelections[0].objectGuid
                );
                Assert.IsTrue(restored.HasCollapseState("Gameplay"));
                Assert.AreEqual("boss", restored.labelFilterConfigs[0].andLabels[0]);
                Assert.IsFalse(restored.processorStates[0].isCollapsed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
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
