namespace WallstopStudios.DataVisualizer.Editor.Services.ObjectCommands
{
    using Events;
    using UnityEngine;

    internal sealed class CloneObjectCommand : ObjectCommandBase<ObjectCloneRequestedEvent>
    {
        public CloneObjectCommand(DataVisualizer dataVisualizer)
            : base(dataVisualizer) { }

        protected override void Execute(ObjectCloneRequestedEvent evt)
        {
            ScriptableObject dataObject = evt?.DataObject;
            if (dataObject == null)
            {
                return;
            }

            DataVisualizer.CloneObject(dataObject);
        }
    }
}
