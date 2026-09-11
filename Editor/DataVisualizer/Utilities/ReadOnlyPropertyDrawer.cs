namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer;

    // https://www.patrykgalach.com/2020/01/20/readonly-attribute-in-unity-editor/
    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    internal sealed class DxReadOnlyPropertyDrawer : PropertyDrawer
    {
        private static readonly ReusableDisposalScope<bool> GuiEnabledScopes = new(enabled =>
            GUI.enabled = enabled
        );

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using ReusableDisposalLease<bool> cleanup = GuiEnabledScopes.Acquire(GUI.enabled);
            GUI.enabled = false;
            _ = EditorGUI.PropertyField(position, property, label);
        }
    }
#endif
}
