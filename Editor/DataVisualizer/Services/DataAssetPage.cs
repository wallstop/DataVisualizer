namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;

    internal sealed class DataAssetPage
    {
        public DataAssetPage(
            Type assetType,
            IReadOnlyList<DataAssetMetadata> items,
            int totalCount,
            int offset
        )
        {
            AssetType = assetType;
            Items = items ?? Array.Empty<DataAssetMetadata>();
            TotalCount = totalCount < 0 ? 0 : totalCount;
            Offset = offset < 0 ? 0 : offset;
        }

        public Type AssetType { get; }

        public IReadOnlyList<DataAssetMetadata> Items { get; }

        public int TotalCount { get; }

        public int Offset { get; }

        public int Count
        {
            get
            {
                return Items.Count;
            }
        }
    }
}
