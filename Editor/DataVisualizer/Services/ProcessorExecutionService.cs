namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer;

    internal sealed class ProcessorExecutionService
    {
        public void Execute(
            IDataProcessor processor,
            Type targetType,
            IReadOnlyList<ScriptableObject> objects
        )
        {
            if (processor == null)
            {
                throw new ArgumentNullException(nameof(processor));
            }

            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            if (objects == null)
            {
                throw new ArgumentNullException(nameof(objects));
            }

            processor.Process(targetType, objects);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
