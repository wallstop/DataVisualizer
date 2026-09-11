namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    public static class AssetGuidOrder
    {
        public static List<string> MergeLoadedOrder(
            IReadOnlyList<string> canonicalOrder,
            IReadOnlyList<string> loadedOrder
        )
        {
            List<string> mergedOrder = CopyDistinct(canonicalOrder);
            List<string> distinctLoadedOrder = CopyDistinct(loadedOrder);
            HashSet<string> canonicalLookup = new(mergedOrder, StringComparer.OrdinalIgnoreCase);
            HashSet<string> loadedLookup = new(
                distinctLoadedOrder,
                StringComparer.OrdinalIgnoreCase
            );
            List<string> loadedCanonicalGuids = new();
            foreach (string guid in distinctLoadedOrder)
            {
                if (canonicalLookup.Contains(guid))
                {
                    loadedCanonicalGuids.Add(guid);
                }
            }

            int loadedIndex = 0;
            for (int index = 0; index < mergedOrder.Count; index++)
            {
                if (loadedLookup.Contains(mergedOrder[index]))
                {
                    mergedOrder[index] = loadedCanonicalGuids[loadedIndex++];
                }
            }

            foreach (string guid in distinctLoadedOrder)
            {
                if (canonicalLookup.Add(guid))
                {
                    mergedOrder.Add(guid);
                }
            }

            return mergedOrder;
        }

        public static bool PlaceFirst(List<string> order, string guid)
        {
            return PlaceAt(order, guid, 0);
        }

        public static bool PlaceLast(List<string> order, string guid)
        {
            return PlaceAt(order, guid, order?.Count ?? 0);
        }

        public static bool PlaceAfter(List<string> order, string guid, string anchorGuid)
        {
            if (
                order == null
                || string.IsNullOrWhiteSpace(guid)
                || string.Equals(guid, anchorGuid, StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            int originalIndex = IndexOf(order, guid);
            if (0 <= originalIndex)
            {
                /*
                    Stable removal is required because this list is the persisted display order.
                    This path removes at most one item; swap-back would scramble neighboring GUIDs.
                */
                order.RemoveAt(originalIndex);
            }

            int anchorIndex = IndexOf(order, anchorGuid);
            int targetIndex = anchorIndex < 0 ? order.Count : anchorIndex + 1;
            order.Insert(targetIndex, guid);
            return originalIndex != targetIndex;
        }

        private static bool PlaceAt(List<string> order, string guid, int targetIndex)
        {
            if (order == null || string.IsNullOrWhiteSpace(guid))
            {
                return false;
            }

            int originalIndex = IndexOf(order, guid);
            if (0 <= originalIndex)
            {
                // Preserve relative order. This is one removal, not a repeated RemoveAt loop.
                order.RemoveAt(originalIndex);
                if (originalIndex < targetIndex)
                {
                    targetIndex--;
                }
            }

            targetIndex = Math.Max(0, Math.Min(targetIndex, order.Count));
            order.Insert(targetIndex, guid);
            return originalIndex != targetIndex;
        }

        private static List<string> CopyDistinct(IReadOnlyList<string> guids)
        {
            List<string> result = new();
            if (guids == null)
            {
                return result;
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < guids.Count; index++)
            {
                string guid = guids[index];
                if (!string.IsNullOrWhiteSpace(guid) && seen.Add(guid))
                {
                    result.Add(guid);
                }
            }

            return result;
        }

        private static int IndexOf(List<string> order, string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return -1;
            }

            for (int index = 0; index < order.Count; index++)
            {
                if (string.Equals(order[index], guid, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }
    }
#endif
}
