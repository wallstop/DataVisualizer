namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    public sealed class EditorAssetOperationTarget
    {
        public string Guid { get; }
        public string Path { get; }
        public string DestinationPath { get; }
        public string ResultGuid { get; }
        public string Code { get; }
        public string Detail { get; }

        public EditorAssetOperationTarget(string guid, string path, string destinationPath, string resultGuid = null, string code = "not_applied", string detail = null)
        {
            Guid = guid;
            Path = path;
            DestinationPath = destinationPath;
            ResultGuid = resultGuid;
            Code = code;
            Detail = detail;
        }
    }
}
