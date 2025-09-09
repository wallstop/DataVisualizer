namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    [Serializable]
    public sealed class TypeObjectOrder
    {
        public int page;
        public string TypeFullName = string.Empty;
        public List<string> ObjectGuids = new();

        public TypeObjectOrder Clone()
        {
            return new TypeObjectOrder
            {
                page = page,
                TypeFullName = TypeFullName ?? string.Empty,
                ObjectGuids = ObjectGuids?.ToList() ?? new List<string>(),
            };
        }
    }
}
