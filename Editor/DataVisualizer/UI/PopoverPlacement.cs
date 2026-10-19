namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using UnityEngine;
    using UnityEngine.UIElements;

    public static class PopoverPlacement
    {
        public static void ApplyViewportLimits(VisualElement popover, Vector2 available)
        {
            if (available.x < popover.resolvedStyle.width)
            {
                popover.style.width = Mathf.Max(0f, available.x);
            }
            if (available.y < popover.resolvedStyle.height)
            {
                popover.style.height = Mathf.Max(0f, available.y);
            }
        }

        public static void ReleaseViewportLimits(VisualElement popover)
        {
            popover.style.width = StyleKeyword.Null;
            popover.style.height = StyleKeyword.Null;
        }

        public static Rect FitToWindow(Rect requested, Vector2 available)
        {
            float width = Mathf.Clamp(requested.width, 0f, Mathf.Max(0f, available.x));
            float height = Mathf.Clamp(requested.height, 0f, Mathf.Max(0f, available.y));
            return new Rect(Mathf.Clamp(requested.x, 0f, available.x - width), Mathf.Clamp(requested.y, 0f, available.y - height), width, height);
        }
    }
}
