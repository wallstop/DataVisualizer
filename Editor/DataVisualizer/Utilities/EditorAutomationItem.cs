namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;

    [Serializable]
    public sealed class EditorAutomationItem
    {
        public string guid;
        public string path;
        public string name;
        public string typeIdentity;
        public string[] labels;
        public string code;
        public string propertyPath;
        public string detail;
        public string dataFolderPath;
        public string selectionMode;
    }
}
