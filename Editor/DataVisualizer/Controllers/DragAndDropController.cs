namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Text;
    using Data;
    using Events;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.State;
    using DragOperationKind = WallstopStudios.DataVisualizer.Editor.State.VisualizerSessionState.DragState.DragOperationKind;
    using Object = UnityEngine.Object;

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
        private float _cachedLayoutHeight;

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

        public void HandlePointerMove(PointerMoveEvent evt)
        {
            if (
                _dataVisualizer._draggedElement == null
                || !_dataVisualizer._draggedElement.HasPointerCapture(evt.pointerId)
            )
            {
                return;
            }

            DataVisualizer.DragType activeDragType = _dataVisualizer._activeDragType;
            if (activeDragType == DataVisualizer.DragType.None)
            {
                SynchronizeDragState(DragOperationKind.None, false, false, false);
                return;
            }

            bool altPressed = evt.altKey;
            bool controlPressed = evt.ctrlKey || evt.commandKey || evt.actionKey;
            bool shiftPressed = evt.shiftKey;

            if (_dataVisualizer._dragGhost != null)
            {
                _dataVisualizer._dragGhost.style.left =
                    evt.position.x - _dataVisualizer._dragGhost.resolvedStyle.width / 2f;
                _dataVisualizer._dragGhost.style.top =
                    evt.position.y - _dataVisualizer._dragGhost.resolvedStyle.height;
            }

            if (!_dataVisualizer._isDragging)
            {
                _dataVisualizer._isDragging = true;
                string dragText = DetermineDragLabel();
                StartDragVisuals(evt.position, dragText);
            }

            if (_dataVisualizer._isDragging)
            {
                UpdateInPlaceGhostPosition(evt.position);
            }

            SynchronizeDragState(
                MapDragOperation(activeDragType),
                altPressed,
                controlPressed,
                shiftPressed
            );
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
                int candidateIndex = 0;

                for (int i = 0; i < childCount; ++i)
                {
                    VisualElement child = container.ElementAt(i);
                    if (ReferenceEquals(child, _dataVisualizer._inPlaceGhost))
                    {
                        continue;
                    }

                    float midpoint = child.layout.yMin + child.layout.height * 0.5f;
                    if (localPointerPos.y < midpoint)
                    {
                        break;
                    }

                    candidateIndex++;
                }

                targetIndex = candidateIndex;
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

            targetIndex = Mathf.Clamp(targetIndex, 0, Math.Max(0, childCount - 1));
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

                AdjustGhostMargins(container, normalizedIndex, localPointerPos.y);
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
                ResetContainerPadding();
                _dataVisualizer._lastGhostInsertIndex = -1;
                _dataVisualizer._lastGhostParent = null;
            }
        }

        private void AdjustGhostMargins(
            VisualElement container,
            int normalizedIndex,
            float pointerLocalY
        )
        {
            if (
                container == null
                || _dataVisualizer._inPlaceGhost == null
                || _dataVisualizer._activeDragType != DataVisualizer.DragType.Object
            )
            {
                return;
            }

            VisualElement referenceElement = GetReferenceElementForGhost(container);

            float leadingMargin = _cachedMarginTop;
            float leadingMarginLeft = _cachedMarginLeft;
            float leadingMarginRight = _cachedMarginRight;
            float leadingHeight = _cachedDragHeight;
            float leadingWidth = _cachedDragWidth;

            float resolvedLeadingMarginTop =
                referenceElement != null
                    ? ResolveMarginTop(referenceElement, leadingMargin)
                    : leadingMargin;
            float resolvedLeadingMarginLeft =
                referenceElement != null
                    ? ResolveMarginLeft(referenceElement, leadingMarginLeft)
                    : leadingMarginLeft;
            float resolvedLeadingMarginRight =
                referenceElement != null
                    ? ResolveMarginRight(referenceElement, leadingMarginRight)
                    : leadingMarginRight;
            float resolvedLeadingMarginBottom =
                referenceElement != null
                    ? ResolveMarginBottom(referenceElement, _cachedMarginBottom)
                    : _cachedMarginBottom;
            float ghostMarginBottom = Mathf.Max(0f, resolvedLeadingMarginBottom);

            leadingMarginLeft = Mathf.Max(0f, resolvedLeadingMarginLeft);
            leadingMarginRight = Mathf.Max(0f, resolvedLeadingMarginRight);

            float containerPaddingTop = Mathf.Max(0f, _cachedMarginTop);
            if (normalizedIndex == 0)
            {
                container.style.paddingTop = new Length(containerPaddingTop, LengthUnit.Pixel);
            }
            else
            {
                container.style.paddingTop = StyleKeyword.Null;
            }

            _dataVisualizer._inPlaceGhost.style.height = new Length(
                Mathf.Max(1f, leadingHeight),
                LengthUnit.Pixel
            );
            _dataVisualizer._inPlaceGhost.style.minHeight = new Length(
                Mathf.Max(1f, leadingHeight),
                LengthUnit.Pixel
            );
            _dataVisualizer._inPlaceGhost.style.flexBasis = new Length(
                Mathf.Max(1f, leadingHeight),
                LengthUnit.Pixel
            );
            _dataVisualizer._inPlaceGhost.style.width = new Length(
                Mathf.Max(1f, leadingWidth),
                LengthUnit.Pixel
            );
            _dataVisualizer._inPlaceGhost.style.minWidth = new Length(
                Mathf.Max(1f, leadingWidth),
                LengthUnit.Pixel
            );

            _dataVisualizer._inPlaceGhost.style.marginTop = new Length(0f, LengthUnit.Pixel);
            _dataVisualizer._inPlaceGhost.style.marginLeft = new Length(
                leadingMarginLeft,
                LengthUnit.Pixel
            );
            _dataVisualizer._inPlaceGhost.style.marginRight = new Length(
                leadingMarginRight,
                LengthUnit.Pixel
            );
            _dataVisualizer._inPlaceGhost.style.marginBottom = new Length(
                ghostMarginBottom,
                LengthUnit.Pixel
            );

#if UNITY_EDITOR
            LogGhostDiagnostics(
                container,
                normalizedIndex,
                pointerLocalY,
                referenceElement,
                0f,
                leadingMarginLeft,
                leadingMarginRight,
                ghostMarginBottom,
                leadingHeight,
                leadingHeight
            );
#endif
        }

        private VisualElement GetReferenceElementForGhost(VisualElement container)
        {
            if (container == null)
            {
                return null;
            }

            VisualElement previous = null;
            bool ghostSeen = false;

            for (int index = 0; index < container.childCount; index++)
            {
                VisualElement candidate = container.ElementAt(index);
                if (ReferenceEquals(candidate, _dataVisualizer._inPlaceGhost))
                {
                    ghostSeen = true;
                    continue;
                }

                if (candidate == null)
                {
                    continue;
                }

                if (ghostSeen)
                {
                    return candidate;
                }

                previous = candidate;
            }

            return previous;
        }

        private static float ResolveMarginTop(VisualElement reference, float fallback)
        {
            float resolvedMargin = reference.resolvedStyle.marginTop;
            if (!float.IsNaN(resolvedMargin))
            {
                return resolvedMargin;
            }

            return fallback > 0f ? fallback : 0f;
        }

        private static float ResolveMarginLeft(VisualElement reference, float fallback)
        {
            float resolvedMargin = reference.resolvedStyle.marginLeft;
            if (!float.IsNaN(resolvedMargin))
            {
                return resolvedMargin;
            }

            return fallback;
        }

        private static float ResolveMarginRight(VisualElement reference, float fallback)
        {
            float resolvedMargin = reference.resolvedStyle.marginRight;
            if (!float.IsNaN(resolvedMargin))
            {
                return resolvedMargin;
            }

            return fallback;
        }

        private static float ResolveMarginBottom(VisualElement reference, float fallback)
        {
            float resolvedMargin = reference.resolvedStyle.marginBottom;
            if (!float.IsNaN(resolvedMargin))
            {
                return resolvedMargin;
            }

            return fallback;
        }

        private static float ResolveHeight(VisualElement reference, float fallback)
        {
            float resolvedHeight = reference.resolvedStyle.height;
            if (!float.IsNaN(resolvedHeight) && resolvedHeight > 0f)
            {
                return resolvedHeight;
            }

            float layoutHeight = reference.layout.height;
            if (layoutHeight > 0f)
            {
                return layoutHeight;
            }

            float contentHeight = reference.contentRect.height;
            if (contentHeight > 0f)
            {
                return contentHeight;
            }

            return fallback;
        }

        private static float ResolveWidth(VisualElement reference, float fallback)
        {
            float resolvedWidth = reference.resolvedStyle.width;
            if (!float.IsNaN(resolvedWidth) && resolvedWidth > 0f)
            {
                return resolvedWidth;
            }

            float layoutWidth = reference.layout.width;
            if (layoutWidth > 0f)
            {
                return layoutWidth;
            }

            float contentWidth = reference.contentRect.width;
            if (contentWidth > 0f)
            {
                return contentWidth;
            }

            return fallback;
        }

        private void ResetContainerPadding()
        {
            if (_dataVisualizer._lastGhostParent != null)
            {
                _dataVisualizer._lastGhostParent.style.paddingTop = StyleKeyword.Null;
            }
        }

#if UNITY_EDITOR
        private void LogGhostDiagnostics(
            VisualElement container,
            int normalizedIndex,
            float pointerLocalY,
            VisualElement referenceElement,
            float marginTop,
            float marginLeft,
            float marginRight,
            float marginBottom,
            float referenceHeight,
            float appliedHeight
        )
        {
            StringBuilder builder = new StringBuilder();
            builder
                .Append("UpdateGhost -> index=")
                .Append(normalizedIndex)
                .Append(", pointerY=")
                .Append(pointerLocalY.ToString("F2"))
                .Append(", containerChildren=")
                .Append(container?.childCount ?? -1)
                .Append(", margins(top,left,right,bottom)= (")
                .Append(marginTop.ToString("F2"))
                .Append(", ")
                .Append(marginLeft.ToString("F2"))
                .Append(", ")
                .Append(marginRight.ToString("F2"))
                .Append(", ")
                .Append(marginBottom.ToString("F2"))
                .Append(")")
                .Append(", ghostHeight=")
                .Append(appliedHeight.ToString("F2"))
                .Append(", refName=")
                .Append(referenceElement?.name ?? "<none>")
                .Append(", refMargins(top,bottom)= (")
                .Append(
                    referenceElement != null
                        ? referenceElement.resolvedStyle.marginTop.ToString()
                        : "<n/a>"
                )
                .Append(", ")
                .Append(
                    referenceElement != null
                        ? referenceElement.resolvedStyle.marginBottom.ToString()
                        : "<n/a>"
                )
                .Append(")")
                .Append(", refHeight=")
                .Append(referenceElement != null ? referenceHeight.ToString("F2") : "<n/a>");

            DataVisualizer.LogReorderDebug(builder.ToString());
        }
#endif

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

            ResetContainerPadding();

            _cachedDragWidth = 0f;
            _cachedDragHeight = 0f;
            _cachedMarginLeft = 0f;
            _cachedMarginRight = 0f;
            _cachedMarginTop = 0f;
            _cachedMarginBottom = 0f;
            _cachedFlexGrow = 0f;
            _cachedFlexShrink = 0f;

            SynchronizeDragState(DragOperationKind.None, false, false, false);
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
            bool altPressed = evt.altKey;
            bool controlPressed = evt.ctrlKey || evt.commandKey || evt.actionKey;
            bool shiftPressed = evt.shiftKey;

            VisualElement draggedElement = _dataVisualizer._draggedElement;
            try
            {
                _dataVisualizer._draggedElement.ReleasePointer(pointerId);

                if (!performDrop)
                {
                    return;
                }

                SynchronizeDragState(
                    MapDragOperation(dropType),
                    altPressed,
                    controlPressed,
                    shiftPressed
                );

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
            ResetContainerPadding();
            if (
                _dataVisualizer._draggedElement == null
                || _dataVisualizer._draggedData is not ScriptableObject draggedObject
            )
            {
#if UNITY_EDITOR
                DataVisualizer.LogReorderDebug(
                    "PerformObjectDrop aborted: dragged element or data missing"
                );
#endif
                return;
            }

            if (_dataVisualizer.GetObjectListContentContainer() == null || targetIndex < 0)
            {
#if UNITY_EDITOR
                DataVisualizer.LogReorderDebug(
                    $"PerformObjectDrop aborted: container null or targetIndex {targetIndex} < 0"
                );
#endif
                return;
            }

            ObjectListState listState = _dataVisualizer.ObjectListState;

            if (listState.DisplayedObjects.Count == 0)
            {
#if UNITY_EDITOR
                DataVisualizer.LogReorderDebug(
                    "PerformObjectDrop aborted: displayed object list empty"
                );
#endif
                return;
            }

            Type selectedType = _dataVisualizer._namespaceController.SelectedType;
            if (selectedType == null)
            {
#if UNITY_EDITOR
                DataVisualizer.LogReorderDebug("PerformObjectDrop aborted: no selected type");
#endif
                return;
            }

            int filteredCount = listState.FilteredObjects.Count;
            int displayTargetIndex = Mathf.Clamp(targetIndex, 0, listState.DisplayedObjects.Count);
            int globalTargetIndex = Mathf.Clamp(
                listState.DisplayStartIndex + displayTargetIndex,
                0,
                filteredCount
            );

            int sourceIndex = listState.FilteredObjectsBuffer.IndexOf(draggedObject);
            if (sourceIndex >= 0 && sourceIndex < globalTargetIndex)
            {
                globalTargetIndex = Mathf.Max(0, globalTargetIndex - 1);
            }
#if UNITY_EDITOR
            DataVisualizer.LogReorderDebug(
                $"Publishing ObjectReorderRequestedEvent type={selectedType.FullName} sourceIndex={sourceIndex} targetIndex={globalTargetIndex}"
            );
#endif
            VisualizerSessionState.DragState dragState = _dataVisualizer.SessionState.Drag;
            _eventHub?.Publish(
                new ObjectReorderRequestedEvent(
                    selectedType,
                    draggedObject,
                    globalTargetIndex,
                    dragState.AltPressed,
                    dragState.ControlPressed,
                    dragState.ShiftPressed
                )
            );
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
            ResetContainerPadding();

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

#if UNITY_EDITOR
            DataVisualizer.LogReorderDebug(
                $"Type drop request for namespace '{namespaceKey}' currentIndex={currentIndex} targetIndex={targetIndex}"
            );
            _dataVisualizer.LogTypeOrder("Type order before drop", types);
#endif
            if (currentIndex < targetIndex)
            {
                targetIndex--;
            }

            types.Remove(draggedType);
            targetIndex = Mathf.Clamp(targetIndex, 0, types.Count);
            types.Insert(targetIndex, draggedType);

            UpdateAndSaveTypeOrder(namespaceKey, types);
            _dataVisualizer._namespacePanelController?.BuildNamespaceView();
#if UNITY_EDITOR
            _dataVisualizer.LogTypeOrder("Type order after drop", types);
#endif
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
            _cachedLayoutHeight = resolvedHeight > 0f ? resolvedHeight : 1f;
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
                    style =
                    {
                        height = 0f,
                        marginTop = 0f,
                        marginBottom = 0f,
                        marginLeft = 0f,
                        marginRight = 0f,
                    },
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

            float resolvedHeight = _dataVisualizer._draggedElement.resolvedStyle.height;
            if (float.IsNaN(resolvedHeight) || resolvedHeight <= 0f)
            {
                resolvedHeight = _cachedLayoutHeight;
            }

            _dataVisualizer._inPlaceGhost.style.height = new Length(
                resolvedHeight,
                LengthUnit.Pixel
            );
            _dataVisualizer._inPlaceGhost.style.minHeight = new Length(
                resolvedHeight,
                LengthUnit.Pixel
            );
            _dataVisualizer._inPlaceGhost.style.flexBasis = new Length(
                resolvedHeight,
                LengthUnit.Pixel
            );

            float marginTop = _dataVisualizer._draggedElement.resolvedStyle.marginTop;
            float marginBottom = _dataVisualizer._draggedElement.resolvedStyle.marginBottom;
            float marginLeft = _dataVisualizer._draggedElement.resolvedStyle.marginLeft;
            float marginRight = _dataVisualizer._draggedElement.resolvedStyle.marginRight;

            if (float.IsNaN(marginTop))
            {
                marginTop = _cachedMarginTop;
            }
            if (float.IsNaN(marginBottom))
            {
                marginBottom = _cachedMarginBottom;
            }
            if (float.IsNaN(marginLeft))
            {
                marginLeft = _cachedMarginLeft;
            }
            if (float.IsNaN(marginRight))
            {
                marginRight = _cachedMarginRight;
            }

            _dataVisualizer._inPlaceGhost.style.marginTop = new Length(marginTop, LengthUnit.Pixel);
            _dataVisualizer._inPlaceGhost.style.marginBottom = new Length(
                marginBottom,
                LengthUnit.Pixel
            );
            _dataVisualizer._inPlaceGhost.style.marginLeft = new Length(
                marginLeft,
                LengthUnit.Pixel
            );
            _dataVisualizer._inPlaceGhost.style.marginRight = new Length(
                marginRight,
                LengthUnit.Pixel
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
                ghost.style.marginLeft = new Length(0f, LengthUnit.Pixel);
                ghost.style.marginRight = new Length(0f, LengthUnit.Pixel);
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

                ghost.style.marginLeft = new Length(_cachedMarginLeft, LengthUnit.Pixel);
                ghost.style.marginRight = new Length(_cachedMarginRight, LengthUnit.Pixel);
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

            ghost.style.marginTop = new Length(_cachedMarginTop, LengthUnit.Pixel);
            ghost.style.marginBottom = new Length(_cachedMarginBottom, LengthUnit.Pixel);
            ghost.style.flexGrow = _cachedFlexGrow;
            ghost.style.flexShrink = _cachedFlexShrink;
        }

        private string DetermineDragLabel()
        {
            string baseLabel = _dataVisualizer._draggedData switch
            {
                IDisplayable displayable => displayable.Title,
                Object dataObject => dataObject.name,
                string namespaceKey => namespaceKey,
                Type type =>
                    WallstopStudios.DataVisualizer.Editor.NamespaceController.GetTypeDisplayName(
                        type
                    ),
                _ => "Dragging Item",
            };

            return baseLabel;
        }

        private static DragOperationKind MapDragOperation(DataVisualizer.DragType dragType)
        {
            switch (dragType)
            {
                case DataVisualizer.DragType.Namespace:
                {
                    return DragOperationKind.Namespace;
                }
                case DataVisualizer.DragType.Type:
                {
                    return DragOperationKind.Type;
                }
                case DataVisualizer.DragType.Object:
                {
                    return DragOperationKind.Object;
                }
                default:
                {
                    return DragOperationKind.None;
                }
            }
        }

        private void SynchronizeDragState(
            DragOperationKind operation,
            bool altPressed,
            bool controlPressed,
            bool shiftPressed
        )
        {
            VisualizerSessionState.DragState dragState = _dataVisualizer.SessionState.Drag;
            bool operationChanged = dragState.SetOperation(operation);
            bool modifiersChanged = dragState.SetModifiers(
                altPressed,
                controlPressed,
                shiftPressed
            );

            if (operationChanged || modifiersChanged)
            {
                PublishDragState(dragState);
            }
        }

        private void PublishDragState(VisualizerSessionState.DragState dragState)
        {
            _eventHub?.Publish(
                new DragStateChangedEvent(
                    dragState.Operation,
                    dragState.IsActive,
                    dragState.AltPressed,
                    dragState.ControlPressed,
                    dragState.ShiftPressed
                )
            );
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
