namespace WallstopStudios.DataVisualizer.Helper
{
    internal static class PathHelper
    {
        public static string SanitizePath(this string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
