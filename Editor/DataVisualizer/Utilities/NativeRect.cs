namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR_WIN
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
#endif
}
