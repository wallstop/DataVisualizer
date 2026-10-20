namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using UnityEngine;

    [Serializable]
    public sealed class AssetValidationTestNode
    {
        [SerializeReference]
        public AssetValidationTestNode next;

        public UnityEngine.Object reference;
    }
}
