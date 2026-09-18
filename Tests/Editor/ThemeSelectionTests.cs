namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using WallstopStudios.DataVisualizer.Editor.UI;

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

        private static string GetThemeFolder()
        {
            string path = AssetDatabase.GUIDToAssetPath("0c7b089fc36e452daa680a6243ec49e9");
            Assert.IsFalse(string.IsNullOrEmpty(path), "The shipped base stylesheet must exist.");
            return System.IO.Path.GetDirectoryName(path).Replace('\\', '/') + "/";
        }

        private static ThemeDropdownItem[] CreateItems(TestCleanupScope cleanup)
        {
            DataVisualizerThemeSettings nord = CreateTheme(cleanup, null);
            DataVisualizerThemeSettings dracula = CreateTheme(cleanup, null);
            nord.name = "Nord";
            dracula.name = "Dracula";
            return new ThemeDropdownItem[]
            {
                new(null, string.Empty, "Classic (Default / Reset)"),
                new(nord, "Assets/ColdPalette/First.asset", "Nord"),
                new(dracula, "Packages/NightPalette/Second.asset", "Dracula"),
            };
        }

        private static void AssertHighlight(SearchableThemeDropdown dropdown, int expected)
        {
            Assert.AreEqual(expected, dropdown.HighlightedIndex);
            List<Button> rows = dropdown.Results.Query<Button>("theme-search-result").ToList();
            Assert.AreEqual(dropdown.FilteredItems.Count, rows.Count);
            for (int index = 0; index < rows.Count; index++)
            {
                Assert.AreEqual(
                    index == expected,
                    rows[index].ClassListContains(StyleConstants.SelectedClass),
                    $"Row {index} highlight"
                );
            }
        }

        private static void SendKey(VisualElement target, KeyCode key)
        {
            using KeyDownEvent evt = KeyDownEvent.GetPooled('\0', key, EventModifiers.None);
            evt.target = target;
            target.SendEvent(evt);
        }

        private static void RecordColor(
            List<string> failures,
            string theme,
            string property,
            string expected,
            Color actual
        )
        {
            string actualHex = ColorUtility.ToHtmlStringRGBA(actual);
            if (!string.Equals(expected, actualHex, System.StringComparison.Ordinal))
            {
                failures.Add($"{theme} {property}: expected {expected}, actual {actualHex}");
            }
        }

        [Test]
        public void ShouldDiscoverShippedAssetsWithDefaultResetFirstAndUniquePaths()
        {
            IReadOnlyList<ThemeDropdownItem> items = SearchableThemeDropdown.DiscoverItems();
            Assert.That(items.Count, Is.GreaterThanOrEqualTo(4));
            Assert.IsTrue(items[0].IsDefault);
            Assert.IsNull(items[0].Theme);
            Assert.AreEqual(string.Empty, items[0].Path);
            Assert.AreEqual("Classic (Default / Reset)", items[0].DisplayName);
            HashSet<string> paths = new(System.StringComparer.Ordinal);
            for (int index = 1; index < items.Count; index++)
            {
                ThemeDropdownItem item = items[index];
                Assert.IsFalse(item.IsDefault, item.DisplayName);
                Assert.IsTrue(paths.Add(item.Path), item.Path);
                Assert.AreEqual(AssetDatabase.GetAssetPath(item.Theme), item.Path);
                Assert.IsFalse(string.IsNullOrEmpty(item.DisplayName));
                if (1 < index)
                {
                    Assert.That(
                        System.StringComparer.OrdinalIgnoreCase.Compare(
                            items[index - 1].Theme.name,
                            item.Theme.name
                        ),
                        Is.LessThanOrEqualTo(0)
                    );
                }
            }

            string folder = GetThemeFolder();
            foreach (string name in new[] { "Classic", "Nord", "Dracula" })
            {
                string path = folder + name + ".asset";
                DataVisualizerThemeSettings theme =
                    AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(path);
                Assert.IsNotNull(theme, path);
                Assert.IsNotNull(theme.StyleSheet, path);
                ThemeDropdownItem[] matches = items.Where(item => item.Path == path).ToArray();
                Assert.AreEqual(1, matches.Length, path);
                Assert.AreSame(theme, matches[0].Theme, path);
                Assert.IsFalse(matches[0].IsDefault, path);
            }
        }

        [Test]
        public void ShouldNormalizeNullItemTextAndDistinguishResetFromThemeWithoutSheet()
        {
            using TestCleanupScope cleanup = new();
            ThemeDropdownItem reset = new(null, null, null);
            Assert.AreEqual(string.Empty, reset.Path);
            Assert.AreEqual(string.Empty, reset.DisplayName);
            Assert.IsTrue(reset.IsDefault);
            DataVisualizerThemeSettings theme = CreateTheme(cleanup, null);
            ThemeDropdownItem item = new(theme, "path", "name");
            Assert.AreSame(theme, item.Theme);
            Assert.IsFalse(item.IsDefault);
        }

        [TestCase("nOrD", 1)]
        [TestCase("  dRaCuLa  ", 2)]
        [TestCase("aSSETS/cOLDpALETTE", 1)]
        [TestCase("pACKAGES/nIGHTpALETTE/sECOND.aSSET", 2)]
        [TestCase("rEsEt", 0)]
        public void ShouldFilterNameAndPathIgnoringCaseAndSurroundingWhitespace(
            string query,
            int expectedIndex
        )
        {
            using TestCleanupScope cleanup = new();
            ThemeDropdownItem[] items = CreateItems(cleanup);
            SearchableThemeDropdown dropdown = new(items, null, null);
            dropdown.SetFilter(query);
            Assert.AreEqual(query, dropdown.Filter);
            CollectionAssert.AreEqual(new[] { items[expectedIndex] }, dropdown.FilteredItems);
            AssertHighlight(dropdown, 0);
            Button row = dropdown.Results.Q<Button>("theme-search-result");
            Assert.AreEqual(items[expectedIndex].DisplayName, row.text);
            Assert.AreEqual(
                expectedIndex == 0 ? "Restore the Classic appearance." : items[expectedIndex].Path,
                row.tooltip
            );
            Assert.IsNull(dropdown.Results.Q<Label>("theme-search-empty"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t ")]
        public void ShouldRestoreAllItemsWhenFilterIsEmpty(string query)
        {
            using TestCleanupScope cleanup = new();
            ThemeDropdownItem[] items = CreateItems(cleanup);
            SearchableThemeDropdown dropdown = new(items, items[2].Theme, null);
            AssertHighlight(dropdown, 2);
            dropdown.SetFilter("no matching palette");
            dropdown.SetFilter(query);
            Assert.AreEqual(query ?? string.Empty, dropdown.Filter);
            CollectionAssert.AreEqual(items, dropdown.FilteredItems);
            AssertHighlight(dropdown, 0);
            Assert.IsNull(dropdown.Results.Q<Label>("theme-search-empty"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldShowEmptyStateWithoutSelectionWhenNothingMatches(bool emptyItems)
        {
            using TestCleanupScope cleanup = new();
            ThemeDropdownItem[] items = emptyItems
                ? System.Array.Empty<ThemeDropdownItem>()
                : CreateItems(cleanup);
            int selections = 0;
            SearchableThemeDropdown dropdown = new(items, null, _ => selections++);
            if (!emptyItems)
            {
                dropdown.SetFilter("no matching palette");
            }
            Assert.IsEmpty(dropdown.FilteredItems);
            AssertHighlight(dropdown, -1);
            Assert.AreEqual(
                "No matching themes",
                dropdown.Results.Q<Label>("theme-search-empty")?.text
            );
            Assert.AreEqual(1, dropdown.Results.contentContainer.childCount);
            dropdown.MoveHighlight(int.MaxValue);
            dropdown.MoveHighlight(int.MinValue);
            AssertHighlight(dropdown, -1);
            Assert.IsFalse(dropdown.SelectHighlighted());
            Assert.IsFalse(dropdown.SelectFilteredIndex(-1));
            Assert.IsFalse(dropdown.SelectFilteredIndex(0));
            Assert.AreEqual(0, selections);
        }

        [Test]
        public void ShouldSelectFilteredThemeAndResetWithExactlyOneCallbackPerSelection()
        {
            using TestCleanupScope cleanup = new();
            ThemeDropdownItem[] items = CreateItems(cleanup);
            List<DataVisualizerThemeSettings> selections = new();
            int cancellations = 0;
            SearchableThemeDropdown dropdown = new(
                items,
                null,
                selections.Add,
                () => cancellations++
            );
            dropdown.SetFilter("NightPalette");
            Assert.IsFalse(dropdown.SelectFilteredIndex(-1));
            Assert.IsFalse(dropdown.SelectFilteredIndex(1));
            Assert.IsEmpty(selections);
            Assert.IsTrue(dropdown.SelectFilteredIndex(0));
            CollectionAssert.AreEqual(new[] { items[2].Theme }, selections);
            dropdown.SetFilter("Reset");
            Assert.IsTrue(dropdown.SelectHighlighted());
            CollectionAssert.AreEqual(
                new DataVisualizerThemeSettings[] { items[2].Theme, null },
                selections
            );
            Assert.AreEqual(0, cancellations);
        }

        [Test]
        public void ShouldCancelWithoutSelectingOrChangingHighlight()
        {
            using TestCleanupScope cleanup = new();
            ThemeDropdownItem[] items = CreateItems(cleanup);
            int selections = 0;
            int cancellations = 0;
            SearchableThemeDropdown dropdown = new(
                items,
                items[1].Theme,
                _ => selections++,
                () => cancellations++
            );
            dropdown.Cancel();
            Assert.AreEqual(1, cancellations);
            Assert.AreEqual(0, selections);
            AssertHighlight(dropdown, 1);
        }

        [Test]
        public void ShouldTitleDropdownAndUseClickableHoverCursorOnResultRows()
        {
            using TestCleanupScope cleanup = new();
            ThemeDropdownItem[] items = CreateItems(cleanup);
            SearchableThemeDropdown dropdown = new(items, null, null);
            Label title = dropdown.Q<Label>("theme-dropdown-title");
            Assert.IsNotNull(title);
            Assert.AreEqual("Select Theme", title.text);
            Assert.IsTrue(title.ClassListContains("theme-dropdown-title"));
            List<Button> rows = dropdown.Results.Query<Button>("theme-search-result").ToList();
            Assert.AreEqual(items.Length, rows.Count);
            foreach (Button row in rows)
            {
                Assert.IsTrue(row.ClassListContains(StyleConstants.ClickableClass), row.text);
                Assert.IsTrue(row.ClassListContains("theme-search-result"), row.text);
                Assert.AreEqual(StyleKeyword.Null, row.style.cursor.keyword, row.text);
            }

            dropdown.SetFilter("no matching palette");
            Label empty = dropdown.Results.Q<Label>("theme-search-empty");
            Assert.AreEqual("No matching themes", empty?.text);
        }

        [Test]
        public void ShouldClampHighlightAtBothBoundariesWithoutOverflowOrSelection()
        {
            using TestCleanupScope cleanup = new();
            ThemeDropdownItem[] items = CreateItems(cleanup);
            int selections = 0;
            SearchableThemeDropdown dropdown = new(items, items[1].Theme, _ => selections++);
            AssertHighlight(dropdown, 1);
            dropdown.MoveHighlight(int.MaxValue);
            AssertHighlight(dropdown, 2);
            dropdown.MoveHighlight(1);
            AssertHighlight(dropdown, 2);
            dropdown.MoveHighlight(int.MinValue);
            AssertHighlight(dropdown, 0);
            dropdown.MoveHighlight(-1);
            AssertHighlight(dropdown, 0);
            dropdown.MoveHighlight(1);
            AssertHighlight(dropdown, 1);
            dropdown.MoveHighlight(0);
            AssertHighlight(dropdown, 1);
            Assert.AreEqual(0, selections);
        }

        [UnityTest]
        public IEnumerator ShouldHandleArrowEnterAndEscapeKeysFromSearchField()
        {
            using TestCleanupScope cleanup = new();
            ThemeDropdownItem[] items = CreateItems(cleanup);
            LayoutTestWindow window = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(window.Close);
            List<DataVisualizerThemeSettings> selections = new();
            int cancellations = 0;
            SearchableThemeDropdown dropdown = new(
                items,
                null,
                selections.Add,
                () => cancellations++
            );
            window.rootVisualElement.Add(dropdown);
            window.ShowUtility();
            yield return null;
            Assert.IsNotNull(dropdown.panel);
            dropdown.SearchField.Focus();
            SendKey(dropdown.SearchField, KeyCode.UpArrow);
            AssertHighlight(dropdown, 0);
            SendKey(dropdown.SearchField, KeyCode.DownArrow);
            AssertHighlight(dropdown, 1);
            SendKey(dropdown.SearchField, KeyCode.DownArrow);
            SendKey(dropdown.SearchField, KeyCode.DownArrow);
            AssertHighlight(dropdown, 2);
            SendKey(dropdown.SearchField, KeyCode.UpArrow);
            AssertHighlight(dropdown, 1);
            Assert.IsEmpty(selections);
            foreach (KeyCode key in new[] { KeyCode.Return, KeyCode.KeypadEnter })
            {
                int previousCount = selections.Count;
                SendKey(dropdown.SearchField, key);
                Assert.AreEqual(previousCount + 1, selections.Count, key.ToString());
                Assert.AreSame(items[1].Theme, selections[previousCount]);
            }
            dropdown.SearchField.value = "Reset";
            CollectionAssert.AreEqual(new[] { items[0] }, dropdown.FilteredItems);
            SendKey(dropdown.SearchField, KeyCode.Return);
            Assert.AreEqual(3, selections.Count);
            Assert.IsNull(selections[2]);
            dropdown.SearchField.value = "no matching palette";
            SendKey(dropdown.SearchField, KeyCode.DownArrow);
            SendKey(dropdown.SearchField, KeyCode.UpArrow);
            AssertHighlight(dropdown, -1);
            SendKey(dropdown.SearchField, KeyCode.Return);
            SendKey(dropdown.SearchField, KeyCode.KeypadEnter);
            Assert.AreEqual(3, selections.Count);
            Assert.AreEqual(0, cancellations);
            SendKey(dropdown.SearchField, KeyCode.Escape);
            Assert.AreEqual(1, cancellations);
            Assert.AreEqual(3, selections.Count);
        }

        [UnityTest]
        public IEnumerator ShouldRenderControlColorsWhenSwitchingNordDraculaAndDefault()
        {
            using TestCleanupScope cleanup = new();
            string folder = GetThemeFolder();
            StyleSheet baseSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                folder + "DataVisualizerStyles.uss"
            );
            Assert.IsNotNull(baseSheet);
            LayoutTestWindow window = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(window.Close);
            VisualElement root = window.rootVisualElement;
            root.AddToClassList("dataviz-root");
            root.styleSheets.Add(baseSheet);
            Button button = new() { text = "Representative button" };
            TextField field = new("Representative field") { value = "Theme text" };
            VisualElement inspector = new() { style = { height = 30 } };
            inspector.AddToClassList("unity-inspector-element");
            Scroller scroller = new(0, 100, _ => { }, SliderDirection.Horizontal);
            HorizontalToggle toggle = new();
            root.Add(button);
            root.Add(field);
            root.Add(inspector);
            root.Add(scroller);
            root.Add(toggle);
            root.Query<VisualElement>()
                .ForEach(element => element.pickingMode = PickingMode.Ignore);
            window.ShowUtility();
            yield return null;
            StyleSheet[] initialSheets = new StyleSheet[root.styleSheets.count];
            for (int index = 0; index < initialSheets.Length; index++)
            {
                initialSheets[index] = root.styleSheets[index];
            }
            VisualElement input = field.Q(className: "unity-base-text-field__input");
            VisualElement tracker = scroller.Q(className: "unity-base-slider__tracker");
            VisualElement dragger = scroller.Q(className: "unity-base-slider__dragger");
            VisualElement toggleContainer = toggle.Q(
                className: HorizontalToggle.containerUssClassName
            );
            Assert.IsNotNull(input);
            Assert.IsNotNull(tracker);
            Assert.IsNotNull(dragger);
            Assert.IsNotNull(toggleContainer);
            DataVisualizerThemeSelection selection = new();
            cleanup.Defer(() => selection.Apply(null, null));
            List<string> failures = new();
            string[] names = { "Nord", "Dracula", null };
            string[] backgrounds = { "2E3440FF", "282A36FF", "383838FF" };
            string[] texts = { "ECEFF4FF", "F8F8F2FF", "FFFFFFFF" };
            string[] controls = { "3B4252FF", "44475AFF", "00000000" };
            string[] inputs = { "3B4252FF", "343746FF", "212121FF" };
            string[] borders = { "4C566AFF", "6272A4FF", "80808080" };
            string[] surfaces = { "3B4252FF", "343746FF", "3C3C3CFF" };
            string[] positives = { "A3BE8CFF", "50FA7BFF", "66B366FF" };
            string[] onPositives = { "2E3440FF", "282A36FF", "152315FF" };
            string[] muted = { "D8DEE9FF", "BFC1D6FF", "808080FF" };
            StyleSheet previousThemeSheet = null;
            for (int index = 0; index < names.Length; index++)
            {
                DataVisualizerThemeSettings theme =
                    names[index] == null
                        ? null
                        : AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(
                            folder + names[index] + ".asset"
                        );
                if (names[index] != null)
                {
                    Assert.IsNotNull(theme, names[index]);
                    Assert.IsNotNull(theme.StyleSheet, names[index]);
                }
                selection.Apply(root, theme);
                yield return new WaitForSecondsRealtime(0.5f);
                Assert.IsNotNull(root.panel);
                Assert.IsTrue(root.styleSheets.Contains(baseSheet));
                Assert.AreEqual(
                    initialSheets.Length + (theme == null ? 0 : 1),
                    root.styleSheets.count
                );
                for (int sheetIndex = 0; sheetIndex < initialSheets.Length; sheetIndex++)
                {
                    Assert.AreSame(initialSheets[sheetIndex], root.styleSheets[sheetIndex]);
                }
                if (previousThemeSheet != null)
                {
                    Assert.IsFalse(root.styleSheets.Contains(previousThemeSheet));
                }
                if (theme != null)
                {
                    Assert.AreSame(theme.StyleSheet, root.styleSheets[initialSheets.Length]);
                }
                previousThemeSheet = theme == null ? null : theme.StyleSheet;
                string name = names[index] ?? "Default";
                RecordColor(
                    failures,
                    name,
                    "root background",
                    backgrounds[index],
                    root.resolvedStyle.backgroundColor
                );
                RecordColor(
                    failures,
                    name,
                    "button background",
                    controls[index],
                    button.resolvedStyle.backgroundColor
                );
                RecordColor(
                    failures,
                    name,
                    "button text",
                    texts[index],
                    button.resolvedStyle.color
                );
                RecordColor(
                    failures,
                    name,
                    "button border",
                    borders[index],
                    button.resolvedStyle.borderTopColor
                );
                RecordColor(
                    failures,
                    name,
                    "input background",
                    inputs[index],
                    input.resolvedStyle.backgroundColor
                );
                RecordColor(failures, name, "input text", texts[index], input.resolvedStyle.color);
                RecordColor(
                    failures,
                    name,
                    "input border",
                    borders[index],
                    input.resolvedStyle.borderTopColor
                );
                RecordColor(
                    failures,
                    name,
                    "field label",
                    texts[index],
                    field.labelElement.resolvedStyle.color
                );
                RecordColor(
                    failures,
                    name,
                    "inspector background",
                    surfaces[index],
                    inspector.resolvedStyle.backgroundColor
                );
                RecordColor(
                    failures,
                    name,
                    "inspector text",
                    texts[index],
                    inspector.resolvedStyle.color
                );
                RecordColor(
                    failures,
                    name,
                    "inspector border",
                    borders[index],
                    inspector.resolvedStyle.borderTopColor
                );
                RecordColor(
                    failures,
                    name,
                    "scroller background",
                    controls[index],
                    scroller.resolvedStyle.backgroundColor
                );
                RecordColor(
                    failures,
                    name,
                    "scroller tracker",
                    inputs[index],
                    tracker.resolvedStyle.backgroundColor
                );
                RecordColor(
                    failures,
                    name,
                    "scroller dragger",
                    borders[index],
                    dragger.resolvedStyle.backgroundColor
                );
                RecordColor(
                    failures,
                    name,
                    "toggle background",
                    inputs[index],
                    toggleContainer.resolvedStyle.backgroundColor
                );
                RecordColor(
                    failures,
                    name,
                    "toggle indicator",
                    positives[index],
                    toggle.Indicator.resolvedStyle.backgroundColor
                );
                RecordColor(
                    failures,
                    name,
                    "toggle selected text",
                    onPositives[index],
                    toggle.LeftLabel.resolvedStyle.color
                );
                RecordColor(
                    failures,
                    name,
                    "toggle unselected text",
                    muted[index],
                    toggle.RightLabel.resolvedStyle.color
                );
                toggle.SelectRight(animate: false, notify: false);
                yield return new WaitForSecondsRealtime(0.5f);
                Assert.IsFalse(toggle.IsLeftSelected);
                RecordColor(
                    failures,
                    name,
                    "toggle right selected text",
                    onPositives[index],
                    toggle.RightLabel.resolvedStyle.color
                );
                RecordColor(
                    failures,
                    name,
                    "toggle left unselected text",
                    muted[index],
                    toggle.LeftLabel.resolvedStyle.color
                );
                toggle.SelectLeft(animate: false, notify: false);
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [UnityTest]
        public IEnumerator ShouldRenderShippedThemesAndRestoreClassicOnReset()
        {
            string folder = GetThemeFolder();
            using TestCleanupScope cleanup = new();
            EditorWindow window = ScriptableObject.CreateInstance<EditorWindow>();
            cleanup.Defer(window.Close);
            window.Show();
            VisualElement root = window.rootVisualElement;
            root.AddToClassList("dataviz-root");
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
            Button select = new() { text = "Select" };
            select.AddToClassList("settings-data-folder-button");
            root.Add(select);
            Button themeField = new() { text = "Dracula" };
            themeField.AddToClassList(StyleConstants.ThemeFieldClass);
            root.Add(themeField);
            VisualElement namespaceRow = new();
            namespaceRow.AddToClassList(StyleConstants.NamespaceItemClass);
            root.Add(namespaceRow);
            string[] surfaces = { "3C3C3CFF", "3B4252FF", "343746FF" };
            string[] names = { "Classic", "Nord", "Dracula" };
            string[] accents = { "#d2691e", "#88c0d0", "#bd93f9" };
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
                Assert.AreEqual(select.resolvedStyle.fontSize, reset.resolvedStyle.fontSize);
                Assert.AreEqual(select.resolvedStyle.color, themeField.resolvedStyle.color);
                Assert.AreEqual(select.resolvedStyle.fontSize, themeField.resolvedStyle.fontSize);
                Assert.AreEqual(
                    select.resolvedStyle.borderTopWidth,
                    themeField.resolvedStyle.borderTopWidth
                );
                Assert.AreEqual(select.resolvedStyle.paddingTop, reset.resolvedStyle.paddingTop);
                Assert.AreEqual(
                    select.resolvedStyle.paddingBottom,
                    reset.resolvedStyle.paddingBottom
                );
                Assert.AreEqual(select.resolvedStyle.paddingLeft, reset.resolvedStyle.paddingLeft);
                Assert.AreEqual(
                    select.resolvedStyle.paddingRight,
                    reset.resolvedStyle.paddingRight
                );
                Assert.AreEqual(select.resolvedStyle.height, reset.resolvedStyle.height);
                Assert.AreEqual(select.resolvedStyle.alignSelf, reset.resolvedStyle.alignSelf);
                Assert.AreEqual(
                    select.resolvedStyle.borderTopWidth,
                    reset.resolvedStyle.borderTopWidth
                );
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
