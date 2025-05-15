namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;

    // https://www.patrykgalach.com/2020/01/20/readonly-attribute-in-unity-editor/
    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    internal sealed class DxReadOnlyPropertyDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            bool previousGUIState = GUI.enabled;
            GUI.enabled = false;
            _ = EditorGUI.PropertyField(position, property, label);
            GUI.enabled = previousGUIState;
        }
    }
#endif
}
