namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;

    [Serializable]
    internal sealed class PerfSearchSample
    {
        private int PendingSnapshotGeneration { get; set; } = -1;
        private int PendingSnapshotRecords { get; set; }
        private int PendingSnapshotMatches { get; set; }
        private bool PendingSnapshotIndexComplete { get; set; }

        public int size;
        public string kind;
        public string mode;
        public string query;
        public string targetGuid;
        public int invalidatedPublications;
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
        public int resultObjectLoads = -1;
        public int previewAssetReferences = -1;
        public int expectedMatches;
        public int minimumMatches;
        public double firstSnapshotMilliseconds = -1;
        public int firstSnapshotGeneration = -1;
        public int firstSnapshotRecords = -1;
        public int firstSnapshotMatches = -1;
        public bool firstSnapshotIndexComplete;
        public bool firstSnapshotCurrent;
        public double firstSnapshotPaintMilliseconds = -1;
        public int firstSnapshotPaintGeneration = -1;
        public int firstSnapshotPaintRecords = -1;
        public int firstSnapshotPaintMatches = -1;
        public bool firstSnapshotPaintIndexComplete;
        public double actionMilliseconds;
        public double firstResultMilliseconds = -1;
        public double completedMilliseconds = -1;
        public double publicationPaintMilliseconds = -1;

        public void ObserveSnapshot(double elapsed, int publishedGeneration, int recordCount, int matchCount, bool fullIndex, bool current)
        {
            if (complete)
            {
                return;
            }
            if (firstSnapshotMilliseconds < 0)
            {
                firstSnapshotMilliseconds = elapsed;
                firstSnapshotGeneration = publishedGeneration;
                firstSnapshotRecords = recordCount;
                firstSnapshotMatches = matchCount;
                firstSnapshotIndexComplete = fullIndex;
                firstSnapshotCurrent = current;
            }
            PendingSnapshotGeneration = publishedGeneration;
            PendingSnapshotRecords = recordCount;
            PendingSnapshotMatches = matchCount;
            PendingSnapshotIndexComplete = fullIndex;
        }

        public bool ObserveSnapshotPaint(double elapsed, int displayedGeneration)
        {
            if (complete || 0 <= firstSnapshotPaintMilliseconds || PendingSnapshotGeneration < 0 || displayedGeneration != PendingSnapshotGeneration)
            {
                return false;
            }
            firstSnapshotPaintMilliseconds = elapsed;
            firstSnapshotPaintGeneration = PendingSnapshotGeneration;
            firstSnapshotPaintRecords = PendingSnapshotRecords;
            firstSnapshotPaintMatches = PendingSnapshotMatches;
            firstSnapshotPaintIndexComplete = PendingSnapshotIndexComplete;
            return true;
        }
    }
}
