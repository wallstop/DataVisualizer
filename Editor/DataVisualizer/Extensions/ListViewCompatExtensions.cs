namespace WallstopStudios.DataVisualizer.Editor.Extensions
{
    using System;
    using System.Collections.Generic;
    using UnityEngine.UIElements;

    /// <summary>
    /// Version-compat shims for <see cref="ListView"/> selection callbacks. The 2021.3-era
    /// <c>onSelectionChange</c> / <c>onItemsChosen</c> members were renamed to
    /// <c>selectionChanged</c> / <c>itemsChosen</c> in 2022.3 and the old names were removed in
    /// Unity 6, so package code must not reference either name directly.
    /// </summary>
    internal static class ListViewCompatExtensions
    {
        /// <summary>Subscribes to the list's selection-changed event across Unity versions.</summary>
        internal static void RegisterSelectionChangedCompat(
            this ListView listView,
            Action<IEnumerable<object>> callback
        )
        {
#if UNITY_2022_3_OR_NEWER
            listView.selectionChanged += callback;
#else
            listView.onSelectionChange += callback;
#endif
        }
    }
}
