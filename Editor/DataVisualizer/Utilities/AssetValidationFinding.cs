namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    public sealed class AssetValidationFinding
    {
        public string Code { get; }
        public string Guid { get; }
        public string Path { get; }
        public string PropertyPath { get; }
        public string Detail { get; }

        public AssetValidationFinding(string code, string guid, string path, string propertyPath, string detail)
        {
            Code = code;
            Guid = guid;
            Path = path;
            PropertyPath = propertyPath;
            Detail = detail;
        }
    }
}
