namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;

    internal sealed class DataAssetChangeEventArgs
    {
        public DataAssetChangeEventArgs(
            bool indexRebuilt,
            IReadOnlyCollection<string> updatedGuids,
            IReadOnlyCollection<string> removedGuids,
            Type refreshedType
        )
        {
            IndexRebuilt = indexRebuilt;
            UpdatedGuids = updatedGuids ?? Array.Empty<string>();
            RemovedGuids = removedGuids ?? Array.Empty<string>();
            RefreshedType = refreshedType;
        }

        public bool IndexRebuilt { get; }

        public IReadOnlyCollection<string> UpdatedGuids { get; }

        public IReadOnlyCollection<string> RemovedGuids { get; }

        public Type RefreshedType { get; }
    }
}
