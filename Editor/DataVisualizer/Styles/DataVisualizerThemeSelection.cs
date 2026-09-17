namespace WallstopStudios.DataVisualizer.Editor.Styles
{
    using UnityEditor;
    using UnityEngine.UIElements;

    public sealed class DataVisualizerThemeSelection
    {
        private StyleSheet _appliedSheet;
        private VisualElement _root;

        public static DataVisualizerThemeSettings Resolve(string guid)
        {
            return string.IsNullOrEmpty(guid)
                ? null
                : AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(
                    AssetDatabase.GUIDToAssetPath(guid)
                );
        }

        public void Apply(VisualElement root, DataVisualizerThemeSettings theme)
        {
            if (_root != null && _appliedSheet != null)
            {
                _root.styleSheets.Remove(_appliedSheet);
            }

            _root = root;
            _appliedSheet = null;
            StyleSheet sheet = theme != null ? theme.StyleSheet : null;
            if (root != null && sheet != null && !root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
                _appliedSheet = sheet;
            }
        }
    }
}
