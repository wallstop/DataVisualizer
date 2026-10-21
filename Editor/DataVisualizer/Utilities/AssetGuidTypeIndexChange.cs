namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    public readonly struct AssetGuidTypeIndexChange
    {
        public string Path { get; }
        public string PreviousGuid { get; }

        public AssetGuidTypeIndexChange(string path, string previousGuid)
        {
            Path = path;
            PreviousGuid = previousGuid;
        }
    }
#endif
}
