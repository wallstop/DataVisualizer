namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;

    [Serializable]
    public sealed class SubassetFeasibilityReport
    {
        public string measuredUtc;
        public string revision;
        public string unity;
        public string os;
        public string cpu;
        public string graphics;
        public string fixtureRoot;
        public int memoryMegabytes;
        public float dpi;
        public float pixelsPerPoint;
        public bool darkTheme;
        public string storage = "Windows host project; storage device and filesystem cache state unmeasured";
        public int backgroundAssetCount;
        public int parentCount;
        public int scriptableObjectCount;
        public int warmups = 5;
        public string mode = "Warm-session real assets; first pass is not a fresh-process cold sample";
        public string limitations =
            "Synthetic fixture contents; no scale guarantee, cache or production browsing support. Unity may retain loaded native assets after local references are released.";
        public int baselineMainAssetCount;
        public double fixtureCreateMilliseconds;
        public double baselineMainReadMilliseconds;
        public double firstReadMilliseconds;
        public List<long> exampleLocalIds = new();
        public List<double> warmMilliseconds = new();
        public bool allocationCounterSupported;
        public List<long> warmAllocatedBytes = new();
        public List<long> warmManagedHeapDeltaBytes = new();
        public int metadataObjectReferenceFields;
        public bool cleanupComplete;
    }
}
