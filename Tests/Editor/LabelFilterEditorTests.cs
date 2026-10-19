namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using WallstopStudios.DataVisualizer.Editor.UI;

    public sealed class LabelFilterEditorTests
    {
        private static IEnumerable<TestCaseData> SavedFilters()
        {
            foreach (int combination in new[] { 0, 1, 2 })
            {
                for (int clauses = 0; clauses < 4; clauses++)
                {
                    yield return new TestCaseData(combination, clauses).SetName($"SavedFilterCombination{combination}Clauses{clauses}");
                }
            }
        }

        private static Button Pill(VisualElement editor, string label)
        {
            foreach (VisualElement element in editor.Q("simple-label-pills").Children())
            {
                if (element is Button button && string.Equals(button.userData as string, label, StringComparison.Ordinal))
                {
                    return button;
                }
            }
            Assert.Fail("Missing label pill: " + label);
            return null;
        }

        private static void Submit(Button button)
        {
            button.Focus();
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        [TestCaseSource(nameof(SavedFilters))]
        public void ShouldPreserveSavedExpressionsWhenBindingAndOpeningAdvanced(int combination, int clauses)
        {
            TypeLabelFilterConfig config = new()
            {
                combinationType = (LabelCombinationType)combination,
                andLabels = clauses % 2 == 1 ? new List<string> { "Urgent", "urgent", "Missing" } : new(),
                orLabels = 2 <= clauses ? new List<string> { "Armor", "Weapon" } : new(),
                isCollapsed = true,
                isAdvancedCollapsed = true,
            };
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            string saved = JsonUtility.ToJson(config);
            LabelFilterEditor editor = new(new VisualElement(), () => Assert.Fail("Presentation must not write saved filters."));
            window.rootVisualElement.Add(editor);
            editor.Bind(JsonUtility.FromJson<TypeLabelFilterConfig>(saved), new[] { "Urgent", "urgent", "Armor" });
            DropdownField mode = editor.Q<DropdownField>("label-filter-mode");
            string expected =
                clauses == 3 ? LabelFilterEditor.AdvancedMode
                : clauses == 2 ? LabelFilterEditor.AnyMode
                : LabelFilterEditor.AllMode;
            Assert.That(mode.value, Is.EqualTo(expected));
            editor.Bind(config, Array.Empty<string>());
            mode.value = LabelFilterEditor.AdvancedMode;
            editor.Bind(config, Array.Empty<string>());
            Assert.That(mode.value, Is.EqualTo(LabelFilterEditor.AdvancedMode));
            Assert.That(JsonUtility.ToJson(config), Is.EqualTo(saved));
            if (clauses == 3)
            {
                mode.value = LabelFilterEditor.AllMode;
                Assert.That(mode.value, Is.EqualTo(LabelFilterEditor.AdvancedMode));
                Assert.That(JsonUtility.ToJson(config), Is.EqualTo(saved));
                Assert.That(editor.Q<Label>("label-filter-summary").text, Does.Contain("Missing"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldKeepAdvancedWhileEditingAnExistingCompoundFilter(bool attemptSimpleMode)
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            TypeLabelFilterConfig config = new()
            {
                andLabels = new() { "Reviewed" },
                orLabels = new() { "Urgent" },
            };
            VisualElement advanced = new();
            LabelFilterEditor editor = new(advanced, () => Assert.Fail("Changing presentation must not write filter state."));
            window.rootVisualElement.Add(editor);
            editor.Bind(config, new[] { "Reviewed", "Urgent" });
            DropdownField mode = editor.Q<DropdownField>("label-filter-mode");
            if (attemptSimpleMode)
            {
                mode.value = LabelFilterEditor.AnyMode;
            }
            config.orLabels.Clear();
            editor.Bind(config, new[] { "Reviewed", "Urgent" });
            Assert.That(mode.value, Is.EqualTo(LabelFilterEditor.AdvancedMode));
            Assert.That(advanced.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            mode.value = LabelFilterEditor.AllMode;
            Assert.That(advanced.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator ShouldAddSwitchRemoveAndClearLabelsWithKeyboard()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            window.position = new Rect(0, 0, 280, 300);
            TypeLabelFilterConfig config = new();
            int saves = 0;
            LabelFilterEditor editor = new(new VisualElement(), () => saves++);
            window.rootVisualElement.Add(editor);
            editor.Bind(config, new[] { "Urgent", "urgent" });
            EditorSurfaceCapture.SettleLayout(window);

            Submit(Pill(editor, "Urgent"));
            yield return null;
            Assert.That(config.andLabels, Is.EqualTo(new[] { "Urgent" }));
            Assert.That(LabelFilterEvaluator.Matches(new[] { "urgent" }, config), Is.False);
            Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(Pill(editor, "Urgent")));
            Submit(Pill(editor, "urgent"));
            yield return null;
            Assert.That(LabelFilterEvaluator.Matches(new[] { "Urgent" }, config), Is.False);

            editor.Q<DropdownField>("label-filter-mode").value = LabelFilterEditor.AnyMode;
            yield return null;
            Assert.That(config.andLabels, Is.Empty);
            Assert.That(config.orLabels, Is.EqualTo(new[] { "Urgent", "urgent" }));
            Assert.That(LabelFilterEvaluator.Matches(new[] { "Urgent" }, config), Is.True);
            Submit(Pill(editor, "Urgent"));
            yield return null;
            Assert.That(config.orLabels, Is.EqualTo(new[] { "urgent" }));
            Submit(editor.Q<Button>("clear-label-filter"));
            yield return null;
            Assert.That(config.andLabels, Is.Empty);
            Assert.That(config.orLabels, Is.Empty);
            Assert.That(saves, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator ShouldFitSimpleControlsInNarrowPanesAcrossThemes()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            window.position = new Rect(0, 0, 220, 400);
            VisualElement root = window.rootVisualElement;
            root.AddToClassList("dataviz-root");
            const string styles = "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/";
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(styles + "DataVisualizerStyles.uss"));
            LabelFilterEditor editor = new(new VisualElement(), () => { });
            root.Add(editor);
            editor.Bind(new TypeLabelFilterConfig(), new[] { "A label with a very long descriptive name", "Urgent" });
            DataVisualizerThemeSelection selection = new();
            cleanup.Defer(() => selection.Apply(null, null));
            foreach (string theme in new[] { "Dx", "Classic", "Nord", "Dracula", "Compact", "Minimal" })
            {
                selection.Apply(root, AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(styles + theme + ".asset"));
                yield return null;
                EditorSurfaceCapture.SettleLayout(window);
                foreach (Button button in editor.Query<Button>().ToList())
                {
                    Assert.That(0 < button.worldBound.height, theme);
                    Assert.That(editor.worldBound.xMin <= button.worldBound.xMin, theme);
                    Assert.That(button.worldBound.xMax <= editor.worldBound.xMax + 1, theme);
                }
            }
        }

        [Test]
        public void ShouldKeepMissingActiveLabelsAvailableForRemoval()
        {
            TypeLabelFilterConfig config = new() { andLabels = new List<string> { "Missing" } };
            LabelFilterEditor editor = new(new VisualElement(), () => { });
            editor.Bind(config, Array.Empty<string>());
            Assert.That(Pill(editor, "Missing").text, Does.Contain("×"));
            Assert.That(editor.Q<Label>("label-filter-summary").text, Does.Contain("Missing"));
            Assert.That(LabelFilterEvaluator.Matches(Array.Empty<string>(), config), Is.False);
        }
    }
}
