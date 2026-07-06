namespace WallstopStudios.DataVisualizer.Editor.Extensions
{
    using UnityEngine.UIElements;

    internal static class EventExtensions
    {
        /// <summary>
        /// Prevents the event's default action on Unity versions where
        /// <see cref="EventBase.PreventDefault"/> is supported. On Unity 2023.2+ that method
        /// is obsolete, so this is a no-op there: every caller pairs it with
        /// <see cref="EventBase.StopPropagation"/>, which consumes the event — the sanctioned
        /// replacement for the KeyDownEvents handled here. FocusController.IgnoreEvent (the
        /// other suggested replacement) only affects pointer/navigation events, so it is
        /// intentionally not used.
        /// </summary>
        internal static void PreventDefaultCompat(this EventBase evt)
        {
#if !UNITY_2023_2_OR_NEWER
            evt.PreventDefault();
#endif
        }
    }
}
