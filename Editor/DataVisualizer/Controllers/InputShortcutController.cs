namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Linq;
    using Events;
    using Extensions;
    using Styles;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class InputShortcutController
    {
        private readonly DataVisualizer _dataVisualizer;
        private readonly DataVisualizerEventHub _eventHub;

        public InputShortcutController(
            DataVisualizer dataVisualizer,
            DataVisualizerEventHub eventHub
        )
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
        }

        public void Register(VisualElement target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.RegisterCallback<KeyDownEvent>(HandleGlobalKeyDown, TrickleDown.TrickleDown);
        }

        public void Unregister(VisualElement target)
        {
            if (target == null)
            {
                return;
            }

            target.UnregisterCallback<KeyDownEvent>(HandleGlobalKeyDown, TrickleDown.TrickleDown);
        }

        public void RegisterPopoverKeyHandler(VisualElement dragHandle)
        {
            if (dragHandle == null)
            {
                throw new ArgumentNullException(nameof(dragHandle));
            }

            dragHandle.RegisterCallback<KeyDownEvent>(HandlePopoverKeyDown);
        }

        public void UnregisterPopoverKeyHandler(VisualElement dragHandle)
        {
            if (dragHandle == null)
            {
                return;
            }

            dragHandle.UnregisterCallback<KeyDownEvent>(HandlePopoverKeyDown);
        }

        private void HandleGlobalKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (_dataVisualizer._activePopover == _dataVisualizer._inspectorLabelSuggestionsPopover)
            {
                _dataVisualizer.HandleNewLabelInputKeyDown(evt);
                return;
            }

            VisualElement activePopover =
                _dataVisualizer._activeNestedPopover ?? _dataVisualizer._activePopover;
            if (activePopover != null && activePopover.style.display == DisplayStyle.Flex)
            {
                switch (evt.keyCode)
                {
                    case KeyCode.Escape:
                    {
                        _dataVisualizer.CloseActivePopover();
                        evt.PreventDefault();
                        evt.StopPropagation();
                        return;
                    }
                    case KeyCode.DownArrow:
                    case KeyCode.UpArrow:
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                    {
                        if (
                            _dataVisualizer._lastActiveFocusArea
                            == DataVisualizer.FocusArea.SearchResultsPopover
                        )
                        {
                            _dataVisualizer._lastEnterPressed = Time.realtimeSinceStartup;
                            _dataVisualizer.HandleSearchKeyDown(evt);
                            return;
                        }

                        if (
                            _dataVisualizer._lastActiveFocusArea
                            == DataVisualizer.FocusArea.AddTypePopover
                        )
                        {
                            _dataVisualizer._lastEnterPressed = Time.realtimeSinceStartup;
                            _dataVisualizer.HandleTypePopoverKeyDown(evt);
                            return;
                        }

                        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                        {
                            Button primaryButton = activePopover
                                .IterateChildrenRecursively()
                                .Where(child =>
                                    child.ClassListContains(
                                        StyleConstants.PopoverPrimaryActionClass
                                    )
                                )
                                .OfType<Button>()
                                .FirstOrDefault();

                            if (primaryButton?.userData is Action action)
                            {
                                action.Invoke();
                            }
                        }

                        break;
                    }
                }

                if (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.UpArrow)
                {
                    evt.PreventDefault();
                    evt.StopPropagation();
                    return;
                }
            }

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                {
                    bool navigationHandled = false;
                    switch (_dataVisualizer._lastActiveFocusArea)
                    {
                        case DataVisualizer.FocusArea.TypeList:
                        {
                            navigationHandled = true;
                            _eventHub.Publish(
                                new TypeNavigationRequestedEvent(TypeNavigationDirection.Next)
                            );
                            break;
                        }
                    }

                    if (navigationHandled)
                    {
                        evt.PreventDefault();
                        evt.StopPropagation();
                    }

                    break;
                }
                case KeyCode.UpArrow:
                {
                    bool navigationHandled = false;
                    switch (_dataVisualizer._lastActiveFocusArea)
                    {
                        case DataVisualizer.FocusArea.TypeList:
                        {
                            navigationHandled = true;
                            _eventHub.Publish(
                                new TypeNavigationRequestedEvent(TypeNavigationDirection.Previous)
                            );
                            break;
                        }
                    }

                    if (navigationHandled)
                    {
                        evt.PreventDefault();
                        evt.StopPropagation();
                    }

                    break;
                }
            }
        }

        private void HandlePopoverKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            VisualElement activePopover =
                _dataVisualizer._activeNestedPopover ?? _dataVisualizer._activePopover;
            if (activePopover == null || activePopover.style.display != DisplayStyle.Flex)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                {
                    _dataVisualizer.CloseActivePopover();
                    evt.PreventDefault();
                    evt.StopPropagation();
                    return;
                }
                case KeyCode.None when evt.character is '\n' or '\r':
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                {
                    Button primaryButton = activePopover
                        .IterateChildrenRecursively()
                        .Where(child =>
                            child.ClassListContains(StyleConstants.PopoverPrimaryActionClass)
                        )
                        .OfType<Button>()
                        .FirstOrDefault();

                    if (primaryButton?.userData is Action action)
                    {
                        action.Invoke();
                    }

                    evt.PreventDefault();
                    evt.StopPropagation();
                    return;
                }
            }
        }
    }
}
