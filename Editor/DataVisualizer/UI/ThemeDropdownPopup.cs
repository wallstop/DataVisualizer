namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using Styles;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class ThemeDropdownPopup : PopupWindowContent
    {
        public SearchableThemeDropdown Dropdown { get; private set; }

        private readonly DataVisualizerThemeSettings _selectedTheme;
        private readonly Action<DataVisualizerThemeSettings> _onSelect;
        private readonly Action<VisualElement> _loadBaseStyleSheet;
        private readonly DataVisualizerThemeSelection _themeSelection = new();

        public ThemeDropdownPopup(
            DataVisualizerThemeSettings selectedTheme,
            Action<DataVisualizerThemeSettings> onSelect,
            Action<VisualElement> loadBaseStyleSheet
        )
        {
            _selectedTheme = selectedTheme;
            _onSelect = onSelect;
            _loadBaseStyleSheet = loadBaseStyleSheet;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(460, 340);
        }

        public override void OnOpen()
        {
            VisualElement root = editorWindow.rootVisualElement;
            root.AddToClassList("dataviz-root");
            _loadBaseStyleSheet?.Invoke(root);
            _themeSelection.Apply(root, _selectedTheme);
            Dropdown = new SearchableThemeDropdown(
                SearchableThemeDropdown.DiscoverItems(),
                _selectedTheme,
                SelectAndClose,
                Close
            );
            root.Add(Dropdown);
            Dropdown.schedule.Execute(() => Dropdown.SearchField.Focus());
        }

        public override void OnGUI(Rect rect) { }

        private void SelectAndClose(DataVisualizerThemeSettings theme)
        {
            _onSelect?.Invoke(theme);
            Close();
        }

        private void Close()
        {
            if (editorWindow != null)
            {
                editorWindow.Close();
            }
        }
    }
}
