namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;

    internal enum AssetGuidTypeIndexState
    {
        [Obsolete("Please use a valid value")]
        Unknown = 0,
        Idle = 1,
        WaitingForPathSnapshot = 2,
        Classifying = 3,
        Complete = 4,
    }
#endif
}
