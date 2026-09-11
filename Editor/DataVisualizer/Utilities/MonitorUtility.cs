namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using System.Globalization;
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
            float constrainedWidth = Mathf.Min(width, placementArea.width);
            float constrainedHeight = Mathf.Min(height, placementArea.height);
            float x = placementArea.x + (placementArea.width - constrainedWidth) / 2f;
            float y = placementArea.y + (placementArea.height - constrainedHeight) / 2f;
            return new Rect(x, y, constrainedWidth, constrainedHeight);
        }

        public static Vector2 SelectPreferredSize(
            Vector2 savedSize,
            bool hasSavedSize,
            Vector2 currentSize,
            Vector2 minimumSize
        )
        {
            if (hasSavedSize && IsUsableSize(savedSize))
            {
                return savedSize;
            }

            return NormalizePreferredSize(currentSize, minimumSize);
        }

        public static Vector2 CalculateWindowMinimumSize(
            Vector2 minimumSize,
            Vector2 temporaryClampedSize,
            bool temporaryClampIsActive
        )
        {
            if (!temporaryClampIsActive || !IsUsableSize(temporaryClampedSize))
            {
                return minimumSize;
            }

            return new Vector2(
                Mathf.Min(minimumSize.x, temporaryClampedSize.x),
                Mathf.Min(minimumSize.y, temporaryClampedSize.y)
            );
        }

        public static bool IsSameSize(Vector2 lhs, Vector2 rhs)
        {
            return IsUsableSize(lhs)
                && IsUsableSize(rhs)
                && Mathf.Approximately(lhs.x, rhs.x)
                && Mathf.Approximately(lhs.y, rhs.y);
        }

        public static Vector2 NormalizePreferredSize(Vector2 size, Vector2 minimumSize)
        {
            if (!IsUsableSize(size))
            {
                return minimumSize;
            }

            return new Vector2(Mathf.Max(minimumSize.x, size.x), Mathf.Max(minimumSize.y, size.y));
        }

        public static bool ShouldCapturePreferredSize(
            bool packagePlacementPending,
            bool hasObservedInitialGeometry,
            bool isDocked
        )
        {
            return !packagePlacementPending && hasObservedInitialGeometry && !isDocked;
        }

        public static string SerializeSize(Vector2 size)
        {
            return size.x.ToString("R", CultureInfo.InvariantCulture)
                + ","
                + size.y.ToString("R", CultureInfo.InvariantCulture);
        }

        public static bool TryParseSize(string serializedSize, out Vector2 size)
        {
            if (!string.IsNullOrEmpty(serializedSize))
            {
                int separatorIndex = serializedSize.IndexOf(',');
                if (
                    0 < separatorIndex
                    && separatorIndex == serializedSize.LastIndexOf(',')
                    && float.TryParse(
                        serializedSize.Substring(0, separatorIndex),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float width
                    )
                    && float.TryParse(
                        serializedSize.Substring(separatorIndex + 1),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float height
                    )
                )
                {
                    Vector2 candidate = new(width, height);
                    if (IsUsableSize(candidate))
                    {
                        size = candidate;
                        return true;
                    }
                }
            }

            size = default;
            return false;
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
            return IsPositiveFinite(rect.width)
                && IsPositiveFinite(rect.height)
                && !float.IsNaN(rect.x)
                && !float.IsNaN(rect.y)
                && !float.IsInfinity(rect.x)
                && !float.IsInfinity(rect.y);
        }

        private static bool IsUsableSize(Vector2 size)
        {
            return IsPositiveFinite(size.x) && IsPositiveFinite(size.y);
        }

        private static bool IsPositiveFinite(float value)
        {
            return 0 < value && !float.IsNaN(value) && !float.IsInfinity(value);
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
