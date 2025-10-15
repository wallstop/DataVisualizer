namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using Services;
    using UnityEngine;

    internal sealed class ObjectRowViewModel
    {
        public ScriptableObject DataObject { get; private set; }

        public DataAssetMetadata Metadata { get; private set; }

        public int DisplayIndex { get; private set; }

        public void Update(
            ScriptableObject dataObject,
            DataAssetMetadata metadata,
            int displayIndex
        )
        {
            DataObject = dataObject;
            Metadata = metadata;
            DisplayIndex = displayIndex;
        }
    }
}
