namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;

    [Serializable]
    public sealed class EditorAutomationRequest
    {
        public const int DefaultPageSize = 100;

        public int schemaVersion;
        public string operation;
        public string[] typeIdentities;
        public string[] assetGuids;
        public string processorIdentity;
        public string assetOperation;
        public string[] destinationPaths;
        public string settingsGuid;
        public string dataFolderPath;
        public string selectionMode;
        public string[] confirmPaths;
        public string text;
        public string[] andLabels;
        public string[] orLabels;
        public string labelCombination;
        public int offset;
        public int pageSize = DefaultPageSize;
    }
}
