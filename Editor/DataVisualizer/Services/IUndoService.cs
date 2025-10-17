namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using Data;
    using UnityEngine;

    internal interface IUndoService
    {
        void RegisterCreatedObject(Object target, string actionName);

        void RecordObject(Object target, string actionName);

        void RecordSettings(DataVisualizerSettings settings, string actionName);
    }
}
