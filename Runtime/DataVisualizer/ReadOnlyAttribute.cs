namespace WallstopStudios.DataVisualizer
{
    using System;
    using UnityEngine;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    internal sealed class ReadOnlyAttribute : PropertyAttribute { }
}
