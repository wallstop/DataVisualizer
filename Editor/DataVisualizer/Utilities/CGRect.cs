namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR_OSX
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public CGPoint origin;
        public CGSize size;
    }
#endif
}
