namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using UnityEngine;
    using UnityEngine.UIElements;

    public static class PopoverPlacement
    {
        private const float StyleSheetMaxWidth = 500f;
        private const float StyleSheetMaxHeight = 500f;

        public static void ApplyViewportLimits(VisualElement popover, Vector2 available)
        {
            bool form = popover.ClassListContains("popover");
            // Restore the stylesheet cap once the viewport no longer constrains it.
            popover.style.maxWidth = form && StyleSheetMaxWidth <= available.x ? StyleKeyword.Null : new StyleLength(Mathf.Max(0f, available.x));
            popover.style.maxHeight = !form && StyleSheetMaxHeight <= available.y ? StyleKeyword.Null : new StyleLength(Mathf.Max(0f, available.y));
        }

        public static Rect FitToWindow(Rect requested, Vector2 available)
        {
            float width = Mathf.Clamp(requested.width, 0f, Mathf.Max(0f, available.x));
            float height = Mathf.Clamp(requested.height, 0f, Mathf.Max(0f, available.y));
            return new Rect(Mathf.Clamp(requested.x, 0f, available.x - width), Mathf.Clamp(requested.y, 0f, available.y - height), width, height);
        }
    }
}
