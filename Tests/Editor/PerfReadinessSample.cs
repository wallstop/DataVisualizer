namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;

    // Negative observations are unavailable, never zero-duration successes.
    [Serializable]
    internal sealed class PerfReadinessSample
    {
        public int size;
        public string mode;
        public bool complete;
        public double firstRootPaintMilliseconds = -1;
        public double firstUsableRowsMilliseconds = -1;
        public double inspectorReadyMilliseconds = -1;
        public double openToIndexMilliseconds = -1;
        public double legacyIndexReadyMilliseconds = -1;
        public int realizedRows;
        public int loadedRows;
        public int retainedSearchObjects;
        public long managedBytes;
        public long unityAllocatedBytes;
        public long unityReservedBytes;
        public double longestIndexSliceMilliseconds;
        public int indexSliceOverruns;
        public double observerMilliseconds;
        public int observations;
    }
}
