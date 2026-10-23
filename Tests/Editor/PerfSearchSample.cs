namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;

    [Serializable]
    internal sealed class PerfSearchSample
    {
        public int size;
        public string kind;
        public string mode;
        public string query;
        public string indexMode = "complete";
        public int availableRecordsAtInput;
        public bool indexCompleteAtInput;
        public bool complete;
        public bool indexComplete;
        public bool reachedLimit;
        public int generation;
        public int sliceOverruns;
        public double longestWindowQuerySliceMilliseconds;
        public int records;
        public int scanned;
        public int matches;
        public int visibleMatches;
        public int expectedMatches;
        public int minimumMatches;
        public double actionMilliseconds;
        public double firstResultMilliseconds = -1;
        public double completedMilliseconds = -1;
        public double publicationPaintMilliseconds = -1;
    }
}
