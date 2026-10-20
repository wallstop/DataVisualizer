namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using UnityEngine;

    public sealed class AssetValidationTestData : BaseDataObject
    {
        public Object reference;

        [SerializeReference]
        public AssetValidationTestNode node;
    }
}
