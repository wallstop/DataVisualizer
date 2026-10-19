namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using System.Collections.Generic;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;

    public sealed class LabelFilterEditor : VisualElement
    {
        public const string AllMode = "Match all labels";
        public const string AnyMode = "Match any label";
        public const string AdvancedMode = "Advanced";

        private readonly VisualElement _advanced;
        private readonly Action _changed;
        private readonly DropdownField _mode;
        private readonly Label _summary;
        private readonly VisualElement _pills;
        private readonly Button _clear;
        private TypeLabelFilterConfig _config;
        private IReadOnlyList<string> _available = Array.Empty<string>();
        private bool _advancedRequested;

        public LabelFilterEditor(VisualElement advanced, Action changed)
        {
            name = "label-filter-editor";
            _advanced = advanced;
            _changed = changed;
            VisualElement controls = new();
            controls.AddToClassList(StyleConstants.LabelFilterControlsClass);
            _mode = new DropdownField(new List<string> { AllMode, AnyMode, AdvancedMode }, 0) { name = "label-filter-mode" };
            _mode.RegisterValueChangedCallback(change => ChangeMode(change.newValue));
            controls.Add(_mode);
            _clear = new Button(ClearFilter)
            {
                name = "clear-label-filter",
                text = "Clear",
                tooltip = "Remove every label constraint",
            };
            controls.Add(_clear);
            Add(controls);
            _summary = new Label { name = "label-filter-summary", enableRichText = false };
            _summary.AddToClassList(StyleConstants.LabelFilterSummaryClass);
            Add(_summary);
            _pills = new VisualElement { name = "simple-label-pills" };
            _pills.AddToClassList(StyleConstants.LabelFilterControlsClass);
            Add(_pills);
            Add(_advanced);
        }

        public static string InferMode(TypeLabelFilterConfig config)
        {
            bool hasAll = config?.andLabels is { Count: > 0 };
            bool hasAny = config?.orLabels is { Count: > 0 };
            return hasAll && hasAny ? AdvancedMode
                : hasAny ? AnyMode
                : AllMode;
        }

        public void Bind(TypeLabelFilterConfig config, IReadOnlyList<string> available)
        {
            string inferred = InferMode(config);
            if (!ReferenceEquals(_config, config))
            {
                _advancedRequested = inferred == AdvancedMode;
                _mode.SetValueWithoutNotify(inferred);
            }
            else if (!_advancedRequested && (config?.andLabels is { Count: > 0 } || config?.orLabels is { Count: > 0 }))
            {
                _mode.SetValueWithoutNotify(inferred);
            }
            if (inferred == AdvancedMode)
            {
                _advancedRequested = true;
                _mode.SetValueWithoutNotify(AdvancedMode);
            }
            _config = config;
            _available = available ?? Array.Empty<string>();
            Render();
        }

        private void ChangeMode(string mode)
        {
            if (_config == null)
            {
                return;
            }
            bool requestedAdvanced = mode == AdvancedMode;
            if (!requestedAdvanced && InferMode(_config) == AdvancedMode)
            {
                _advancedRequested = true;
                _mode.SetValueWithoutNotify(AdvancedMode);
                _summary.text = "Clear the compound filter before choosing a simple mode. " + Expression();
                return;
            }
            _advancedRequested = requestedAdvanced;
            if (!_advancedRequested)
            {
                List<string> source = mode == AllMode ? _config.orLabels : _config.andLabels;
                List<string> target = mode == AllMode ? _config.andLabels : _config.orLabels;
                if (0 < source.Count)
                {
                    target.AddRange(source);
                    source.Clear();
                    _changed();
                }
            }
            Render();
        }

        private void ClearFilter()
        {
            if (_config == null)
            {
                return;
            }
            _config.andLabels.Clear();
            _config.orLabels.Clear();
            _advancedRequested = false;
            _mode.SetValueWithoutNotify(AllMode);
            _changed();
            Render();
        }

        private void ToggleLabel(string label)
        {
            bool restoreFocus = panel?.focusController?.focusedElement is Button focused && string.Equals(focused.userData as string, label, StringComparison.Ordinal);
            List<string> selected = _mode.value == AnyMode ? _config.orLabels : _config.andLabels;
            if (selected.RemoveAll(candidate => string.Equals(candidate, label, StringComparison.Ordinal)) == 0)
            {
                selected.Add(label);
            }
            _changed();
            Render();
            if (restoreFocus)
            {
                foreach (VisualElement element in _pills.Children())
                {
                    if (string.Equals(element.userData as string, label, StringComparison.Ordinal))
                    {
                        element.Focus();
                        break;
                    }
                }
            }
        }

        private string Expression()
        {
            string all = _config?.andLabels is { Count: > 0 } ? "All of [" + string.Join(", ", _config.andLabels) + "]" : null;
            string any = _config?.orLabels is { Count: > 0 } ? "Any of [" + string.Join(", ", _config.orLabels) + "]" : null;
            if (all != null && any != null)
            {
                return "(" + all + ") " + (_config.combinationType == LabelCombinationType.Or ? "OR" : "AND") + " (" + any + ")";
            }
            return all ?? any ?? "All assets (no label filter)";
        }

        private void Render()
        {
            bool advanced = _mode.value == AdvancedMode;
            _advanced.style.display = advanced ? DisplayStyle.Flex : DisplayStyle.None;
            _pills.style.display = advanced ? DisplayStyle.None : DisplayStyle.Flex;
            _mode.SetEnabled(_config != null);
            _clear.SetEnabled(_config != null && (0 < _config.andLabels.Count || 0 < _config.orLabels.Count));
            _summary.text = Expression();
            _pills.Clear();
            if (advanced || _config == null)
            {
                return;
            }
            List<string> labels = new(_available.Count);
            for (int index = 0; index < _available.Count; index++)
            {
                labels.Add(_available[index]);
            }
            List<string> selected = _mode.value == AnyMode ? _config.orLabels : _config.andLabels;
            foreach (string label in selected)
            {
                if (!labels.Contains(label))
                {
                    labels.Add(label);
                }
            }
            labels.Sort(StringComparer.Ordinal);
            foreach (string label in labels)
            {
                bool active = selected.Contains(label);
                Button pill = new(() => ToggleLabel(label))
                {
                    text = active ? label + " ×" : label + " +",
                    tooltip = (active ? "Remove " : "Add ") + label,
                    userData = label,
                };
                pill.EnableInClassList(StyleConstants.SelectedClass, active);
                _pills.Add(pill);
            }
        }
    }
}
