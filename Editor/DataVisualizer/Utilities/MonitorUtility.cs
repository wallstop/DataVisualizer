namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;

    public static class MonitorUtility
    {
        [Obsolete("Use TryGetEditorPlacementRect instead.")]
        public static bool TryGetPrimaryMonitorRect(out Rect rect)
        {
            return TryGetEditorPlacementRect(out rect);
        }

        public static bool TryGetEditorPlacementRect(out Rect rect)
        {
            return TryResolveMonitorRect(
                EditorGUIUtility.GetMainWindowPosition,
                GetCurrentResolutionRect,
                out rect
            );
        }

        public static Rect CalculateCenteredRect(Rect placementArea, float width, float height)
        {
            float x = placementArea.x + (placementArea.width - width) / 2f;
            float y = placementArea.y + (placementArea.height - height) / 2f;
            return new Rect(x, y, width, height);
        }

        public static bool ShouldApplyInitialPlacement(bool initialSizeApplied, bool isDocked)
        {
            return !initialSizeApplied && !isDocked;
        }

        public static bool TryResolveMonitorRect(
            Func<Rect> preferredRectProvider,
            Func<Rect> fallbackRectProvider,
            out Rect rect
        )
        {
            if (TryGetUsableRect(preferredRectProvider, out Rect preferredRect))
            {
                rect = preferredRect;
                return true;
            }

            if (TryGetUsableRect(fallbackRectProvider, out Rect fallbackRect))
            {
                rect = fallbackRect;
                return true;
            }

            rect = default;
            return false;
        }

        private static Rect GetCurrentResolutionRect()
        {
            Resolution currentResolution = Screen.currentResolution;
            return new Rect(0, 0, currentResolution.width, currentResolution.height);
        }

        private static bool IsUsable(Rect rect)
        {
            return 0 < rect.width
                && 0 < rect.height
                && !float.IsNaN(rect.x)
                && !float.IsNaN(rect.y)
                && !float.IsNaN(rect.width)
                && !float.IsNaN(rect.height)
                && !float.IsInfinity(rect.x)
                && !float.IsInfinity(rect.y)
                && !float.IsInfinity(rect.width)
                && !float.IsInfinity(rect.height);
        }

        private static bool TryGetUsableRect(Func<Rect> rectProvider, out Rect rect)
        {
            try
            {
                Rect candidate = rectProvider();
                if (IsUsable(candidate))
                {
                    rect = candidate;
                    return true;
                }
            }
            catch (Exception) { }

            rect = default;
            return false;
        }
    }
#endif
}
