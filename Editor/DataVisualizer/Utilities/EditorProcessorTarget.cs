namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    /// <summary>An exact preview target and its last known operation outcome.</summary>
    public sealed class EditorProcessorTarget
    {
        public string Guid { get; }
        public string Path { get; }
        public string Code { get; }
        public string Detail { get; }

        public EditorProcessorTarget(string guid, string path, string code, string detail = null)
        {
            Guid = guid;
            Path = path;
            Code = code;
            Detail = detail;
        }
    }
}
