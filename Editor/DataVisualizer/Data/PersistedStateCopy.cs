namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System.Collections.Generic;

    internal static class PersistedStateCopy
    {
        internal static List<string> CloneStrings(List<string> source)
        {
            return source == null ? new List<string>() : new List<string>(source);
        }

        internal static List<NamespaceTypeOrder> CloneNamespaceTypeOrders(
            List<NamespaceTypeOrder> source
        )
        {
            List<NamespaceTypeOrder> clones = new(source?.Count ?? 0);
            if (source == null)
            {
                return clones;
            }

            foreach (NamespaceTypeOrder entry in source)
            {
                clones.Add(entry.Clone());
            }

            return clones;
        }

        internal static List<LastObjectSelectionEntry> CloneLastObjectSelections(
            List<LastObjectSelectionEntry> source
        )
        {
            List<LastObjectSelectionEntry> clones = new(source?.Count ?? 0);
            if (source == null)
            {
                return clones;
            }

            foreach (LastObjectSelectionEntry entry in source)
            {
                clones.Add(entry.Clone());
            }

            return clones;
        }

        internal static List<NamespaceCollapseState> CloneNamespaceCollapseStates(
            List<NamespaceCollapseState> source
        )
        {
            List<NamespaceCollapseState> clones = new(source?.Count ?? 0);
            if (source == null)
            {
                return clones;
            }

            foreach (NamespaceCollapseState entry in source)
            {
                clones.Add(entry.Clone());
            }

            return clones;
        }

        internal static List<TypeObjectOrder> CloneTypeObjectOrders(List<TypeObjectOrder> source)
        {
            List<TypeObjectOrder> clones = new(source?.Count ?? 0);
            if (source == null)
            {
                return clones;
            }

            foreach (TypeObjectOrder entry in source)
            {
                clones.Add(entry.Clone());
            }

            return clones;
        }

        internal static List<TypeLabelFilterConfig> CloneTypeLabelFilterConfigs(
            List<TypeLabelFilterConfig> source
        )
        {
            List<TypeLabelFilterConfig> clones = new(source?.Count ?? 0);
            if (source == null)
            {
                return clones;
            }

            foreach (TypeLabelFilterConfig entry in source)
            {
                clones.Add(entry.Clone());
            }

            return clones;
        }

        internal static List<ProcessorState> CloneProcessorStates(List<ProcessorState> source)
        {
            List<ProcessorState> clones = new(source?.Count ?? 0);
            if (source == null)
            {
                return clones;
            }

            foreach (ProcessorState entry in source)
            {
                clones.Add(entry.Clone());
            }

            return clones;
        }
    }
}
