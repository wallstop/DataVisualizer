namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR_OSX
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGSize
    {
        public double width;
        public double height;
    }
#endif
}
