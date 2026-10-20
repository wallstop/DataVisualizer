namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Distinguishes explicit deselection gestures from incidental empty ListView notifications.</summary>
    public sealed class ObjectListSelectionInput : IDisposable
    {
        public bool AllowsEmptySelection { get; private set; }

        private readonly ListView _list;
        private readonly VisualElement _root;
        private IVisualElementScheduledItem _reset;

        public ObjectListSelectionInput(ListView list, VisualElement root)
        {
            _list = list ?? throw new ArgumentNullException(nameof(list));
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _list.RegisterCallback<PointerDownEvent>(BeginPointer, TrickleDown.TrickleDown);
            _list.RegisterCallback<PointerUpEvent>(BeginPointer, TrickleDown.TrickleDown);
            _list.RegisterCallback<KeyDownEvent>(BeginKey, TrickleDown.TrickleDown);
            _list.RegisterCallback<KeyUpEvent>(BeginKey, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerCancelEvent>(CancelInput, TrickleDown.TrickleDown);
        }

        private static bool IsButton(object target)
        {
            return target is VisualElement element && (element is Button || element.GetFirstAncestorOfType<Button>() != null);
        }

        public void BlockEmptySelection()
        {
            SetInputState(false);
        }

        public void Dispose()
        {
            SetInputState(false);
            _list.UnregisterCallback<PointerDownEvent>(BeginPointer, TrickleDown.TrickleDown);
            _list.UnregisterCallback<PointerUpEvent>(BeginPointer, TrickleDown.TrickleDown);
            _list.UnregisterCallback<KeyDownEvent>(BeginKey, TrickleDown.TrickleDown);
            _list.UnregisterCallback<KeyUpEvent>(BeginKey, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerCancelEvent>(CancelInput, TrickleDown.TrickleDown);
        }

        private void BeginPointer(EventBase evt)
        {
            IPointerEvent pointer = (IPointerEvent)evt;
            bool rowTarget = false;
            for (VisualElement element = evt.target as VisualElement; element != null && element != _list; element = element.parent)
            {
                if (element.userData is ScriptableObject)
                {
                    rowTarget = true;
                    break;
                }
            }
            SetInputState(pointer.button == 0 && !IsButton(evt.target) && (pointer.ctrlKey || pointer.commandKey || !rowTarget));
        }

        private void BeginKey(EventBase evt)
        {
            IKeyboardEvent key = (IKeyboardEvent)evt;
            SetInputState(!IsButton(evt.target) && key.keyCode == KeyCode.Space && (key.ctrlKey || key.commandKey));
        }

        private void CancelInput(EventBase evt)
        {
            SetInputState(false);
        }

        private void ClearInput()
        {
            SetInputState(false);
        }

        private void SetInputState(bool allow)
        {
            _reset?.Pause();
            _reset = null;
            AllowsEmptySelection = allow;
            if (allow)
            {
                // Input authorization ends after this dispatch, even if mouse capture bypasses release callbacks.
                _reset = _root.schedule.Execute(ClearInput);
            }
        }
    }
}
