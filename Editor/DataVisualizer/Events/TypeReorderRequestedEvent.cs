namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;

    internal sealed class TypeReorderRequestedEvent
    {
        public TypeReorderRequestedEvent(string namespaceKey, Type type, int targetIndex)
        {
            NamespaceKey = namespaceKey;
            Type = type;
            TargetIndex = targetIndex;
        }

        public string NamespaceKey { get; }

        public Type Type { get; }

        public int TargetIndex { get; }
    }
}
