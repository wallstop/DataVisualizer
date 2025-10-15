namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class ObjectRenameRequestedEvent
    {
        public ObjectRenameRequestedEvent(Button trigger, ScriptableObject dataObject)
        {
            Trigger = trigger;
            DataObject = dataObject;
        }

        public Button Trigger { get; }

        public ScriptableObject DataObject { get; }
    }
}
