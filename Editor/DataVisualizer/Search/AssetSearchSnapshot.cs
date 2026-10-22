namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System.Collections;
    using System.Collections.Generic;

    // The cache detaches its document list before any later write.
    internal sealed class AssetSearchSnapshot : IReadOnlyList<AssetSearchDocument>
    {
        public int Count => _documents.Count;
        public AssetSearchDocument this[int index] => _documents[index];

        private readonly IReadOnlyList<AssetSearchDocument> _documents;

        internal AssetSearchSnapshot(IReadOnlyList<AssetSearchDocument> documents)
        {
            _documents = documents;
        }

        public IEnumerator<AssetSearchDocument> GetEnumerator()
        {
            return _documents.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
