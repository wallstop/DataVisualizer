namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;

    [Serializable]
    public sealed class PrefabFeasibilityReport
    {
        public string revision;
        public string measuredUtc;
        public string unity;
        public string os;
        public string cpu;
        public string graphics;
        public float dpi;
        public float pixelsPerPoint;
        public string fixtureRoot;
        public string mode;
        public int recordCount;
        public int missingScriptCount;
        public double firstDiscoveryMilliseconds;
        public List<double> warmMilliseconds = new();
        public List<double> selectedComponentMilliseconds = new();
        public List<double> isolatedContentsMilliseconds = new();
        public List<double> changedPrefabMilliseconds = new();
        public List<double> changedPrefabImportMilliseconds = new();
        public List<long> heapDeltaBytes = new();
        public int contentsScenesBefore;
        public int contentsScenesAfter;
        public int contentsGameObjectsBefore;
        public int contentsGameObjectsAfter;
        public int retainedObjectReferenceFields;
        public bool cleanupComplete;
    }
}
