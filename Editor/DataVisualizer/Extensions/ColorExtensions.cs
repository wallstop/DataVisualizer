namespace WallstopStudios.DataVisualizer.Editor.Extensions
{
    using UnityEngine;

    internal static class ColorExtensions
    {
        public static string ToHex(this Color color, bool includeAlpha = true)
        {
            int r = (int)(Mathf.Clamp01(color.r) * 255f);
            int g = (int)(Mathf.Clamp01(color.g) * 255f);
            int b = (int)(Mathf.Clamp01(color.b) * 255f);

            if (!includeAlpha)
            {
                return $"#{r:X2}{g:X2}{b:X2}";
            }

            int a = (int)(Mathf.Clamp01(color.a) * 255f);
            return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
        }
    }
}
