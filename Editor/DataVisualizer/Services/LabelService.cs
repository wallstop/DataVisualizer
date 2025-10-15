namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using Data;
    using UnityEngine;

    internal sealed class LabelService
    {
        private readonly DataVisualizer _dataVisualizer;

        public LabelService(DataVisualizer dataVisualizer)
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
        }

        public void UpdateLabelAreaAndFilter()
        {
            _dataVisualizer.UpdateLabelAreaAndFilter();
        }

        public void SaveLabelFilterConfig(TypeLabelFilterConfig config)
        {
            _dataVisualizer.SaveLabelFilterConfig(config);
        }

        public TypeLabelFilterConfig GetCurrentConfig()
        {
            return _dataVisualizer.CurrentTypeLabelFilterConfig;
        }
    }
}
