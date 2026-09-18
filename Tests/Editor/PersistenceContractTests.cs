namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class PersistenceContractTests
    {
        private const string PreferenceKeyPrefix = "WallstopStudios.Editor.DataVisualizer.";

        private static readonly string[] ExpectedPreferenceKeyValues =
        {
            "WallstopStudios.Editor.DataVisualizer.InitialSizeApplied",
            "WallstopStudios.Editor.DataVisualizer.PreferredWindowSize",
            "WallstopStudios.Editor.DataVisualizer.SplitterInnerFixedPaneWidth",
            "WallstopStudios.Editor.DataVisualizer.SplitterOuterFixedPaneWidth",
            "WallstopStudios.Editor.DataVisualizer.TemporaryWindowClampSize",
        };

        private static readonly string[] ExpectedSettingsAssetFieldNames =
        {
            "_dataFolderPath",
            "labelFilterConfigs",
            "lastObjectSelections",
            "lastSelectedNamespaceKey",
            "lastSelectedTypeFullName",
            "managedTypeNames",
            "namespaceCollapseStates",
            "namespaceOrder",
            "objectOrders",
            "persistStateInSettingsAsset",
            "processorStates",
            "selectActiveObject",
            "themeGuid",
            "typeOrders",
        };

        private static readonly string[] ExpectedUserStateFieldNames =
        {
            "labelFilterConfigs",
            "lastObjectSelections",
            "lastSelectedNamespaceKey",
            "lastSelectedTypeFullName",
            "managedTypeNames",
            "namespaceCollapseStates",
            "namespaceOrder",
            "objectOrders",
            "processorStates",
            "themeGuid",
            "typeOrders",
        };

        private static List<string> CollectPreferenceKeyValues()
        {
            List<string> preferenceKeyValues = new();
            FieldInfo[] fields = typeof(DataVisualizerWindow).GetFields(
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Static
                    | BindingFlags.FlattenHierarchy
            );
            foreach (FieldInfo field in fields)
            {
                if (!field.IsLiteral || field.IsInitOnly || field.FieldType != typeof(string))
                {
                    continue;
                }

                string value = (string)field.GetValue(null);
                if (
                    value.StartsWith(PreferenceKeyPrefix, StringComparison.Ordinal)
                    && PreferenceKeyPrefix.Length < value.Length
                )
                {
                    preferenceKeyValues.Add(value);
                }
            }

            preferenceKeyValues.Sort(StringComparer.Ordinal);
            return preferenceKeyValues;
        }

        private static List<string> CollectUserStateFieldNames()
        {
            List<string> fieldNames = new();
            FieldInfo[] fields = typeof(DataVisualizerUserState).GetFields(
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly
            );
            foreach (FieldInfo field in fields)
            {
                if (field.IsStatic || field.IsInitOnly || field.IsLiteral || !field.IsPublic)
                {
                    continue;
                }

                fieldNames.Add(field.Name);
            }

            fieldNames.Sort(StringComparer.Ordinal);
            return fieldNames;
        }

        private static string ReadConstString(Type declaringType, string fieldName)
        {
            FieldInfo field = declaringType.GetField(
                fieldName,
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Static
                    | BindingFlags.FlattenHierarchy
            );
            return field?.GetValue(null) as string;
        }

        /*
            Unity serializes m_-prefixed bookkeeping properties (and hideFlags) on every
            ScriptableObject; they are not package-owned state and their set varies by Unity
            version, so they are excluded like PR #88 excluded compiler-generated types from
            the declared-type surface.
        */
        private static bool IsUnityBookkeepingProperty(string propertyName)
        {
            return propertyName.StartsWith("m_", StringComparison.Ordinal)
                || string.Equals(propertyName, "hideFlags", StringComparison.Ordinal);
        }

        private static void AssertContractSurface(
            IReadOnlyList<string> expectedNames,
            List<string> actualNames,
            string surfaceName
        )
        {
            HashSet<string> actual = new(actualNames, StringComparer.Ordinal);
            HashSet<string> expected = new(expectedNames, StringComparer.Ordinal);
            List<string> missing = new();
            List<string> extra = new();
            foreach (string expectedName in expected)
            {
                if (!actual.Contains(expectedName))
                {
                    missing.Add(expectedName);
                }
            }

            foreach (string actualName in actual)
            {
                if (!expected.Contains(actualName))
                {
                    extra.Add(actualName);
                }
            }

            if (missing.Count == 0 && extra.Count == 0)
            {
                return;
            }

            missing.Sort(StringComparer.Ordinal);
            extra.Sort(StringComparer.Ordinal);
            Assert.Fail(
                $"Persisted {surfaceName} contract drifted. Missing: [{string.Join(", ", missing)}]. Extra: [{string.Join(", ", extra)}]. Update PersistenceContractTests deliberately when the persistence contract changes."
            );
        }

        [Test]
        public void ShouldPreserveEditorPreferenceKeyValuesWhenWindowStatePersists()
        {
            Assert.AreEqual(
                PreferenceKeyPrefix,
                ReadConstString(typeof(DataVisualizerWindow), "PrefsPrefix"),
                "EditorPrefs key prefix changed; persisted user settings (window sizes, splitters) would reset on upgrade. Update PersistenceContractTests deliberately with a migration story."
            );

            AssertContractSurface(
                ExpectedPreferenceKeyValues,
                CollectPreferenceKeyValues(),
                "EditorPrefs keys"
            );
        }

        [Test]
        public void ShouldPreservePersistedStateLocationsWhenPackageResolvesStatePaths()
        {
            Assert.AreEqual(
                "Assets/Editor/DataVisualizerSettings.asset",
                ReadConstString(typeof(DataVisualizerWindow), "SettingsDefaultPath"),
                "Settings asset path drifted; existing project settings would be orphaned on upgrade. Update PersistenceContractTests deliberately with a migration story."
            );
            Assert.AreEqual(
                "DataVisualizerUserState.json",
                ReadConstString(typeof(DataVisualizerWindow), "UserStateFileName"),
                "User state file name drifted; existing per-user state would be orphaned on upgrade. Update PersistenceContractTests deliberately with a migration story."
            );
        }

        [Test]
        public void ShouldExposeOnlyDeclaredFieldsWhenSettingsAssetIsSerialized()
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));

            List<string> actualFieldNames = new();
            SerializedProperty property = new SerializedObject(settings).GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = false;
                if (property.depth != 0 || IsUnityBookkeepingProperty(property.name))
                {
                    continue;
                }

                actualFieldNames.Add(property.name);
            }

            AssertContractSurface(
                ExpectedSettingsAssetFieldNames,
                actualFieldNames,
                "settings asset serialized fields"
            );
        }

        [Test]
        public void ShouldExposeOnlyDeclaredFieldsWhenUserStateFormatIsEnumerated()
        {
            AssertContractSurface(
                ExpectedUserStateFieldNames,
                CollectUserStateFieldNames(),
                "user state persisted fields"
            );
        }

        [Test]
        public void ShouldPreservePopulatedValuesWhenUserStateRoundTripsThroughJson()
        {
            DataVisualizerUserState populated = SettingsPersistenceTests.CreatePopulatedUserState();
            populated.lastSelectedNamespaceKey = "Gameplay";
            populated.lastSelectedTypeFullName = "Example.GameplayData";

            string savedJson = JsonUtility.ToJson(populated);
            DataVisualizerUserState loaded = DataVisualizerUserState.FromJson(savedJson);

            Assert.That(loaded != null);
            Assert.AreEqual(
                savedJson,
                JsonUtility.ToJson(loaded),
                "User state JSON changed across a save/load round trip; a persisted value no longer survives the current format."
            );
            SettingsPersistenceTests.AssertPopulatedUserState(loaded);
        }
    }
}
