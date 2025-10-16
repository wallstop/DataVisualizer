namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using WallstopStudios.DataVisualizer;

    internal sealed class DataProcessorRegistry : IDataProcessorRegistry
    {
        private readonly List<IDataProcessor> _processors = new List<IDataProcessor>();
        private readonly Dictionary<Type, List<IDataProcessor>> _compatibilityCache =
            new Dictionary<Type, List<IDataProcessor>>();

        public event Action ProcessorsChanged;

        public void Refresh()
        {
            _processors.Clear();
            _compatibilityCache.Clear();

            foreach (Type type in TypeCache.GetTypesDerivedFrom<IDataProcessor>())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                try
                {
                    if (Activator.CreateInstance(type) is IDataProcessor processor)
                    {
                        _processors.Add(processor);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError(
                        $"Failed to create instance of IDataProcessor '{type.FullName}': {ex.Message}"
                    );
                }
            }

            _processors.Sort((lhs, rhs) => string.CompareOrdinal(lhs.Name, rhs.Name));

            ProcessorsChanged?.Invoke();
        }

        public IReadOnlyList<IDataProcessor> GetAllProcessors()
        {
            return _processors;
        }

        public IReadOnlyList<IDataProcessor> GetCompatibleProcessors(Type type)
        {
            if (type == null)
            {
                return Array.Empty<IDataProcessor>();
            }

            if (_compatibilityCache.TryGetValue(type, out List<IDataProcessor> cached))
            {
                return cached;
            }

            List<IDataProcessor> compatible = new List<IDataProcessor>();
            for (int index = 0; index < _processors.Count; index++)
            {
                IDataProcessor processor = _processors[index];
                IEnumerable<Type> accepts = processor.Accepts;
                if (accepts == null)
                {
                    continue;
                }

                foreach (Type acceptedType in accepts)
                {
                    if (acceptedType == type)
                    {
                        compatible.Add(processor);
                        break;
                    }
                }
            }

            _compatibilityCache[type] = compatible;
            return compatible;
        }
    }
}
