namespace WallstopStudios.DataVisualizer.Editor
{
#if UNITY_EDITOR
    using System;

    internal enum LabelFilterSection
    {
        [Obsolete("Please use a valid value")]
        None = 0,
        Available = 1,
        AND = 2,
        OR = 3,
    }
#endif
}
