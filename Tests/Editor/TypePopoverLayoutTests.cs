namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Styles;

    public sealed class TypePopoverLayoutTests
    {
        private const string DataVisualizerStyleSheetPath =
            "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/DataVisualizerStyles.uss";
        private const string PlaceholderClass = "unity-text-field__placeholder";
        private const float LayoutTolerance = 0.01f;

        private static readonly string[] SearchFieldClassNames =
        {
            "type-add-search-field",
            "type-search-field",
        };

        private static Label CreateTypeRow(string text)
        {
            Label row = new(text);
            row.AddToClassList("type-selection-list-item");
            return row;
        }

        private static LayoutTestWindow CreateWindow(
            StyleSheet styleSheet,
            float width,
            float height
        )
        {
            LayoutTestWindow window = EditorWindow.CreateWindow<LayoutTestWindow>();
            window.titleContent = new GUIContent(nameof(TypePopoverLayoutTests));
            window.position = new Rect(100, 100, width, height);
            window.rootVisualElement.styleSheets.Add(styleSheet);
            window.rootVisualElement.style.width = width;
            window.rootVisualElement.style.height = height;
            window.ShowUtility();
            return window;
        }

        private static StyleSheet LoadStyleSheet()
        {
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                DataVisualizerStyleSheetPath
            );
            if (styleSheet != null)
            {
                return styleSheet;
            }

            return FindStyleSheet();
        }

        private static StyleSheet FindStyleSheet()
        {
            string[] styleSheetGuids = AssetDatabase.FindAssets(
                "DataVisualizerStyles t:StyleSheet"
            );
            for (int index = 0; index < styleSheetGuids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(styleSheetGuids[index]);
                if (
                    !path.EndsWith(
                        "/Editor/DataVisualizer/Styles/DataVisualizerStyles.uss",
                        System.StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                StyleSheet candidate = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static IEnumerator WaitForResolvedHeights(params VisualElement[] elements)
        {
            float[] previousHeights = null;
            for (int frame = 0; frame < 30; frame++)
            {
                yield return null;

                bool allReady = true;
                for (int index = 0; index < elements.Length; index++)
                {
                    VisualElement element = elements[index];
                    if (element?.panel == null || !IsPositiveFinite(element.resolvedStyle.height))
                    {
                        allReady = false;
                        break;
                    }
                }

                if (!allReady)
                {
                    previousHeights = null;
                    continue;
                }

                float[] currentHeights = new float[elements.Length];
                for (int index = 0; index < elements.Length; index++)
                {
                    currentHeights[index] = elements[index].resolvedStyle.height;
                }

                bool allStable = previousHeights != null;
                for (int index = 0; allStable && index < currentHeights.Length; index++)
                {
                    if (Mathf.Abs(currentHeights[index] - previousHeights[index]) > LayoutTolerance)
                    {
                        allStable = false;
                    }
                }

                if (allStable)
                {
                    yield break;
                }

                previousHeights = currentHeights;
            }

            List<string> heightDescriptions = new(elements.Length);
            for (int index = 0; index < elements.Length; index++)
            {
                VisualElement element = elements[index];
                heightDescriptions.Add(
                    element == null
                        ? "<null>"
                        : $"{element.name}:{element.resolvedStyle.height} panel={element.panel != null}"
                );
            }
            string heightSummary = string.Join(", ", heightDescriptions);
            Assert.Fail($"Timed out waiting for stable positive layout heights: {heightSummary}");
        }

        private static bool IsPositiveFinite(float value)
        {
            return 0 < value && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        [UnityTest]
        public IEnumerator ShouldKeepSearchFieldHeightWhenPlaceholderChangesToTypedText()
        {
            StyleSheet styleSheet = LoadStyleSheet();
            Assert.NotNull(styleSheet);
            LayoutTestWindow window = CreateWindow(styleSheet, 360, 160);
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(window.Close);
                TextField[] fields = new TextField[SearchFieldClassNames.Length];
                for (int index = 0; index < SearchFieldClassNames.Length; index++)
                {
                    string className = SearchFieldClassNames[index];
                    TextField field = new() { name = className };
                    field.AddToClassList(className);
                    window.rootVisualElement.Add(field);
                    fields[index] = field;
                }

                yield return WaitForResolvedHeights(fields);

                foreach (TextField field in fields)
                {
                    field.SetValueWithoutNotify("Search...");
                    field.AddToClassList(PlaceholderClass);
                }

                yield return WaitForResolvedHeights(fields);

                foreach (TextField field in fields)
                {
                    float placeholderHeight = field.resolvedStyle.height;
                    field.RemoveFromClassList(PlaceholderClass);
                    field.SetValueWithoutNotify("base");

                    yield return WaitForResolvedHeights(field);

                    Assert.That(
                        field.resolvedStyle.height,
                        Is.EqualTo(placeholderHeight).Within(LayoutTolerance),
                        $"{field.name} height changed when placeholder text was replaced."
                    );
                }
            }
        }

        [UnityTest]
        public IEnumerator ShouldKeepTypeRowHeightWhenFilterChangesVisibleRows()
        {
            StyleSheet styleSheet = LoadStyleSheet();
            Assert.NotNull(styleSheet);
            LayoutTestWindow window = CreateWindow(styleSheet, 360, 180);
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(window.Close);
                VisualElement container = new()
                {
                    name = "type-add-list-content",
                    style = { height = 120 },
                };
                container.AddToClassList("type-add-list-container");
                window.rootVisualElement.Add(container);

                VisualElement header = new();
                header.AddToClassList("popover-namespace-header");
                header.style.width = 180;
                Label indicator = new(StyleConstants.ArrowExpanded);
                indicator.AddToClassList("popover-namespace-indicator");
                Label namespaceLabel = new(
                    "WallstopStudios.DataVisualizer.Tests.Editor.Generated.Namespace"
                );
                namespaceLabel.AddToClassList("type-selection-list-namespace");
                namespaceLabel.AddToClassList("type-selection-list-namespace--not-empty");
                header.Add(indicator);
                header.Add(namespaceLabel);

                Label firstRow = CreateTypeRow("BaseData");
                Label secondRow = CreateTypeRow("AdvancedData");
                container.Add(header);
                container.Add(firstRow);
                container.Add(secondRow);

                yield return WaitForResolvedHeights(header, namespaceLabel, firstRow, secondRow);

                float unfilteredHeaderHeight = header.resolvedStyle.height;
                float unfilteredNamespaceHeight = namespaceLabel.resolvedStyle.height;
                float unfilteredHeight = firstRow.resolvedStyle.height;
                Assert.That(
                    namespaceLabel.resolvedStyle.width,
                    Is.LessThanOrEqualTo(header.resolvedStyle.width)
                );

                container.Remove(secondRow);
                namespaceLabel.enableRichText = true;
                namespaceLabel.text =
                    "WallstopStudios.<color=yellow><b>DataVisualizer</b></color>.Tests.Editor.Generated.Namespace";
                firstRow.enableRichText = true;
                firstRow.text = "<color=yellow><b>Base</b></color>Data";

                yield return WaitForResolvedHeights(header, namespaceLabel, firstRow);

                Assert.That(
                    header.resolvedStyle.height,
                    Is.EqualTo(unfilteredHeaderHeight).Within(LayoutTolerance)
                );
                Assert.That(
                    namespaceLabel.resolvedStyle.height,
                    Is.EqualTo(unfilteredNamespaceHeight).Within(LayoutTolerance)
                );
                Assert.That(
                    firstRow.resolvedStyle.height,
                    Is.EqualTo(unfilteredHeight).Within(LayoutTolerance)
                );
            }
        }
    }
}
