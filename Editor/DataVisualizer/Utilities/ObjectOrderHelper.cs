namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;

    internal static class ObjectOrderHelper
    {
        internal static void ReorderItem<T>(List<T> items, T item, T insertBefore, T insertAfter)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (!items.Remove(item))
            {
                return;
            }

            int insertIndex = -1;
            if (insertBefore != null)
            {
                insertIndex = items.IndexOf(insertBefore);
            }

            if (insertIndex < 0 && insertAfter != null)
            {
                int afterIndex = items.IndexOf(insertAfter);
                if (afterIndex >= 0)
                {
                    insertIndex = afterIndex + 1;
                }
            }

            if (insertIndex < 0 || insertIndex > items.Count)
            {
                items.Add(item);
            }
            else
            {
                items.Insert(insertIndex, item);
            }
        }
    }
}
