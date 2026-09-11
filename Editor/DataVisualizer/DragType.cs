namespace WallstopStudios.DataVisualizer.Editor
{
#if UNITY_EDITOR
    using System;

    internal enum DragType
    {
        [Obsolete("Please use a valid value")]
        Unknown = 0,
        None = 1,
        Namespace = 2,
        Type = 3,
    }
#endif
}
