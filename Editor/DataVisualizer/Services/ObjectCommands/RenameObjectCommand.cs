namespace WallstopStudios.DataVisualizer.Editor.Services.ObjectCommands
{
    using Events;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class RenameObjectCommand
        : ObjectCommandBase<ObjectRenameRequestedEvent>
    {
        public RenameObjectCommand(DataVisualizer dataVisualizer)
            : base(dataVisualizer) { }

        protected override void Execute(ObjectRenameRequestedEvent evt)
        {
            Button trigger = evt?.Trigger;
            ScriptableObject dataObject = evt?.DataObject;
            if (trigger == null || dataObject == null)
            {
                return;
            }

            object property = trigger.GetProperty(DataVisualizer.RowComponentsProperty);
            if (property is not DataVisualizer.ObjectRowComponents components)
            {
                return;
            }

            DataVisualizer.OpenRenamePopover(components.TitleLabel, trigger, dataObject);
        }
    }
}
