namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;

    internal sealed class DialogConfirmationRequestEvent
    {
        public DialogConfirmationRequestEvent(
            string title,
            string message,
            string confirmText,
            string cancelText,
            Action<bool> onResult
        )
        {
            Title = title;
            Message = message;
            ConfirmText = confirmText;
            CancelText = cancelText;
            OnResult = onResult;
        }

        public string Title { get; }

        public string Message { get; }

        public string ConfirmText { get; }

        public string CancelText { get; }

        public Action<bool> OnResult { get; }
    }
}
