namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;

    [Serializable]
    public sealed class TypeObjectOrder
    {
        public string TypeFullName = string.Empty;
        public List<string> ObjectGuids = new();

        public TypeObjectOrder Clone()
        {
            return new TypeObjectOrder
            {
                TypeFullName = TypeFullName,
                ObjectGuids = new List<string>(ObjectGuids),
            };
        }
    }
}
