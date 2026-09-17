namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using Styles;

    public sealed class ThemeDropdownItem
    {
        public DataVisualizerThemeSettings Theme { get; }
        public string Path { get; }
        public string DisplayName { get; }
        public bool IsDefault => Theme == null;

        public ThemeDropdownItem(DataVisualizerThemeSettings theme, string path, string displayName)
        {
            Theme = theme;
            Path = path ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }
    }
}
