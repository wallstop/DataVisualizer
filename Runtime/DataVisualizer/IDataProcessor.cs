namespace WallstopStudios.DataVisualizer
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;

    public interface IDataProcessor
    {
        string Name { get; }

        string Description { get; }

        IEnumerable<Type> Accepts { get; }

        int WillEffect(Type type, IEnumerable<ScriptableObject> objects)
        {
            if (objects == null)
            {
                throw new ArgumentNullException(nameof(objects));
            }

            if (objects is ICollection<ScriptableObject> genericCollection)
            {
                return genericCollection.Count;
            }

            if (objects is ICollection collection)
            {
                return collection.Count;
            }

            int count = 0;
            checked
            {
                foreach (ScriptableObject _ in objects)
                {
                    count++;
                }
            }

            return count;
        }

        void Process(Type type, IEnumerable<ScriptableObject> objects);
    }
}
