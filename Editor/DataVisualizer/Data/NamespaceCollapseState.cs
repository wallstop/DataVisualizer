namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;

    [Serializable]
    public sealed class NamespaceCollapseState
    {
        public string namespaceKey = string.Empty;
        public bool isCollapsed;

        public static bool SetCollapsed(
            List<NamespaceCollapseState> states,
            string namespaceKey,
            bool isCollapsed
        )
        {
            if (states == null || string.IsNullOrWhiteSpace(namespaceKey))
            {
                return false;
            }

            NamespaceCollapseState entry = states.Find(state =>
                string.Equals(state?.namespaceKey, namespaceKey, StringComparison.Ordinal)
            );
            if (entry == null)
            {
                states.Add(
                    new NamespaceCollapseState
                    {
                        namespaceKey = namespaceKey,
                        isCollapsed = isCollapsed,
                    }
                );
                return true;
            }

            if (entry.isCollapsed == isCollapsed)
            {
                return false;
            }

            entry.isCollapsed = isCollapsed;
            return true;
        }

        public static bool Remove(List<NamespaceCollapseState> states, string namespaceKey)
        {
            if (states == null || string.IsNullOrWhiteSpace(namespaceKey))
            {
                return false;
            }

            return states.RemoveAll(state =>
                    string.Equals(state?.namespaceKey, namespaceKey, StringComparison.Ordinal)
                ) > 0;
        }

        public NamespaceCollapseState Clone()
        {
            return new NamespaceCollapseState
            {
                namespaceKey = namespaceKey ?? string.Empty,
                isCollapsed = isCollapsed,
            };
        }
    }
}
