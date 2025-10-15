namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using UnityEngine;

    internal sealed class ObjectMoveToBottomRequestedEvent
    {
        public ObjectMoveToBottomRequestedEvent(ScriptableObject dataObject)
        {
            DataObject = dataObject;
        }

        public ScriptableObject DataObject { get; }
    }
}
