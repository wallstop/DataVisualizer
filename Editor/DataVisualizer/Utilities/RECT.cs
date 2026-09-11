namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR_WIN
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
#endif
}
