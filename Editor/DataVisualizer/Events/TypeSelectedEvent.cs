namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;

    internal sealed class TypeSelectedEvent
    {
        public TypeSelectedEvent(string namespaceKey, Type selectedType)
        {
            NamespaceKey = namespaceKey;
            SelectedType = selectedType;
        }

        public string NamespaceKey { get; }

        public Type SelectedType { get; }
    }
}
