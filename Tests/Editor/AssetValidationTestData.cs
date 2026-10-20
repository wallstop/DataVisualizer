namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using UnityEngine;

    public sealed class AssetValidationTestData : BaseDataObject
    {
        public Object reference;
        public ExposedReference<Object> exposedReference;

        [SerializeReference]
        public AssetValidationTestNode node;
    }
}
