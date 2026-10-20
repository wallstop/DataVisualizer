namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;

    [Serializable]
    public sealed class EditorAutomationResponse
    {
        public int schemaVersion = 1;
        public string status;
        public string code;
        public string detail;
        public bool complete;
        public int processedCount;
        public int scopeCount;
        public int availableCount;
        public int offset;
        public string coverage = "Unity main assets; Unity serialized object references and BaseDataObject.Id only. Custom serialization is not checked.";
        public EditorAutomationItem[] items;
    }
}
