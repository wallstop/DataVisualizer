namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class ObjectRowLayoutTests
    {
        [UnityTest]
        public IEnumerator ShouldKeepCompactRowsAndControlsInsideEveryShippedTheme()
        {
            using TestCleanupScope cleanup = new();
            FieldInfo instanceField = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previousInstance = instanceField.GetValue(null);
            cleanup.Defer(() => instanceField.SetValue(null, previousInstance));
            DataVisualizerWindow owner = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => Object.DestroyImmediate(owner));
            owner.CreateGUI();
            owner.OpenTypeSearchPopover();
            Assert.That(owner.rootVisualElement.Q("type-add-list-content").childCount, Is.Positive, "first Search Types open must build content");
            owner.OpenTypeSearchPopover();
            Assert.That(owner.rootVisualElement.Q("type-add-list-content").childCount, Is.Positive, "immediate reopen must rebuild content");
            LayoutTestWindow window = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(window.Close);
            window.position = new Rect(100, 100, 700, 200);
            VisualElement root = window.rootVisualElement;
            root.AddToClassList("dataviz-root");
            const string folder = "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/";
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(folder + "DataVisualizerStyles.uss"));
            VisualElement first = owner.MakeObjectRow();
            VisualElement second = owner.MakeObjectRow();
            first.Q<Label>("object-item-label").text = "An intentionally long asset name with localized text: Objet très long";
            first.style.width = 220f;
            second.Q<Label>("object-item-label").text = "Second object";
            root.Add(first);
            root.Add(second);
            TextField assetSearch = new("Search all assets");
            assetSearch.AddToClassList("global-search-field");
            root.Add(assetSearch);
            int rowPointerEvents = 0;
            first.RegisterCallback<PointerDownEvent>(_ => rowPointerEvents++);
            using (PointerDownEvent pointer = PointerDownEvent.GetPooled())
            {
                first.Q<Button>("object-actions-button").SendEvent(pointer);
            }
            Assert.That(rowPointerEvents, Is.Zero, "Opening row actions must not select the row");
            DataVisualizerThemeSelection selection = new();
            cleanup.Defer(() => selection.Apply(null, null));
            window.ShowUtility();

            string[] themes = { "Dx", "Classic", "Nord", "Dracula", "Compact", "Minimal" };
            foreach (string name in themes)
            {
                DataVisualizerThemeSettings theme = AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(folder + name + ".asset");
                Assert.That(theme != null, name);
                selection.Apply(root, theme);
                yield return null;
                EditorSurfaceCapture.SettleLayout(window);
                float searchLabelWidth = assetSearch
                    .labelElement.MeasureTextSize(assetSearch.label, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined)
                    .x;
                Assert.That(assetSearch.labelElement.worldBound.width, Is.GreaterThanOrEqualTo(searchLabelWidth), name + " search label must remain readable");
                VisualElement firstBox = first.Q("object-item-box");
                VisualElement secondBox = second.Q("object-item-box");
                float gap = secondBox.worldBound.yMin - firstBox.worldBound.yMax;
                Assert.That(0 <= gap, name + " cards must not overlap");
                if (name == "Dx")
                {
                    Assert.LessOrEqual(gap, 5f, "halve the previous 10-point default gap");
                }

                foreach (VisualElement row in new[] { first, second })
                {
                    Button actions = row.Q<Button>("object-actions-button");
                    Assert.That(actions.text, Is.EqualTo("Actions"));
                    Assert.That(actions.focusable, Is.True);
                    Assert.That(actions.tooltip, Does.Contain("Clone"));
                    Assert.That(row.Query<Button>().ToList().Count, Is.EqualTo(3));
                    VisualElement box = row.Q("object-item-box");
                    Assert.That(row.worldBound.yMin <= box.worldBound.yMin, name);
                    Assert.That(box.worldBound.yMax <= row.worldBound.yMax, name);
                    foreach (Button button in box.Query<Button>().ToList())
                    {
                        Assert.That(0 < button.worldBound.height, name);
                        Assert.That(box.worldBound.yMin <= button.worldBound.yMin, name);
                        Assert.That(button.worldBound.yMax <= box.worldBound.yMax, name);
                        Assert.That(box.worldBound.xMin <= button.worldBound.xMin, name);
                        Assert.That(button.worldBound.xMax <= box.worldBound.xMax, name);
                    }
                }
            }
        }
    }
}
