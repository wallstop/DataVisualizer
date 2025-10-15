namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;
    using System.Collections.Generic;

    internal sealed class NamespaceRemovalConfirmedEvent
    {
        public NamespaceRemovalConfirmedEvent(string namespaceKey, IReadOnlyList<Type> types)
        {
            NamespaceKey = namespaceKey;
            Types = types;
        }

        public string NamespaceKey { get; }

        public IReadOnlyList<Type> Types { get; }
    }
}
