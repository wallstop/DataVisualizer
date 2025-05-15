namespace WallstopStudios.DataVisualizer
{
    using UnityEngine.UIElements;

    public interface IGUIProvider
    {
        public VisualElement BuildGUI(DataVisualizerGUIContext context);
    }
}
