namespace WallstopStudios.DataVisualizer.Editor.Services.ObjectCommands
{
    using Events;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class MoveObjectCommand
        : ObjectCommandBase<ObjectMoveRequestedEvent>
    {
        public MoveObjectCommand(DataVisualizer dataVisualizer)
            : base(dataVisualizer) { }

        protected override void Execute(ObjectMoveRequestedEvent evt)
        {
            Button trigger = evt?.Trigger;
            ScriptableObject dataObject = evt?.DataObject;
            if (trigger == null || dataObject == null)
            {
                return;
            }

            DataVisualizer.ExecuteMoveObject(trigger, dataObject);
        }
    }
}
