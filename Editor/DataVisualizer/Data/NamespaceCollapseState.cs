﻿namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;

    [Serializable]
    public sealed class NamespaceCollapseState
    {
        public string namespaceKey = string.Empty;
        public bool isCollapsed;

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
