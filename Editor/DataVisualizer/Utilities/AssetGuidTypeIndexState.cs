namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    internal enum AssetGuidTypeIndexState
    {
        Idle,
        WaitingForPathSnapshot,
        Classifying,
        Complete,
    }
#endif
}
