namespace WallstopStudios.DataVisualizer.Editor.Services.ObjectCommands
{
    using System;
    using System.Collections.Generic;
    using Events;
    using State;
    using UnityEngine;
    using Utilities;

    internal sealed class ReorderObjectCommand : ObjectCommandBase<ObjectReorderRequestedEvent>
    {
        public ReorderObjectCommand(DataVisualizer dataVisualizer)
            : base(dataVisualizer) { }

        protected override void Execute(ObjectReorderRequestedEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            ScriptableObject draggedObject = evt.DraggedObject;
            Type targetType = evt.TargetType;
            if (draggedObject == null || targetType == null)
            {
                return;
            }

            DataVisualizer dataVisualizer = DataVisualizer;
            ObjectListState listState = dataVisualizer.ObjectListState;
            if (listState == null)
            {
                return;
            }

            int filteredCount = listState.FilteredObjectsBuffer.Count;
            if (filteredCount == 0)
            {
                return;
            }

            int targetIndex = evt.TargetIndex;
            if (targetIndex < 0)
            {
                targetIndex = 0;
            }

            ScriptableObject previousSelection = dataVisualizer._selectedObject;
            int sourceIndex = listState.FilteredObjectsBuffer.IndexOf(draggedObject);
            if (sourceIndex < 0)
            {
                return;
            }

            List<ScriptableObject> filteredWithoutDragged = new List<ScriptableObject>(
                listState.FilteredObjectsBuffer
            );
            filteredWithoutDragged.Remove(draggedObject);
            targetIndex = Mathf.Clamp(targetIndex, 0, filteredWithoutDragged.Count);

            if (evt.ShiftPressed)
            {
                targetIndex = 0;
            }
            else if (evt.ControlPressed)
            {
                targetIndex = filteredWithoutDragged.Count;
            }

            ScriptableObject insertBefore =
                targetIndex < filteredWithoutDragged.Count
                    ? filteredWithoutDragged[targetIndex]
                    : null;
            ScriptableObject insertAfter =
                targetIndex > 0 ? filteredWithoutDragged[targetIndex - 1] : null;

            ObjectOrderHelper.ReorderItem(
                listState.FilteredObjectsBuffer,
                draggedObject,
                insertBefore,
                insertAfter
            );
            DataVisualizer.RemoveDuplicateObjects(listState.FilteredObjectsBuffer);

            ObjectOrderHelper.ReorderItem(
                dataVisualizer._selectedObjects,
                draggedObject,
                insertBefore,
                insertAfter
            );
            DataVisualizer.RemoveDuplicateObjects(dataVisualizer._selectedObjects);

            dataVisualizer.UpdateAndSaveObjectOrderList(
                targetType,
                dataVisualizer._selectedObjects
            );

            if (dataVisualizer._suppressObjectListReloadForTests)
            {
                listState.ClearDisplayed();
                listState.DisplayedObjectsBuffer.AddRange(listState.FilteredObjectsBuffer);
                listState.DisplayedMetadataBuffer.Clear();
                listState.DisplayedMetadataBuffer.AddRange(listState.FilteredMetadataBuffer);
            }
            else
            {
                dataVisualizer.LoadObjectTypes(targetType);
                dataVisualizer.ApplyLabelFilter(buildObjectsView: false);
                dataVisualizer.BuildObjectsView();
            }

            string draggedGuid = DataVisualizer.GetAssetGuid(draggedObject);
            ScriptableObject objectToSelect = dataVisualizer.DeterminePostDropSelection(
                previousSelection,
                draggedObject,
                draggedGuid
            );

            dataVisualizer.SelectObject(objectToSelect);
        }
    }
}
