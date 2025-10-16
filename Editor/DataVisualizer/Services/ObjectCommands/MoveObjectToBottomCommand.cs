namespace WallstopStudios.DataVisualizer.Editor.Services.ObjectCommands
{
    using System.Collections.Generic;
    using Events;
    using State;
    using UnityEngine;

    internal sealed class MoveObjectToBottomCommand
        : ObjectCommandBase<ObjectMoveToBottomRequestedEvent>
    {
        public MoveObjectToBottomCommand(DataVisualizer dataVisualizer)
            : base(dataVisualizer) { }

        protected override void Execute(ObjectMoveToBottomRequestedEvent evt)
        {
            ScriptableObject dataObject = evt?.DataObject;
            if (dataObject == null)
            {
                return;
            }

            DataVisualizer._selectedObjects.Remove(dataObject);
            DataVisualizer._selectedObjects.Add(dataObject);
            ObjectListState listState = DataVisualizer.ObjectListState;
            List<ScriptableObject> filteredObjects = listState.FilteredObjectsBuffer;
            filteredObjects.Remove(dataObject);
            filteredObjects.Add(dataObject);
            DataVisualizer.UpdateAndSaveObjectOrderList(
                dataObject.GetType(),
                DataVisualizer._selectedObjects
            );
            DataVisualizer.BuildObjectsView();
        }
    }
}
