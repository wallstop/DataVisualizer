namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using UnityEngine;

    public static class LayoutGeometry
    {
        public static float ClampPersistedPaneWidth(
            float persistedWidth,
            float defaultWidth,
            float minimumWidth
        )
        {
            float safeMinimum = IsFinite(minimumWidth) ? Mathf.Max(0f, minimumWidth) : 0f;
            float safeDefault = IsFinite(defaultWidth)
                ? Mathf.Max(defaultWidth, safeMinimum)
                : safeMinimum;
            return IsFinite(persistedWidth) ? Mathf.Max(persistedWidth, safeMinimum) : safeDefault;
        }

        public static int ToInitialPaneDimension(float width)
        {
            if (!IsFinite(width) || width <= 0f)
            {
                return 1;
            }

            return Mathf.Max(1, Mathf.RoundToInt(width));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
