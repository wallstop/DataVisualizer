namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.UI;

    public sealed class ResponsivePaneLayoutTests
    {
        [TestCase(360f, 480f, 800f, 600f)]
        [TestCase(1200f, 800f, 350f, 150f)]
        [TestCase(600f, 480f, 600f, 480f)]
        public void ShouldKeepPopoverInsideNarrowAndWideWindows(float width, float height, float popoverWidth, float popoverHeight)
        {
            Rect fitted = PopoverPlacement.FitToWindow(new Rect(width - 10f, height - 10f, popoverWidth, popoverHeight), new Vector2(width, height));
            Assert.That(0f <= fitted.xMin && 0f <= fitted.yMin);
            Assert.That(fitted.xMax <= width && fitted.yMax <= height);
            Assert.That(fitted.width, Is.EqualTo(Mathf.Min(width, popoverWidth)));
            Assert.That(fitted.height, Is.EqualTo(Mathf.Min(height, popoverHeight)));
        }

        [UnityTest]
        public IEnumerator ShouldRestorePreferredWidthsAfterRepeatedClampingAndNavigation()
        {
            using TestCleanupScope cleanup = new();
            LayoutTestWindow window = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(window.Close);
            window.position = new Rect(40, 40, 1600, 600);
            VisualElement root = window.rootVisualElement;
            VisualElement navigation = new() { style = { flexDirection = FlexDirection.Row } };
            VisualElement types = new() { style = { minWidth = ResponsivePaneLayout.CatalogMinimum } };
            VisualElement objects = new() { style = { minWidth = ResponsivePaneLayout.ObjectsMinimum } };
            VisualElement inspector = new() { style = { minWidth = ResponsivePaneLayout.InspectorMinimum } };
            TestDataObject data = ScriptableObject.CreateInstance<TestDataObject>();
            cleanup.Defer(() => Object.DestroyImmediate(data));
            Editor ordinaryEditor = Editor.CreateEditor(data);
            cleanup.Defer(() => Object.DestroyImmediate(ordinaryEditor));
            Editor customEditor = Editor.CreateEditor(data, typeof(ResponsiveTestInspectorEditor));
            cleanup.Defer(() => Object.DestroyImmediate(customEditor));
            inspector.Add(new InspectorElement(ordinaryEditor));
            inspector.Add(customEditor.CreateInspectorGUI());
            TextField customField = inspector.Q<TextField>("custom-inspector-field");
            TwoPaneSplitView inner = new(0, 600, TwoPaneSplitViewOrientation.Horizontal) { style = { flexGrow = 1 } };
            inner.Add(objects);
            inner.Add(inspector);
            TwoPaneSplitView outer = new(0, 400, TwoPaneSplitViewOrientation.Horizontal) { style = { flexGrow = 1 } };
            outer.Add(types);
            outer.Add(inner);
            root.Add(navigation);
            root.Add(outer);
            ResponsivePaneLayout layout = new(outer, inner, navigation);
            window.ShowUtility();
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            Vector2 preferred = new(400, 600);

            for (int cycle = 0; cycle < 3; cycle++)
            {
                root.style.width = 1200;
                layout.Resize(1200, preferred);
                EditorSurfaceCapture.SettleLayout(window);
                Assert.That(layout.CanSaveWidths, Is.False, "clamped widths must not replace preferences");
                Assert.That(inspector.resolvedStyle.width, Is.GreaterThanOrEqualTo(ResponsivePaneLayout.InspectorMinimum));

                foreach (float width in new[] { 600f, 360f })
                {
                    customField.Focus();
                    root.style.width = width;
                    layout.Resize(width, preferred);
                    EditorSurfaceCapture.SettleLayout(window);
                    VisualElement focused = root.focusController.focusedElement as VisualElement;
                    Assert.That(focused != null);
                    for (VisualElement ancestor = focused; ancestor != null; ancestor = ancestor.parent)
                    {
                        Assert.That(ancestor.resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None), "resize must not strand focus in a hidden pane");
                    }
                    Assert.That(layout.IsCompact, Is.True);
                    Assert.That(layout.CanSaveWidths, Is.False);
                    foreach (PaneNavigation pane in new[] { PaneNavigation.Types, PaneNavigation.Objects, PaneNavigation.Inspector })
                    {
                        layout.Show(pane);
                        EditorSurfaceCapture.SettleLayout(window);
                        VisualElement visible =
                            pane == PaneNavigation.Types ? types
                            : pane == PaneNavigation.Objects ? objects
                            : inspector;
                        Assert.That(visible.resolvedStyle.width, Is.EqualTo(width).Within(1f));
                        Assert.That(visible.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                        Assert.That(root.focusController.focusedElement, Is.SameAs(visible));
                    }
                    Assert.That(customField.value, Is.EqualTo("The value must survive pane changes"));
                }

                root.style.width = 1600;
                layout.Resize(1600, preferred);
                EditorSurfaceCapture.SettleLayout(window);
                Assert.That(layout.CanSaveWidths, Is.True);
                Assert.That(types.resolvedStyle.width, Is.EqualTo(preferred.x).Within(1f));
                Assert.That(objects.resolvedStyle.width, Is.EqualTo(preferred.y).Within(1f));
                layout.Show(PaneNavigation.Types);
                EditorSurfaceCapture.SettleLayout(window);
                Assert.That(types.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(layout.CanSaveWidths, Is.False);
                layout.Show(PaneNavigation.Types);
                EditorSurfaceCapture.SettleLayout(window);
                Assert.That(types.resolvedStyle.width, Is.EqualTo(preferred.x).Within(1f));
            }
        }
    }
}
