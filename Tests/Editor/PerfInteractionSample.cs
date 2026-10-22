namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;

    [Serializable]
    internal sealed class PerfInteractionSample
    {
        public int size;
        public string kind;
        public string mode;
        public bool complete;
        public double actionMilliseconds;
        public double firstRootPaintMilliseconds = -1;
        public double observedReadyMilliseconds = -1;
        public double observerMilliseconds;
        public int realizedRows;
        public int loadedRows;
        public int retainedSearchObjects;
        public int expectedRows = -1;
        public string selectedType;
        public bool viewportMoved;
    }
}
