namespace WallstopStudios.DataVisualizer.Editor.Events
{
    internal sealed class DialogMessageRequestEvent
    {
        public DialogMessageRequestEvent(string title, string message, string closeText)
        {
            Title = title;
            Message = message;
            CloseText = closeText;
        }

        public string Title { get; }

        public string Message { get; }

        public string CloseText { get; }
    }
}
