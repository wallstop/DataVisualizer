namespace WallstopStudios.DataVisualizer.Editor
{
#if UNITY_EDITOR
    using System;

    internal enum FocusArea
    {
        [Obsolete("Please use a valid value")]
        Unknown = 0,
        TypeList = 1,
        AddTypePopover = 2,
        SearchResultsPopover = 3,
        None = 4,
    }
#endif
}
