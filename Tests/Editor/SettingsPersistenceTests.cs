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
        private static DataVisualizerUserState CreatePopulatedUserState()
        {
            return new DataVisualizerUserState
            {
                namespaceOrder = new List<string> { "Gameplay" },
                typeOrders = new List<NamespaceTypeOrder>
                {
                    new()
                    {
                        namespaceKey = "Gameplay",
                        typeNames = new List<string> { "Example.GameplayData" },
                    },
                },
                lastObjectSelections = new List<LastObjectSelectionEntry>
                {
                    new() { typeFullName = "Example.GameplayData", objectGuid = "guid-1" },
                },
                namespaceCollapseStates = new List<NamespaceCollapseState>
                {
                    new() { namespaceKey = "Gameplay", isCollapsed = true },
                },
                objectOrders = new List<TypeObjectOrder>
                {
                    new()
                    {
                        TypeFullName = "Example.GameplayData",
                        ObjectGuids = new List<string> { "guid-1" },
                    },
                },
                managedTypeNames = new List<string> { "Example.GameplayData" },
                labelFilterConfigs = new List<TypeLabelFilterConfig>
                {
                    new()
                    {
                        typeFullName = "Example.GameplayData",
                        andLabels = new List<string> { "required" },
                        orLabels = new List<string> { "optional" },
                    },
                },
                processorStates = new List<ProcessorState>
                {
                    new() { typeFullName = "Example.Processor" },
                },
            };
        }

        private static void AssertPopulatedUserState(DataVisualizerUserState state)
        {
            Assert.AreEqual("Gameplay", state.namespaceOrder[0]);
            Assert.AreEqual("Gameplay", state.typeOrders[0].namespaceKey);
            Assert.AreEqual("Example.GameplayData", state.typeOrders[0].typeNames[0]);
            Assert.AreEqual("Example.GameplayData", state.lastObjectSelections[0].typeFullName);
            Assert.AreEqual("guid-1", state.lastObjectSelections[0].objectGuid);
            Assert.AreEqual("Gameplay", state.namespaceCollapseStates[0].namespaceKey);
            Assert.IsTrue(state.namespaceCollapseStates[0].isCollapsed);
            Assert.AreEqual("Example.GameplayData", state.objectOrders[0].TypeFullName);
            Assert.AreEqual("guid-1", state.objectOrders[0].ObjectGuids[0]);
            Assert.AreEqual("Example.GameplayData", state.managedTypeNames[0]);
            Assert.AreEqual("Example.GameplayData", state.labelFilterConfigs[0].typeFullName);
            Assert.AreEqual("required", state.labelFilterConfigs[0].andLabels[0]);
            Assert.AreEqual("optional", state.labelFilterConfigs[0].orLabels[0]);
            Assert.AreEqual("Example.Processor", state.processorStates[0].typeFullName);
        }

        private static void MutatePopulatedUserState(DataVisualizerUserState state)
        {
            state.namespaceOrder[0] = "Changed";
            state.typeOrders[0].namespaceKey = "Changed";
            state.typeOrders[0].typeNames[0] = "Changed";
            state.lastObjectSelections[0].typeFullName = "Changed";
            state.lastObjectSelections[0].objectGuid = "Changed";
            state.namespaceCollapseStates[0].namespaceKey = "Changed";
            state.namespaceCollapseStates[0].isCollapsed = false;
            state.objectOrders[0].TypeFullName = "Changed";
            state.objectOrders[0].ObjectGuids[0] = "Changed";
            state.managedTypeNames[0] = "Changed";
            state.labelFilterConfigs[0].typeFullName = "Changed";
            state.labelFilterConfigs[0].andLabels[0] = "Changed";
            state.labelFilterConfigs[0].orLabels[0] = "Changed";
            state.processorStates[0].typeFullName = "Changed";
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
        public void ShouldDeepCopyPersistedListsAcrossBothStateTransferDirections()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
                DataVisualizerUserState source = CreatePopulatedUserState();

                settings.HydrateFrom(source);
                Assert.IsTrue(EditorUtility.IsDirty(settings));
                MutatePopulatedUserState(source);

                DataVisualizerUserState firstCopy = new();
                firstCopy.HydrateFrom(settings);
                AssertPopulatedUserState(firstCopy);
                MutatePopulatedUserState(firstCopy);

                DataVisualizerUserState secondCopy = new();
                secondCopy.HydrateFrom(settings);
                AssertPopulatedUserState(secondCopy);
            }
        }

        [Test]
        public void ShouldNormalizeNullPersistedListsToEmptyListsWhenTransferringState()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
                DataVisualizerUserState source = new()
                {
                    namespaceOrder = null,
                    typeOrders = null,
                    lastObjectSelections = null,
                    namespaceCollapseStates = null,
                    objectOrders = null,
                    managedTypeNames = null,
                    labelFilterConfigs = null,
                    processorStates = null,
                };

                settings.HydrateFrom(source);
                DataVisualizerUserState copy = new();
                copy.HydrateFrom(settings);

                Assert.IsEmpty(copy.namespaceOrder);
                Assert.IsEmpty(copy.typeOrders);
                Assert.IsEmpty(copy.lastObjectSelections);
                Assert.IsEmpty(copy.namespaceCollapseStates);
                Assert.IsEmpty(copy.objectOrders);
                Assert.IsEmpty(copy.managedTypeNames);
                Assert.IsEmpty(copy.labelFilterConfigs);
                Assert.IsEmpty(copy.processorStates);
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
