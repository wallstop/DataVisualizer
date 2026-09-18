namespace WallstopStudios.DataVisualizer.Editor.Styles
{
    using UnityEngine;
    using UnityEngine.UIElements;

    [CreateAssetMenu(
        fileName = "DataVisualizerThemeSettings",
        menuName = "Wallstop Studios/DataVisualizer/Data Visualizer Theme",
        order = 2
    )]
    public sealed class DataVisualizerThemeSettings : ScriptableObject
    {
        public StyleSheet StyleSheet => styleSheet;

        [SerializeField]
        private StyleSheet styleSheet;
    }
}
