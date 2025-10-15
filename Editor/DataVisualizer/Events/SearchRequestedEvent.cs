namespace WallstopStudios.DataVisualizer.Editor.Events
{
    internal sealed class SearchRequestedEvent
    {
        public SearchRequestedEvent(string query, bool isCommit)
        {
            Query = query;
            IsCommit = isCommit;
        }

        public string Query { get; }

        public bool IsCommit { get; }
    }
}
