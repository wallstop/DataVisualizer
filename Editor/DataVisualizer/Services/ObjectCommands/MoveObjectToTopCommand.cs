namespace WallstopStudios.DataVisualizer.Editor.Services.ObjectCommands
{
    using Events;
    using UnityEngine;

    internal sealed class MoveObjectToTopCommand
        : ObjectCommandBase<ObjectMoveToTopRequestedEvent>
    {
        public MoveObjectToTopCommand(DataVisualizer dataVisualizer)
            : base(dataVisualizer) { }

        protected override void Execute(ObjectMoveToTopRequestedEvent evt)
        {
            ScriptableObject dataObject = evt?.DataObject;
            if (dataObject == null)
            {
                return;
            }

            DataVisualizer._filteredObjects.Remove(dataObject);
            DataVisualizer._filteredObjects.Insert(0, dataObject);
            DataVisualizer._selectedObjects.Remove(dataObject);
            DataVisualizer._selectedObjects.Insert(0, dataObject);
            DataVisualizer.UpdateAndSaveObjectOrderList(
                dataObject.GetType(),
                DataVisualizer._selectedObjects
            );
            DataVisualizer.BuildObjectsView();
        }
    }
}
