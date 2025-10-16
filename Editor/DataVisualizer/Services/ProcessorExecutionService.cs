namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer;

    internal sealed class ProcessorExecutionService
    {
        private readonly ScriptableAssetSaveScheduler _saveScheduler;

        public ProcessorExecutionService(ScriptableAssetSaveScheduler saveScheduler)
        {
            _saveScheduler = saveScheduler
                ?? throw new ArgumentNullException(nameof(saveScheduler));
        }

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
            _saveScheduler.ScheduleAssetDatabaseSave();
            _saveScheduler.Schedule(AssetDatabase.Refresh);
        }
    }
}
