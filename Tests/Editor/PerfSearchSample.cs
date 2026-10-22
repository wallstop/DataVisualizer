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
        public bool complete;
        public bool indexComplete;
        public int generation;
        public int records;
        public int scanned;
        public int matches;
        public int expectedMatches;
        public double actionMilliseconds;
        public double firstResultMilliseconds = -1;
        public double completedMilliseconds = -1;
    }
}
