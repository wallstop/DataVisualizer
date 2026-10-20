namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;

    public sealed class EditorAssetRecord
    {
        public string Guid { get; }
        public string Path { get; }
        public string Name { get; }
        public string TypeIdentity { get; }
        public IReadOnlyList<string> Labels { get; }
        public string Error { get; }

        public EditorAssetRecord(string guid, string path, string name, string typeIdentity, string[] labels, string error = null)
        {
            Guid = guid;
            Path = path;
            Name = name;
            TypeIdentity = typeIdentity;
            Labels = Array.AsReadOnly(labels == null ? Array.Empty<string>() : (string[])labels.Clone());
            Error = error;
        }
    }
}
