namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;

    [Serializable]
    public sealed class EditorAutomationRequest
    {
        public int schemaVersion;
        public string operation;
        public string[] typeIdentities;
        public string[] assetGuids;
        public string text;
        public string[] andLabels;
        public string[] orLabels;
        public string labelCombination;
        public int offset;
        public int pageSize = 100;
    }
}
