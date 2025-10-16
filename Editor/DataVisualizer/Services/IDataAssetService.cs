namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;

    internal interface IDataAssetService
    {
        event Action<DataAssetChangeEventArgs> AssetsChanged;

        void ConfigureTrackedTypes(IEnumerable<Type> types);

        void MarkDirty();

        void ForceRebuild();

        int GetAssetCount(Type type);

        DataAssetPage GetAssetsPage(Type type, int offset, int count);

        IReadOnlyList<DataAssetMetadata> GetAssetsForType(Type type);

        IReadOnlyList<string> GetGuidsForType(Type type);

        IEnumerable<DataAssetMetadata> GetAllAssets();

        bool TryGetAssetByGuid(string guid, out DataAssetMetadata metadata);

        bool TryGetAssetByPath(string path, out DataAssetMetadata metadata);

        void RefreshAsset(string guid);

        void RefreshType(Type type);

        void RemoveAsset(string guid);

        IReadOnlyCollection<string> EnumerateLabels(Type type);
    }
}
