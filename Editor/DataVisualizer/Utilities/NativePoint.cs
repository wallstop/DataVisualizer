namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR_WIN
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int x;
        public int y;
    }
#endif
}
