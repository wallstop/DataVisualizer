namespace WallstopStudios.DataVisualizer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;

    public interface IDataProcessor
    {
        string Name { get; }

        string Description { get; }

        IEnumerable<Type> Accepts { get; }

        int WillEffect(Type type, IEnumerable<ScriptableObject> objects)
        {
            return objects.Count();
        }

        void Process(Type type, IEnumerable<ScriptableObject> objects);
    }
}
