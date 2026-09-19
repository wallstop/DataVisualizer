namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using System.Collections.Generic;
    using Extensions;
    using Styles;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class SearchableThemeDropdown : VisualElement
    {
        public IReadOnlyList<ThemeDropdownItem> Items { get; }
        public IReadOnlyList<ThemeDropdownItem> FilteredItems { get; }
        public TextField SearchField { get; }
        public ScrollView Results { get; }
        public int HighlightedIndex => _highlightedIndex;
        public string Filter => SearchField.value;

        private readonly List<ThemeDropdownItem> _filteredItems = new();
        private readonly List<Button> _rows = new();
        private readonly Action<DataVisualizerThemeSettings> _onSelect;
        private readonly Action _onCancel;
        private int _highlightedIndex = -1;

        public SearchableThemeDropdown(
            IReadOnlyList<ThemeDropdownItem> items,
            DataVisualizerThemeSettings selectedTheme,
            Action<DataVisualizerThemeSettings> onSelect,
            Action onCancel = null
        )
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            List<ThemeDropdownItem> snapshot = new(items.Count);
            for (int i = 0; i < items.Count; ++i)
            {
                if (items[i] != null)
                {
                    snapshot.Add(items[i]);
                }
            }

            Items = snapshot.AsReadOnly();
            FilteredItems = _filteredItems.AsReadOnly();
            _onSelect = onSelect;
            _onCancel = onCancel;
            name = "searchable-theme-dropdown";
            style.flexGrow = 1;
            style.minHeight = 0;
            Label title = new("Select Theme") { name = "theme-dropdown-title" };
            title.AddToClassList("theme-dropdown-title");
            Add(title);
            SearchField = new TextField("Search") { name = "theme-search-field" };
            Add(SearchField);
            Results = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "theme-search-results",
                style = { flexGrow = 1, minHeight = 0 },
            };
            Add(Results);
            SearchField.RegisterValueChangedCallback(evt => SetFilter(evt.newValue));
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            SetFilter(string.Empty);
            int selectedIndex = _filteredItems.FindIndex(item => item.Theme == selectedTheme);
            if (0 <= selectedIndex)
            {
                Highlight(selectedIndex);
            }
        }

        public static IReadOnlyList<ThemeDropdownItem> DiscoverItems()
        {
            List<DataVisualizerThemeSettings> themes = new();
            HashSet<string> paths = new(StringComparer.Ordinal);
            Dictionary<string, int> nameCounts = new(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets(
                "t:DataVisualizerThemeSettings",
                new[] { "Assets", "Packages" }
            );
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!paths.Add(path))
                {
                    continue;
                }

                DataVisualizerThemeSettings theme =
                    AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(path);
                if (theme == null)
                {
                    continue;
                }

                themes.Add(theme);
                nameCounts.TryGetValue(theme.name, out int count);
                nameCounts[theme.name] = count + 1;
            }

            themes.Sort(
                (left, right) =>
                {
                    int comparison = StringComparer.OrdinalIgnoreCase.Compare(
                        left.name,
                        right.name
                    );
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    comparison = StringComparer.Ordinal.Compare(left.name, right.name);
                    return comparison != 0
                        ? comparison
                        : StringComparer.Ordinal.Compare(
                            AssetDatabase.GetAssetPath(left),
                            AssetDatabase.GetAssetPath(right)
                        );
                }
            );
            List<ThemeDropdownItem> items = new(themes.Count + 1)
            {
                new(null, string.Empty, "Classic (Default / Reset)"),
            };
            foreach (DataVisualizerThemeSettings theme in themes)
            {
                string path = AssetDatabase.GetAssetPath(theme);
                string displayName =
                    1 < nameCounts[theme.name]
                    || string.Equals(
                        theme.name,
                        items[0].DisplayName,
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? $"{theme.name} ({path})"
                        : theme.name;
                items.Add(new ThemeDropdownItem(theme, path, displayName));
            }

            return items.AsReadOnly();
        }

        public void SetFilter(string filter)
        {
            filter ??= string.Empty;
            SearchField.SetValueWithoutNotify(filter);
            string query = filter.Trim();
            _filteredItems.Clear();
            _rows.Clear();
            Results.Clear();
            for (int i = 0; i < Items.Count; ++i)
            {
                ThemeDropdownItem item = Items[i];
                if (
                    item.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                    && item.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                )
                {
                    continue;
                }

                int index = _filteredItems.Count;
                _filteredItems.Add(item);
                Button row = new(() => SelectFilteredIndex(index))
                {
                    name = "theme-search-result",
                    text = item.DisplayName,
                    tooltip = item.IsDefault ? "Restore the Classic appearance." : item.Path,
                    style =
                    {
                        flexShrink = 0,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        whiteSpace = WhiteSpace.Normal,
                        minHeight = 26,
                    },
                };
                row.AddToClassList(StyleConstants.NamespaceItemClass);
                row.AddToClassList(StyleConstants.ClickableClass);
                row.AddToClassList("theme-search-result");
                _rows.Add(row);
                Results.Add(row);
            }

            if (_filteredItems.Count == 0)
            {
                Label emptyLabel = new("No matching themes") { name = "theme-search-empty" };
                emptyLabel.AddToClassList("theme-search-empty");
                Results.Add(emptyLabel);
            }

            Highlight(_filteredItems.Count == 0 ? -1 : 0);
        }

        public bool SelectFilteredIndex(int index)
        {
            if (index < 0 || _filteredItems.Count <= index)
            {
                return false;
            }

            _onSelect?.Invoke(_filteredItems[index].Theme);
            return true;
        }

        public bool SelectHighlighted()
        {
            return SelectFilteredIndex(_highlightedIndex);
        }

        public void MoveHighlight(int offset)
        {
            if (_filteredItems.Count == 0)
            {
                return;
            }

            Highlight(
                (int)
                    Math.Max(
                        0L,
                        Math.Min(_filteredItems.Count - 1L, (long)_highlightedIndex + offset)
                    )
            );
        }

        public void Cancel()
        {
            _onCancel?.Invoke();
        }

        private void Highlight(int index)
        {
            _highlightedIndex = index;
            for (int i = 0; i < _rows.Count; ++i)
            {
                _rows[i].EnableInClassList(StyleConstants.SelectedClass, i == index);
            }

            if (0 <= index && index < _rows.Count && Results.panel != null)
            {
                Results.ScrollTo(_rows[index]);
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    MoveHighlight(1);
                    break;
                case KeyCode.UpArrow:
                    MoveHighlight(-1);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    SelectHighlighted();
                    break;
                case KeyCode.Escape:
                    Cancel();
                    break;
                default:
                    return;
            }

            evt.StopImmediatePropagation();
            evt.PreventDefaultCompat();
        }
    }
}
