namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using Data;
    using State;
    using UnityEngine;

    internal interface ILabelService
    {
        TypeLabelFilterConfig GetOrCreateConfig(Type type);

        void SaveConfig(TypeLabelFilterConfig config);

        void UpdateSessionState(
            Type type,
            TypeLabelFilterConfig config,
            VisualizerSessionState sessionState
        );

        LabelFilterResult ApplyFilter(
            Type type,
            IReadOnlyList<ScriptableObject> availableObjects,
            TypeLabelFilterConfig config
        );

        IReadOnlyCollection<string> GetAvailableLabels(Type type);

        void ClearFilters(Type type);

        LabelSuggestionProvider SuggestionProvider { get; }
    }
}
