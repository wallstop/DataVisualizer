namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using Events;
    using State;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utilities;

    internal sealed class DragAndDropController
    {
        private readonly DataVisualizer _dataVisualizer;
        private readonly DataVisualizerEventHub _eventHub;

        private float _cachedDragWidth;
        private float _cachedDragHeight;
        private float _cachedMarginLeft;
        private float _cachedMarginRight;
        private float _cachedMarginTop;
        private float _cachedMarginBottom;
        private float _cachedFlexGrow;
        private float _cachedFlexShrink;

        public DragAndDropController(DataVisualizer dataVisualizer, DataVisualizerEventHub eventHub)
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
        }

        public void StartDragVisuals(Vector2 currentPosition, string dragText)
        {
            if (_dataVisualizer._draggedElement == null || _dataVisualizer._draggedData == null)
            {
                return;
            }

            CacheDragMetrics();
            _dataVisualizer.rootVisualElement.AddToClassList("dragging-cursor");

            EnsureDragGhost(dragText);
            PositionDragGhost(currentPosition);
            EnsureInPlaceGhost(dragText);

            _dataVisualizer._lastGhostInsertIndex = -1;
            _dataVisualizer._lastGhostParent = null;

            _dataVisualizer._draggedElement.style.visibility = Visibility.Hidden;
            _dataVisualizer._draggedElement.style.display = DisplayStyle.None;
        }

        public void UpdateInPlaceGhostPosition(Vector2 pointerPosition)
        {
            VisualElement container = null;
            switch (_dataVisualizer._activeDragType)
            {
                case DataVisualizer.DragType.Object:
                {
                    container = _dataVisualizer.GetObjectListContentContainer();
                    break;
                }
                case DataVisualizer.DragType.Namespace:
                {
                    container = _dataVisualizer._namespaceListContainer;
                    break;
                }
                case DataVisualizer.DragType.Type:
                {
                    container = _dataVisualizer._draggedElement?.parent;
                    break;
                }
            }

            if (
                container == null
                || _dataVisualizer._draggedElement == null
                || _dataVisualizer._inPlaceGhost == null
            )
            {
                if (_dataVisualizer._inPlaceGhost?.parent != null)
                {
                    _dataVisualizer._inPlaceGhost.RemoveFromHierarchy();
                }

                if (_dataVisualizer._inPlaceGhost != null)
                {
                    _dataVisualizer._inPlaceGhost.style.visibility = Visibility.Hidden;
                }

                _dataVisualizer._lastGhostInsertIndex = -1;
                _dataVisualizer._lastGhostParent = null;
                return;
            }

            ApplyCachedSizing(_dataVisualizer._inPlaceGhost, container);

            int childCount = container.childCount;
            int targetIndex;
            Vector2 localPointerPos = container.WorldToLocal(pointerPosition);

            if (_dataVisualizer._activeDragType != DataVisualizer.DragType.Namespace)
            {
                targetIndex = -1;
                for (int i = 0; i < childCount; ++i)
                {
                    VisualElement child = container.ElementAt(i);
                    float midpoint = child.layout.yMin + child.layout.height / 2f;
                    if (localPointerPos.y < midpoint)
                    {
                        targetIndex = i;
                        break;
                    }
                }

                if (targetIndex < 0)
                {
                    targetIndex = childCount;
                }
            }
            else
            {
                targetIndex = 0;
                if (localPointerPos.y >= 0f)
                {
                    bool seenInPlaceGhost = false;
                    for (int i = 0; i < childCount; ++i)
                    {
                        VisualElement child = container.ElementAt(i);
                        if (child == _dataVisualizer._inPlaceGhost)
                        {
                            seenInPlaceGhost = true;
                        }

                        float yMax = child.layout.yMax;
                        if (localPointerPos.y < yMax)
                        {
                            targetIndex = seenInPlaceGhost ? i : i + 1;
                            break;
                        }
                    }
                }
            }

            targetIndex = Mathf.Clamp(targetIndex, 0, childCount);
            int normalizedIndex = NormalizeGhostInsertIndex(
                container,
                _dataVisualizer._inPlaceGhost,
                targetIndex
            );

            bool targetIndexValid = normalizedIndex >= 0;
            if (
                targetIndexValid
                && (
                    normalizedIndex != _dataVisualizer._lastGhostInsertIndex
                    || container != _dataVisualizer._lastGhostParent
                )
            )
            {
                if (_dataVisualizer._inPlaceGhost.parent != null)
                {
                    _dataVisualizer._inPlaceGhost.RemoveFromHierarchy();
                }

                int insertionIndex = Mathf.Clamp(normalizedIndex, 0, container.childCount);
                container.Insert(insertionIndex, _dataVisualizer._inPlaceGhost);
                normalizedIndex = insertionIndex;
                _dataVisualizer._lastGhostInsertIndex = normalizedIndex;
                _dataVisualizer._lastGhostParent = container;
            }

            if (targetIndexValid)
            {
                _dataVisualizer._inPlaceGhost.style.visibility = Visibility.Visible;
                _dataVisualizer._inPlaceGhost.userData = normalizedIndex;
            }
            else
            {
                if (_dataVisualizer._inPlaceGhost.parent != null)
                {
                    _dataVisualizer._inPlaceGhost.RemoveFromHierarchy();
                }

                _dataVisualizer._inPlaceGhost.style.visibility = Visibility.Hidden;
                _dataVisualizer._inPlaceGhost.userData = -1;
                _dataVisualizer._lastGhostInsertIndex = -1;
                _dataVisualizer._lastGhostParent = null;
            }
        }

        public void CancelDrag()
        {
            if (_dataVisualizer._inPlaceGhost != null)
            {
                _dataVisualizer._inPlaceGhost.RemoveFromHierarchy();
                _dataVisualizer._inPlaceGhost = null;
            }

            _dataVisualizer._lastGhostInsertIndex = -1;
            _dataVisualizer._lastGhostParent = null;

            if (_dataVisualizer._draggedElement != null)
            {
                _dataVisualizer._draggedElement.style.opacity = 1f;
                _dataVisualizer._draggedElement.style.visibility = Visibility.Visible;
                _dataVisualizer._draggedElement.style.display = DisplayStyle.Flex;
            }

            if (_dataVisualizer._dragGhost != null)
            {
                _dataVisualizer._dragGhost.style.visibility = Visibility.Hidden;
            }

            _dataVisualizer._isDragging = false;
            _dataVisualizer._draggedElement = null;
            _dataVisualizer._draggedData = null;
            _dataVisualizer._activeDragType = DataVisualizer.DragType.None;
            _dataVisualizer.rootVisualElement.RemoveFromClassList("dragging-cursor");

            _cachedDragWidth = 0f;
            _cachedDragHeight = 0f;
            _cachedMarginLeft = 0f;
            _cachedMarginRight = 0f;
            _cachedMarginTop = 0f;
            _cachedMarginBottom = 0f;
            _cachedFlexGrow = 0f;
            _cachedFlexShrink = 0f;
        }

        public static int NormalizeGhostInsertIndex(
            VisualElement container,
            VisualElement ghost,
            int desiredIndex
        )
        {
            if (container == null)
            {
                return -1;
            }

            int childCount = container.childCount;
            bool ghostInContainer = ghost != null && ghost.parent == container;

            if (ghostInContainer)
            {
                childCount = Math.Max(0, childCount - 1);
            }

            childCount = Math.Max(0, childCount);
            return Mathf.Clamp(desiredIndex, 0, childCount);
        }

        public void HandlePointerUp(PointerUpEvent evt)
        {
            if (
                _dataVisualizer._draggedElement == null
                || !_dataVisualizer._draggedElement.HasPointerCapture(evt.pointerId)
                || _dataVisualizer._activeDragType == DataVisualizer.DragType.None
            )
            {
                return;
            }

            int pointerId = evt.pointerId;
            bool performDrop = _dataVisualizer._isDragging;
            DataVisualizer.DragType dropType = _dataVisualizer._activeDragType;

            VisualElement draggedElement = _dataVisualizer._draggedElement;
            try
            {
                _dataVisualizer._draggedElement.ReleasePointer(pointerId);

                if (!performDrop)
                {
                    return;
                }

                switch (dropType)
                {
                    case DataVisualizer.DragType.Object:
                    {
                        PerformObjectDrop();
                        break;
                    }
                    case DataVisualizer.DragType.Namespace:
                    {
                        PerformNamespaceDrop();
                        break;
                    }
                    case DataVisualizer.DragType.Type:
                    {
                        PerformTypeDrop();
                        break;
                    }
                    default:
                    {
                        throw new InvalidEnumArgumentException(
                            nameof(dropType),
                            (int)dropType,
                            typeof(DataVisualizer.DragType)
                        );
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"Error during drop execution for {dropType}. {exception}");
            }
            finally
            {
                draggedElement.UnregisterCallback<PointerMoveEvent>(
                    _dataVisualizer.OnCapturedPointerMove
                );
                draggedElement.UnregisterCallback<PointerUpEvent>(
                    _dataVisualizer.OnCapturedPointerUp
                );
                draggedElement.UnregisterCallback<PointerCaptureOutEvent>(
                    _dataVisualizer.OnPointerCaptureOut
                );

                _dataVisualizer.CancelDrag();
            }

            evt.StopPropagation();
        }

        public void PerformObjectDrop()
        {
            int targetIndex = _dataVisualizer._inPlaceGhost?.userData is int index ? index : -1;
            _dataVisualizer._inPlaceGhost?.RemoveFromHierarchy();
            if (
                _dataVisualizer._draggedElement == null
                || _dataVisualizer._draggedData is not ScriptableObject draggedObject
            )
            {
                return;
            }

            if (_dataVisualizer.GetObjectListContentContainer() == null || targetIndex < 0)
            {
                return;
            }

            ObjectListState listState = _dataVisualizer.ObjectListState;

            if (listState.DisplayedObjects.Count == 0)
            {
                return;
            }

            Type selectedType = _dataVisualizer._namespaceController.SelectedType;
            if (selectedType == null)
            {
                return;
            }

            ScriptableObject previousSelection = _dataVisualizer._selectedObject;
            int filteredCount = listState.FilteredObjects.Count;
            int displayTargetIndex = Mathf.Clamp(targetIndex, 0, listState.DisplayedObjects.Count);
            int globalTargetIndex = Mathf.Clamp(
                listState.DisplayStartIndex + displayTargetIndex,
                0,
                filteredCount
            );

            List<ScriptableObject> filteredWithoutDragged = new List<ScriptableObject>(
                listState.FilteredObjects
            );
            filteredWithoutDragged.Remove(draggedObject);
            globalTargetIndex = Mathf.Clamp(globalTargetIndex, 0, filteredWithoutDragged.Count);

            ScriptableObject insertBefore =
                globalTargetIndex < filteredWithoutDragged.Count
                    ? filteredWithoutDragged[globalTargetIndex]
                    : null;
            ScriptableObject insertAfter =
                globalTargetIndex > 0 ? filteredWithoutDragged[globalTargetIndex - 1] : null;

            ObjectOrderHelper.ReorderItem(
                listState.FilteredObjectsBuffer,
                draggedObject,
                insertBefore,
                insertAfter
            );
            DataVisualizer.RemoveDuplicateObjects(listState.FilteredObjectsBuffer);

            ObjectOrderHelper.ReorderItem(
                _dataVisualizer._selectedObjects,
                draggedObject,
                insertBefore,
                insertAfter
            );
            DataVisualizer.RemoveDuplicateObjects(_dataVisualizer._selectedObjects);

            _dataVisualizer.UpdateAndSaveObjectOrderList(
                selectedType,
                _dataVisualizer._selectedObjects
            );

            string draggedGuid = DataVisualizer.GetAssetGuid(draggedObject);

            if (_dataVisualizer._suppressObjectListReloadForTests)
            {
                listState.ClearDisplayed();
                listState.DisplayedObjectsBuffer.AddRange(listState.FilteredObjectsBuffer);
                listState.DisplayedMetadataBuffer.Clear();
                listState.DisplayedMetadataBuffer.AddRange(listState.FilteredMetadataBuffer);
            }
            else
            {
                _dataVisualizer.LoadObjectTypes(selectedType);
                _dataVisualizer.ApplyLabelFilter(buildObjectsView: false);
                _dataVisualizer.BuildObjectsView();
            }

            ScriptableObject objectToSelect = _dataVisualizer.DeterminePostDropSelection(
                previousSelection,
                draggedObject,
                draggedGuid
            );

            _dataVisualizer.SelectObject(objectToSelect);
            if (_dataVisualizer._draggedElement != null)
            {
                _dataVisualizer._draggedElement.style.display = DisplayStyle.Flex;
                _dataVisualizer._draggedElement.style.opacity = 1.0f;
            }
        }

        public void PerformNamespaceDrop()
        {
            int targetIndex = _dataVisualizer._inPlaceGhost?.userData is int index ? index : -1;

            _dataVisualizer._inPlaceGhost?.RemoveFromHierarchy();

            if (
                _dataVisualizer._draggedElement == null
                || _dataVisualizer._draggedData is not string draggedKey
                || _dataVisualizer._namespaceListContainer == null
            )
            {
                return;
            }

            if (targetIndex < 0)
            {
                return;
            }

            int currentIndex = _dataVisualizer._namespaceListContainer.IndexOf(
                _dataVisualizer._draggedElement
            );
            if (currentIndex < 0)
            {
                return;
            }

            if (currentIndex < targetIndex)
            {
                targetIndex--;
            }

            _dataVisualizer._draggedElement.style.display = DisplayStyle.Flex;
            _dataVisualizer._draggedElement.style.opacity = 1.0f;
            _dataVisualizer._namespaceListContainer.Insert(
                targetIndex,
                _dataVisualizer._draggedElement
            );
            foreach (VisualElement child in _dataVisualizer._namespaceListContainer.Children())
            {
                NamespaceController.RecalibrateVisualElements(child);
            }

            _eventHub?.Publish(new NamespaceReorderRequestedEvent(draggedKey, targetIndex));
        }

        public void PerformTypeDrop()
        {
            if (
                _dataVisualizer._draggedElement == null
                || _dataVisualizer._draggedData is not Type draggedType
                || _dataVisualizer._draggedElement.parent is not VisualElement typesContainer
                || typesContainer.userData is not string namespaceKey
            )
            {
                return;
            }

            int targetIndex = _dataVisualizer._inPlaceGhost?.userData is int index ? index : -1;
            _dataVisualizer._inPlaceGhost?.RemoveFromHierarchy();

            if (targetIndex < 0)
            {
                return;
            }

            if (
                !_dataVisualizer._scriptableObjectTypes.TryGetValue(
                    namespaceKey,
                    out List<Type> types
                )
                || types == null
            )
            {
                return;
            }

            int currentIndex = types.IndexOf(draggedType);
            if (currentIndex < 0)
            {
                return;
            }

            if (currentIndex < targetIndex)
            {
                targetIndex--;
            }

            types.Remove(draggedType);
            targetIndex = Mathf.Clamp(targetIndex, 0, types.Count);
            types.Insert(targetIndex, draggedType);

            UpdateAndSaveTypeOrder(namespaceKey, types);
            _dataVisualizer._namespacePanelController?.BuildNamespaceView();
            _eventHub?.Publish(
                new TypeReorderRequestedEvent(namespaceKey, draggedType, targetIndex)
            );
        }

        private void CacheDragMetrics()
        {
            float resolvedWidth = _dataVisualizer._draggedElement.resolvedStyle.width;
            float resolvedHeight = _dataVisualizer._draggedElement.resolvedStyle.height;
            if (float.IsNaN(resolvedWidth) || resolvedWidth <= 0f)
            {
                resolvedWidth = _dataVisualizer._draggedElement.layout.width;
            }

            if (float.IsNaN(resolvedHeight) || resolvedHeight <= 0f)
            {
                resolvedHeight = _dataVisualizer._draggedElement.layout.height;
            }

            if (resolvedWidth <= 0f)
            {
                resolvedWidth = _dataVisualizer._draggedElement.contentRect.width;
            }

            if (resolvedHeight <= 0f)
            {
                resolvedHeight = _dataVisualizer._draggedElement.contentRect.height;
            }

            _cachedDragWidth = resolvedWidth > 0f ? resolvedWidth : 1f;
            _cachedDragHeight = resolvedHeight > 0f ? resolvedHeight : 1f;
            _cachedMarginLeft = _dataVisualizer._draggedElement.resolvedStyle.marginLeft;
            _cachedMarginRight = _dataVisualizer._draggedElement.resolvedStyle.marginRight;
            _cachedMarginTop = _dataVisualizer._draggedElement.resolvedStyle.marginTop;
            _cachedMarginBottom = _dataVisualizer._draggedElement.resolvedStyle.marginBottom;
            _cachedFlexGrow = _dataVisualizer._draggedElement.resolvedStyle.flexGrow;
            _cachedFlexShrink = _dataVisualizer._draggedElement.resolvedStyle.flexShrink;
        }

        private void EnsureDragGhost(string dragText)
        {
            if (_dataVisualizer._dragGhost == null)
            {
                _dataVisualizer._dragGhost = new VisualElement
                {
                    name = "drag-ghost-cursor",
                    pickingMode = PickingMode.Ignore,
                    style = { visibility = Visibility.Visible },
                };
                _dataVisualizer.rootVisualElement.Add(_dataVisualizer._dragGhost);
            }

            _dataVisualizer._dragGhost.ClearClassList();
            foreach (string className in _dataVisualizer._draggedElement.GetClasses())
            {
                _dataVisualizer._dragGhost.AddToClassList(className);
            }

            _dataVisualizer._dragGhost.AddToClassList("drag-ghost");

            float width = _cachedDragWidth;
            if (width <= 0f)
            {
                VisualElement parent = _dataVisualizer._draggedElement.parent;
                width =
                    parent?.resolvedStyle.width
                    ?? _dataVisualizer.rootVisualElement.resolvedStyle.width;
            }

            if (width <= 0f)
            {
                width = _dataVisualizer.rootVisualElement.layout.width;
            }

            if (width <= 0f)
            {
                width = 1f;
            }

            float height = _cachedDragHeight;
            if (height <= 0f)
            {
                height = _dataVisualizer._draggedElement.layout.height;
            }

            if (height <= 0f)
            {
                height = _dataVisualizer._draggedElement.contentRect.height;
            }

            if (height <= 0f)
            {
                height = 1f;
            }

            _dataVisualizer._dragGhost.style.width = width;
            _dataVisualizer._dragGhost.style.minWidth = width;
            _dataVisualizer._dragGhost.style.height = height;
            _dataVisualizer._dragGhost.style.minHeight = height;
            _dataVisualizer._dragGhost.style.flexBasis = height;

            Label ghostLabel = _dataVisualizer._dragGhost.Q<Label>() ?? new Label();
            ghostLabel.text = dragText;
            ghostLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            ghostLabel.pickingMode = PickingMode.Ignore;
            if (ghostLabel.parent != _dataVisualizer._dragGhost)
            {
                _dataVisualizer._dragGhost.Clear();
                _dataVisualizer._dragGhost.Add(ghostLabel);
            }
        }

        private void PositionDragGhost(Vector2 currentPosition)
        {
            if (_dataVisualizer._dragGhost == null)
            {
                return;
            }

            float width = _dataVisualizer._dragGhost.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f)
            {
                width = _cachedDragWidth > 0f ? _cachedDragWidth : 1f;
            }

            _dataVisualizer._dragGhost.style.visibility = Visibility.Visible;
            _dataVisualizer._dragGhost.style.left = currentPosition.x - width / 2f;
            _dataVisualizer._dragGhost.style.top =
                currentPosition.y - _dataVisualizer._dragGhost.resolvedStyle.height;
            _dataVisualizer._dragGhost.BringToFront();
        }

        private void EnsureInPlaceGhost(string dragText)
        {
            if (_dataVisualizer._inPlaceGhost == null)
            {
                _dataVisualizer._inPlaceGhost = new VisualElement
                {
                    name = "drag-ghost-overlay",
                    pickingMode = PickingMode.Ignore,
                };
                foreach (string className in _dataVisualizer._draggedElement.GetClasses())
                {
                    _dataVisualizer._inPlaceGhost.AddToClassList(className);
                }

                _dataVisualizer._inPlaceGhost.AddToClassList("in-place-ghost");

                Label ghostLabel =
                    _dataVisualizer._inPlaceGhost.Q<Label>()
                    ?? new Label { pickingMode = PickingMode.Ignore };
                ghostLabel.text = dragText;
                ghostLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                if (ghostLabel.parent != _dataVisualizer._inPlaceGhost)
                {
                    _dataVisualizer._inPlaceGhost.Clear();
                    _dataVisualizer._inPlaceGhost.Add(ghostLabel);
                }
            }

            ApplyCachedSizing(
                _dataVisualizer._inPlaceGhost,
                _dataVisualizer._draggedElement?.parent ?? _dataVisualizer.rootVisualElement
            );
            _dataVisualizer._inPlaceGhost.style.visibility = Visibility.Hidden;
        }

        private void ApplyCachedSizing(VisualElement ghost, VisualElement container)
        {
            float width;
            if (_dataVisualizer._activeDragType == DataVisualizer.DragType.Namespace)
            {
                width =
                    container?.resolvedStyle.width
                    ?? _dataVisualizer.rootVisualElement.resolvedStyle.width;
                ghost.style.marginLeft = 0f;
                ghost.style.marginRight = 0f;
            }
            else
            {
                width = _cachedDragWidth;
                if (width <= 0f && container != null)
                {
                    width = container.resolvedStyle.width;
                }

                if (width <= 0f)
                {
                    width = _dataVisualizer.rootVisualElement.resolvedStyle.width;
                }

                ghost.style.marginLeft = _cachedMarginLeft;
                ghost.style.marginRight = _cachedMarginRight;
            }

            if (width <= 0f)
            {
                width = _dataVisualizer._draggedElement?.layout.width ?? 1f;
            }

            ghost.style.width = width > 0f ? width : 1f;
            ghost.style.minWidth = ghost.style.width;

            float height = _cachedDragHeight;
            if (height <= 0f)
            {
                height = _dataVisualizer._draggedElement?.layout.height ?? 1f;
            }
            if (height <= 0f)
            {
                height = _dataVisualizer._draggedElement?.contentRect.height ?? 1f;
            }

            height = Mathf.Max(1f, height);
            ghost.style.height = height;
            ghost.style.minHeight = height;
            ghost.style.flexBasis = height;

            ghost.style.marginTop = _cachedMarginTop;
            ghost.style.marginBottom = _cachedMarginBottom;
            ghost.style.flexGrow = _cachedFlexGrow;
            ghost.style.flexShrink = _cachedFlexShrink;
        }

        private void UpdateAndSaveTypeOrder(string namespaceKey, IReadOnlyList<Type> orderedTypes)
        {
            _dataVisualizer.SetTypeOrderForNamespace(
                namespaceKey,
                orderedTypes.Select(t => t.FullName).ToList()
            );
        }
    }
}
