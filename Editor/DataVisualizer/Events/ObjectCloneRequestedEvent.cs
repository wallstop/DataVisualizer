namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using UnityEngine;

    internal sealed class ObjectCloneRequestedEvent
    {
        public ObjectCloneRequestedEvent(ScriptableObject dataObject)
        {
            DataObject = dataObject;
        }

        public ScriptableObject DataObject { get; }
    }
}
