#if UNITY_EDITOR
namespace WallstopStudios.DataVisualizer.Editor
{
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class ExampleWindow : EditorWindow
    {
        private const string WindowTitle = "Example Window";
        private const string UssClassName = "example-window";
        private const string HeaderClassName = "namespace-group-header";
        private const string PrefsPrefix = "WallstopStudios.Editor.ExampleWindow.";
        private const string StylesPath =
            "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/DataVisualizerStyles.uss";

        [MenuItem("Tools/Wallstop Studios/" + WindowTitle)]
        private static void ShowWindow()
        {
            ExampleWindow window = GetWindow<ExampleWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(480f, 320f);
        }

        public void OnEnable()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            root.AddToClassList(UssClassName);

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylesPath);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            Label header = new Label(WindowTitle);
            header.AddToClassList(HeaderClassName);
            root.Add(header);
        }
    }
}
#endif
