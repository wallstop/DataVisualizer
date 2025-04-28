namespace WallstopStudios.Helper
{
    public static class PathHelper
    {
        public static string SanitizePath(this string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
