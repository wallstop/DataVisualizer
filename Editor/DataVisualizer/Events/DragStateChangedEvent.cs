namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using WallstopStudios.DataVisualizer.Editor.State;

    internal sealed class DragStateChangedEvent
    {
        public DragStateChangedEvent(
            VisualizerSessionState.DragState.DragOperationKind operation,
            bool isActive,
            bool altPressed,
            bool controlPressed,
            bool shiftPressed
        )
        {
            Operation = operation;
            IsActive = isActive;
            AltPressed = altPressed;
            ControlPressed = controlPressed;
            ShiftPressed = shiftPressed;
        }

        public VisualizerSessionState.DragState.DragOperationKind Operation { get; }

        public bool IsActive { get; }

        public bool AltPressed { get; }

        public bool ControlPressed { get; }

        public bool ShiftPressed { get; }
    }
}
