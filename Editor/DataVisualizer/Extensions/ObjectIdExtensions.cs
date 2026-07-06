namespace WallstopStudios.DataVisualizer.Editor.Extensions
{
    using System.Globalization;
    using UnityEngine;

    /// <summary>
    /// Helpers for deriving identifier strings from Unity objects. Public because the
    /// package's editor test assembly cannot access internals of the editor assembly:
    /// InternalsVisibleTo is not honored for this assembly pair in Unity's compilation
    /// (verified — the internal type is reported CS0122 from the test assembly even with
    /// a correct friend attribute compiled into the editor assembly).
    /// </summary>
    public static class ObjectIdExtensions
    {
        /// <summary>
        /// Returns a per-session unique identifier string for the object, suitable for
        /// building unique UI element names. NOT stable across editor sessions or domain
        /// reloads; do not persist.
        /// </summary>
        public static string GetObjectIdString(this Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(obj.GetEntityId()).ToString(CultureInfo.InvariantCulture);
#else
            return obj.GetInstanceID().ToString(CultureInfo.InvariantCulture);
#endif
        }
    }
}
