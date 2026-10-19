namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using UnityEditor;
    using UnityEngine.UIElements;

    // Instantiated explicitly so the fixture does not replace a project's custom editors.
    public sealed class ResponsiveTestInspectorEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            return new TextField("A custom inspector field") { name = "custom-inspector-field", value = "The value must survive pane changes" };
        }
    }
}
