namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using Data;
    using UnityEditor;
    using UnityEngine;

    internal sealed class UndoService : IUndoService
    {
        public void RegisterCreatedObject(Object target, string actionName)
        {
            if (target == null)
            {
                return;
            }

            Undo.RegisterCreatedObjectUndo(target, actionName ?? "Create Object");
        }

        public void RecordObject(Object target, string actionName)
        {
            if (target == null)
            {
                return;
            }

            Undo.RecordObject(target, actionName ?? "Modify Object");
            EditorUtility.SetDirty(target);
        }

        public void RecordSettings(DataVisualizerSettings settings, string actionName)
        {
            if (settings == null)
            {
                return;
            }

            RecordObject(settings, actionName ?? "Modify Settings");
        }
    }
}
