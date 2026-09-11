namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR_WIN
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NativeMonitorInfo
    {
        public uint cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }
#endif
}
