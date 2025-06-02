namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine.Serialization;

    public enum LabelCombinationType
    {
        [Obsolete("Please use a valid value")]
        None = 0,
        And = 1,
        Or = 2,
    }

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

        public TypeLabelFilterConfig Clone()
        {
            return new TypeLabelFilterConfig
            {
                isCollapsed = isCollapsed,
                combinationType = combinationType,
                typeFullName = typeFullName ?? string.Empty,
                andLabels = andLabels?.ToList() ?? new List<string>(),
                orLabels = orLabels?.ToList() ?? new List<string>(),
            };
        }
    }
}
