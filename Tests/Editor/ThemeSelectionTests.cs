namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;

    public sealed class ThemeSelectionTests
    {
        private static DataVisualizerThemeSettings CreateTheme(
            TestCleanupScope cleanup,
            StyleSheet sheet
        )
        {
            DataVisualizerThemeSettings theme =
                ScriptableObject.CreateInstance<DataVisualizerThemeSettings>();
            cleanup.Defer(() => Object.DestroyImmediate(theme));
            using SerializedObject serialized = new(theme);
            serialized.FindProperty("styleSheet").objectReferenceValue = sheet;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return theme;
        }

        private static StyleSheet CreateSheet(TestCleanupScope cleanup)
        {
            StyleSheet sheet = ScriptableObject.CreateInstance<StyleSheet>();
            cleanup.Defer(() => Object.DestroyImmediate(sheet));
            return sheet;
        }

        [TestCase("")]
        [TestCase("missing-theme-guid")]
        [TestCase("0123456789abcdef0123456789abcdef")]
        public void ShouldPreserveThemeGuidThroughBothPersistenceModesAndJson(string guid)
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => Object.DestroyImmediate(settings));
            DataVisualizerUserState state = new() { themeGuid = guid };
            settings.HydrateFrom(state);
            Assert.AreEqual(guid, settings.themeGuid);
            DataVisualizerUserState restored = new();
            restored.HydrateFrom(settings);
            restored = DataVisualizerUserState.FromJson(JsonUtility.ToJson(restored));
            Assert.AreEqual(guid, restored.themeGuid);
            Assert.IsNull(DataVisualizerThemeSelection.Resolve(restored.themeGuid));
            Assert.AreEqual(guid, restored.themeGuid);
        }

        [Test]
        public void ShouldUseDefaultThemeWhenLoadingLegacyUserState()
        {
            DataVisualizerUserState state = DataVisualizerUserState.FromJson("{}");
            Assert.IsTrue(string.IsNullOrEmpty(state.themeGuid));
            Assert.IsNull(DataVisualizerThemeSelection.Resolve(state.themeGuid));
        }

        [Test]
        public void ShouldReplaceAndClearOnlyOwnedStylesheetWhenThemeChanges()
        {
            using TestCleanupScope cleanup = new();
            StyleSheet baseSheet = CreateSheet(cleanup);
            StyleSheet firstSheet = CreateSheet(cleanup);
            StyleSheet secondSheet = CreateSheet(cleanup);
            DataVisualizerThemeSettings first = CreateTheme(cleanup, firstSheet);
            DataVisualizerThemeSettings second = CreateTheme(cleanup, secondSheet);
            DataVisualizerThemeSettings empty = CreateTheme(cleanup, null);
            VisualElement root = new();
            root.styleSheets.Add(baseSheet);
            DataVisualizerThemeSelection selection = new();

            selection.Apply(root, first);
            selection.Apply(root, first);
            Assert.AreEqual(2, root.styleSheets.count);
            Assert.AreSame(firstSheet, root.styleSheets[1]);
            selection.Apply(root, second);
            Assert.IsFalse(root.styleSheets.Contains(firstSheet));
            Assert.AreSame(secondSheet, root.styleSheets[1]);
            selection.Apply(root, empty);
            Assert.AreEqual(1, root.styleSheets.count);
            Assert.AreSame(baseSheet, root.styleSheets[0]);
            selection.Apply(root, first);
            selection.Apply(root, null);
            Assert.AreEqual(1, root.styleSheets.count);
            Assert.AreSame(baseSheet, root.styleSheets[0]);
        }

        [Test]
        public void ShouldPreserveExistingSheetWhenThemeReferencesBaseSheet()
        {
            using TestCleanupScope cleanup = new();
            StyleSheet baseSheet = CreateSheet(cleanup);
            DataVisualizerThemeSettings theme = CreateTheme(cleanup, baseSheet);
            VisualElement root = new();
            root.styleSheets.Add(baseSheet);
            DataVisualizerThemeSelection selection = new();
            selection.Apply(root, theme);
            selection.Apply(root, null);
            Assert.AreEqual(1, root.styleSheets.count);
            Assert.AreSame(baseSheet, root.styleSheets[0]);
        }

        [Test]
        public void ShouldRemovePreviousRootOverrideWhenRootChanges()
        {
            using TestCleanupScope cleanup = new();
            StyleSheet sheet = CreateSheet(cleanup);
            DataVisualizerThemeSettings theme = CreateTheme(cleanup, sheet);
            VisualElement first = new();
            VisualElement second = new();
            DataVisualizerThemeSelection selection = new();
            selection.Apply(first, theme);
            selection.Apply(second, theme);
            Assert.AreEqual(0, first.styleSheets.count);
            Assert.AreEqual(1, second.styleSheets.count);
            selection.Apply(null, null);
            Assert.AreEqual(0, second.styleSheets.count);
        }
    }
}
