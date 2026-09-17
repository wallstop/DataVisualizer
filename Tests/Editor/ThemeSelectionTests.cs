namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
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

        [UnityTest]
        public IEnumerator ShouldRenderShippedThemesAndRestoreClassicOnReset()
        {
            string folder =
                System
                    .IO.Path.GetDirectoryName(
                        AssetDatabase.GUIDToAssetPath("f3275f8fb8a4c4c388cf6982cfb86929")
                    )
                    .Replace('\\', '/') + "/";
            using TestCleanupScope cleanup = new();
            EditorWindow window = ScriptableObject.CreateInstance<EditorWindow>();
            cleanup.Defer(window.Close);
            window.Show();
            VisualElement root = window.rootVisualElement;
            root.styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>(folder + "DataVisualizerStyles.uss")
            );
            DataVisualizerThemeSelection selection = new();
            bool resetInvoked = false;
            Button reset = new(() =>
            {
                resetInvoked = true;
                selection.Apply(root, null);
            })
            {
                text = "Reset Theme",
            };
            reset.AddToClassList(StyleConstants.ThemeResetButtonClass);
            root.Add(reset);
            VisualElement namespaceRow = new();
            namespaceRow.AddToClassList(StyleConstants.NamespaceItemClass);
            root.Add(namespaceRow);
            string[] surfaces = { "3C3C3CFF", "3B4252FF", "343746FF" };
            string[] names = { "Classic", "Nord", "Dracula" };
            string[] accents = { "#d2691e", "#88c0d0", "#bd93f9" };
            float[] fontSizes = { 16, 15, 16 };
            float[] padding = { 6, 7, 8 };
            for (int index = 0; index < names.Length; index++)
            {
                DataVisualizerThemeSettings theme =
                    AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(
                        folder + names[index] + ".asset"
                    );
                Assert.IsNotNull(theme, names[index]);
                Assert.IsNotNull(theme.StyleSheet, names[index]);
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(theme));
                Assert.AreSame(theme, DataVisualizerThemeSelection.Resolve(guid));
                selection.Apply(root, theme);
                ColorUtility.TryParseHtmlString(accents[index], out Color accent);
                yield return new WaitForSecondsRealtime(0.5f);
                Assert.AreEqual(
                    ColorUtility.ToHtmlStringRGBA(accent),
                    ColorUtility.ToHtmlStringRGBA(reset.resolvedStyle.color),
                    names[index]
                );
                Assert.AreEqual(
                    surfaces[index],
                    ColorUtility.ToHtmlStringRGBA(namespaceRow.resolvedStyle.backgroundColor),
                    names[index]
                );
                Assert.AreEqual(fontSizes[index], reset.resolvedStyle.fontSize, names[index]);
                Assert.AreEqual(padding[index], reset.resolvedStyle.paddingTop, names[index]);
                Assert.AreEqual(Align.FlexEnd, reset.resolvedStyle.alignSelf);
            }

            ColorUtility.TryParseHtmlString("#282a36", out Color focusedOnAccent);
            reset.Focus();
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(focusedOnAccent, reset.resolvedStyle.color, "focused");
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = reset;
                reset.SendEvent(submit);
            }
            double deadline = EditorApplication.timeSinceStartup + 3;
            while (!resetInvoked && EditorApplication.timeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.IsTrue(resetInvoked, "The reset click callback must run.");
            Assert.IsFalse(
                root.styleSheets.Contains(
                    AssetDatabase.LoadAssetAtPath<StyleSheet>(folder + "Dracula.uss")
                )
            );
            reset.Blur();
            yield return new WaitForSecondsRealtime(0.5f);
            ColorUtility.TryParseHtmlString("#d2691e", out Color classic);
            Assert.AreEqual(classic, reset.resolvedStyle.color);
            Assert.IsTrue(
                root.styleSheets.Contains(
                    AssetDatabase.LoadAssetAtPath<StyleSheet>(folder + "DataVisualizerStyles.uss")
                )
            );
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
