namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;
    using UnityEngine.Serialization;

    [Serializable]
    public sealed class TypeLabelFilterConfig
    {
        [FormerlySerializedAs("TypeFullName")]
        public string typeFullName = string.Empty;

        [FormerlySerializedAs("AndLabels")]
        public List<string> andLabels = new();

        [FormerlySerializedAs("OrLabels")]
        public List<string> orLabels = new();

        [FormerlySerializedAs("CombinationType")]
        public LabelCombinationType combinationType = LabelCombinationType.And;

        public bool isCollapsed;

        public bool isAdvancedCollapsed = true;

        public TypeLabelFilterConfig Clone()
        {
            return new TypeLabelFilterConfig
            {
                isCollapsed = isCollapsed,
                isAdvancedCollapsed = isAdvancedCollapsed,
#pragma warning disable CS0618 // Type or member is obsolete
                combinationType =
                    combinationType == LabelCombinationType.None
                        ? LabelCombinationType.And
                        : combinationType,
#pragma warning restore CS0618 // Type or member is obsolete
                typeFullName = typeFullName ?? string.Empty,
                andLabels = PersistedStateCopy.CloneStrings(andLabels),
                orLabels = PersistedStateCopy.CloneStrings(orLabels),
            };
        }
    }
}
