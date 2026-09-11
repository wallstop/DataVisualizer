namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR_OSX
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGPoint
    {
        public double x;
        public double y;
    }
#endif
}
