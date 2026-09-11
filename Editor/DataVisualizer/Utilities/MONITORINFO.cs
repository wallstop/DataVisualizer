namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR_WIN
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
#endif
}
