namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;

    [Serializable]
    public sealed class SubassetMetadataRecord
    {
        public string guid;
        public long localId;
        public string name;
        public string typeIdentity;
        public string path;
        public bool isMainAsset;
    }
}
