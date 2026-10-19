namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using UnityEngine;

    public static class PopoverPlacement
    {
        public static Rect FitToWindow(Rect requested, Vector2 available)
        {
            float width = Mathf.Clamp(requested.width, 0f, Mathf.Max(0f, available.x));
            float height = Mathf.Clamp(requested.height, 0f, Mathf.Max(0f, available.y));
            return new Rect(Mathf.Clamp(requested.x, 0f, available.x - width), Mathf.Clamp(requested.y, 0f, available.y - height), width, height);
        }
    }
}
