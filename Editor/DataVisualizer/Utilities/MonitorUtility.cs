namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;

    public static class MonitorUtility
    {
        private const float LastResortWidth = 1f;
        private const float LastResortHeight = 1f;

        public static Rect GetPrimaryMonitorRect()
        {
            try
            {
                Rect mainWindowRect = EditorGUIUtility.GetMainWindowPosition();
                if (IsUsable(mainWindowRect))
                {
                    return mainWindowRect;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    $"Unable to read the Unity main window position: {exception.Message}"
                );
            }

            Resolution currentResolution = Screen.currentResolution;
            if (currentResolution.width > 0 && currentResolution.height > 0)
            {
                return new Rect(0, 0, currentResolution.width, currentResolution.height);
            }

            // Keep callers from producing an invalid or NaN position when Unity has
            // not exposed window/display geometry yet (for example during startup).
            return new Rect(0, 0, LastResortWidth, LastResortHeight);
        }

        private static bool IsUsable(Rect rect)
        {
            return rect.width > 0
                && rect.height > 0
                && !float.IsNaN(rect.x)
                && !float.IsNaN(rect.y)
                && !float.IsNaN(rect.width)
                && !float.IsNaN(rect.height)
                && !float.IsInfinity(rect.x)
                && !float.IsInfinity(rect.y)
                && !float.IsInfinity(rect.width)
                && !float.IsInfinity(rect.height);
        }
    }
#endif
}
