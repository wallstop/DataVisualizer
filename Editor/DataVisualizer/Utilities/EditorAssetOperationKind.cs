namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;

    public enum EditorAssetOperationKind
    {
        [Obsolete("Please use a valid value")]
        Unknown = 0,
        Create = 1,
        Clone = 2,
        Rename = 3,
        Move = 4,
        Delete = 5,
    }
}
