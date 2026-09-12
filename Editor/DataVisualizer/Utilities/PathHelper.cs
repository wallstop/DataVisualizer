namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    internal static class PathHelper
    {
        public static string SanitizePath(this string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
