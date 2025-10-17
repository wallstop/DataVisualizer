namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;
    using UnityEngine;

    internal sealed class ObjectReorderRequestedEvent
    {
        public ObjectReorderRequestedEvent(
            Type targetType,
            ScriptableObject draggedObject,
            int targetIndex,
            bool altPressed,
            bool controlPressed,
            bool shiftPressed
        )
        {
            TargetType = targetType;
            DraggedObject = draggedObject;
            TargetIndex = targetIndex;
            AltPressed = altPressed;
            ControlPressed = controlPressed;
            ShiftPressed = shiftPressed;
        }

        public Type TargetType { get; }

        public ScriptableObject DraggedObject { get; }

        public int TargetIndex { get; }

        public bool AltPressed { get; }

        public bool ControlPressed { get; }

        public bool ShiftPressed { get; }
    }
}
