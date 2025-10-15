namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using UnityEngine;

    internal sealed class ObjectMoveToTopRequestedEvent
    {
        public ObjectMoveToTopRequestedEvent(ScriptableObject dataObject)
        {
            DataObject = dataObject;
        }

        public ScriptableObject DataObject { get; }
    }
}
