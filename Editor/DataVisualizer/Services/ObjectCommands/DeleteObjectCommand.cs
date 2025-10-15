namespace WallstopStudios.DataVisualizer.Editor.Services.ObjectCommands
{
    using Events;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class DeleteObjectCommand
        : ObjectCommandBase<ObjectDeleteRequestedEvent>
    {
        public DeleteObjectCommand(DataVisualizer dataVisualizer)
            : base(dataVisualizer) { }

        protected override void Execute(ObjectDeleteRequestedEvent evt)
        {
            Button trigger = evt?.Trigger;
            ScriptableObject dataObject = evt?.DataObject;
            if (trigger == null || dataObject == null)
            {
                return;
            }

            DataVisualizer.OpenConfirmDeletePopover(trigger, dataObject);
        }
    }
}
