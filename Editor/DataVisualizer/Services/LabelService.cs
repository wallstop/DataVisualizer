namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Data;
    using Helper.Pooling;
    using State;
    using UnityEditor;
    using UnityEngine;

    internal sealed class LabelService : ILabelService
    {
        private readonly DataVisualizer _dataVisualizer;
        private readonly IDataAssetService _assetService;
        private readonly VisualizerSessionState _sessionState;
        private readonly LabelSuggestionProvider _suggestionProvider;

        public LabelService(
            DataVisualizer dataVisualizer,
            IDataAssetService assetService,
            VisualizerSessionState sessionState
        )
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
            _suggestionProvider = new LabelSuggestionProvider(_assetService);
        }

        public LabelSuggestionProvider SuggestionProvider => _suggestionProvider;

        public TypeLabelFilterConfig GetOrCreateConfig(Type type)
        {
            return _dataVisualizer.LoadOrCreateLabelFilterConfig(type);
        }

        public void SaveConfig(TypeLabelFilterConfig config)
        {
            _dataVisualizer.SaveLabelFilterConfig(config);
        }

        public void UpdateSessionState(
            Type type,
            TypeLabelFilterConfig config,
            VisualizerSessionState sessionState
        )
        {
            if (sessionState == null || config == null)
            {
                return;
            }

            VisualizerSessionState.LabelFilterState state = sessionState.Labels;
            state.SetAndLabels(config.andLabels);
            state.SetOrLabels(config.orLabels);
            state.SetCombinationType(config.combinationType);
        }

        public LabelFilterResult ApplyFilter(
            Type type,
            IReadOnlyList<ScriptableObject> availableObjects,
            TypeLabelFilterConfig config
        )
        {
            List<ScriptableObject> filteredObjects = new List<ScriptableObject>();
            List<DataAssetMetadata> filteredMetadata = new List<DataAssetMetadata>();
            List<string> uniqueLabels = new List<string>();

            if (type == null || config == null || availableObjects == null)
            {
                return new LabelFilterResult(
                    filteredObjects,
                    filteredMetadata,
                    uniqueLabels,
                    0,
                    0,
                    "Select a type to see objects."
                );
            }

            IReadOnlyList<string> andLabels = config.andLabels ?? new List<string>();
            IReadOnlyList<string> orLabels = config.orLabels ?? new List<string>();
            bool noAndFilter = andLabels.Count == 0;
            bool noOrFilter = orLabels.Count == 0;

            int totalCount = 0;
            int matchedCount = 0;

            using PooledResource<HashSet<string>> uniqueLabelsLease = Buffers<string>.HashSet.Get(
                out HashSet<string> uniqueLabelSet
            );
            using PooledResource<HashSet<string>> labelSetLease = Buffers<string>.HashSet.Get(
                out HashSet<string> labelSet
            );

            for (int index = 0; index < availableObjects.Count; index++)
            {
                ScriptableObject dataObject = availableObjects[index];
                if (dataObject == null)
                {
                    continue;
                }

                totalCount++;
                labelSet.Clear();
                string[] labels = AssetDatabase.GetLabels(dataObject) ?? Array.Empty<string>();
                for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
                {
                    string label = labels[labelIndex];
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        continue;
                    }

                    string normalized = label.Trim();
                    labelSet.Add(normalized);
                    uniqueLabelSet.Add(normalized);
                }

                bool matches = EvaluateMatch(
                    labelSet,
                    andLabels,
                    orLabels,
                    config.combinationType,
                    noAndFilter,
                    noOrFilter
                );

                if (!matches)
                {
                    continue;
                }

                matchedCount++;
                filteredObjects.Add(dataObject);
                string guid = DataVisualizer.GetAssetGuid(dataObject);
                if (
                    !string.IsNullOrWhiteSpace(guid)
                    && _assetService.TryGetAssetByGuid(guid, out DataAssetMetadata metadata)
                )
                {
                    filteredMetadata.Add(metadata);
                }
            }

            uniqueLabels = uniqueLabelSet
                .Select(label => label.Trim())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string statusMessage = BuildStatusMessage(totalCount, matchedCount);
            return new LabelFilterResult(
                filteredObjects,
                filteredMetadata,
                uniqueLabels,
                totalCount,
                matchedCount,
                statusMessage
            );
        }

        public IReadOnlyCollection<string> GetAvailableLabels(Type type)
        {
            if (type == null)
            {
                return Array.Empty<string>();
            }

            return _assetService.EnumerateLabels(type);
        }

        private static bool EvaluateMatch(
            HashSet<string> labelSet,
            IReadOnlyList<string> andLabels,
            IReadOnlyList<string> orLabels,
            LabelCombinationType combinationType,
            bool noAndFilter,
            bool noOrFilter
        )
        {
            bool matchesAnd = noAndFilter || andLabels.All(label => labelSet.Contains(label));
            bool matchesOr = noOrFilter || orLabels.Any(label => labelSet.Contains(label));

            return combinationType switch
            {
                LabelCombinationType.Or => matchesAnd || matchesOr,
                _ => matchesAnd && matchesOr,
            };
        }

        private static string BuildStatusMessage(int totalCount, int matchedCount)
        {
            if (totalCount == 0)
            {
                return "No objects found for the selected type.";
            }

            int hiddenCount = Math.Max(0, totalCount - matchedCount);
            if (hiddenCount == 0)
            {
                return string.Empty;
            }

            string color = hiddenCount < 20 && hiddenCount != totalCount ? "yellow" : "red";
            return $"<b><color={color}>{hiddenCount}</color></b> objects hidden by label filter.";
        }
    }
}
