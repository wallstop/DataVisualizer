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
#if UNITY_EDITOR
                DataVisualizer.LogReorderDebug("ReorderObjectCommand aborted: event was null");
#endif
                return;
            }

            ScriptableObject draggedObject = evt.DraggedObject;
            Type targetType = evt.TargetType;
            if (draggedObject == null || targetType == null)
            {
#if UNITY_EDITOR
                DataVisualizer.LogReorderDebug(
                    "ReorderObjectCommand aborted: dragged object or target type null"
                );
#endif
                return;
            }

            DataVisualizer dataVisualizer = DataVisualizer;
            ObjectListState listState = dataVisualizer.ObjectListState;
            if (listState == null)
            {
#if UNITY_EDITOR
                DataVisualizer.LogReorderDebug(
                    "ReorderObjectCommand aborted: object list state null"
                );
#endif
                return;
            }

#if UNITY_EDITOR
            DataVisualizer.LogReorderDebug(
                $"ReorderObjectCommand executing for type '{targetType.FullName}'"
            );
#endif
            int filteredCount = listState.FilteredObjectsBuffer.Count;
            if (filteredCount == 0)
            {
#if UNITY_EDITOR
                DataVisualizer.LogReorderDebug(
                    "ReorderObjectCommand aborted: filtered object list empty"
                );
#endif
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
#if UNITY_EDITOR
                DataVisualizer.LogReorderDebug(
                    "ReorderObjectCommand aborted: dragged object not found in filtered list"
                );
#endif
                return;
            }

#if UNITY_EDITOR
            DataVisualizer.LogReorderDebug(
                $"Object drop request for type '{targetType.FullName}' currentIndex={sourceIndex} targetIndex={targetIndex}"
            );
            dataVisualizer.LogObjectOrder(
                "Filtered objects before drop",
                listState.FilteredObjectsBuffer
            );
#endif
            List<ScriptableObject> filteredWithoutDragged = new List<ScriptableObject>(
                listState.FilteredObjectsBuffer
            );
            filteredWithoutDragged.Remove(draggedObject);

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

            targetIndex = Mathf.Clamp(targetIndex, 0, filteredWithoutDragged.Count);

            dataVisualizer.UpdateAndSaveObjectOrderList(
                targetType,
                listState.FilteredObjectsBuffer
            );

            if (dataVisualizer._suppressObjectListReloadForTests)
            {
                listState.ClearDisplayed();
                listState.DisplayedObjectsBuffer.AddRange(listState.FilteredObjectsBuffer);
            }

            dataVisualizer.LoadObjectTypes(targetType);
            dataVisualizer.ApplyLabelFilter(buildObjectsView: false);
            dataVisualizer.BuildObjectsView();
#if UNITY_EDITOR
            dataVisualizer.LogObjectOrder(
                "Selected objects after drop",
                dataVisualizer._selectedObjects
            );
            dataVisualizer.LogObjectOrder(
                "Filtered objects after drop",
                listState.FilteredObjectsBuffer
            );
#endif

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
