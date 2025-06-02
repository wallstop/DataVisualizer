namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    [Serializable]
    public sealed class TypeLabelFilterConfig
    {
        public string TypeFullName = string.Empty;
        public List<string> AndLabels = new();
        public List<string> OrLabels = new();

        public TypeLabelFilterConfig Clone()
        {
            return new TypeLabelFilterConfig
            {
                TypeFullName = TypeFullName ?? string.Empty,
                AndLabels = AndLabels?.ToList() ?? new List<string>(),
                OrLabels = OrLabels?.ToList() ?? new List<string>(),
            };
        }
    }
}
