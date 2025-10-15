namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
    using Data;
    using Services;
    using State;
    using Styles;
    using UI;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class LabelPanelController
    {
        private readonly DataVisualizer _dataVisualizer;
        private readonly LabelService _labelService;
        private readonly VisualizerSessionState _sessionState;

        public LabelPanelController(
            DataVisualizer dataVisualizer,
            LabelService labelService,
            VisualizerSessionState sessionState
        )
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _labelService = labelService ?? throw new ArgumentNullException(nameof(labelService));
            _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        }

        public void BuildLabelPanel(VisualElement objectColumn)
        {
            if (objectColumn == null)
            {
                return;
            }

            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;

            _dataVisualizer._labelCollapseRow = new VisualElement();
            _dataVisualizer._labelCollapseRow.AddToClassList("collapse-row");
            _dataVisualizer._labelCollapseToggle = new Label();
            _dataVisualizer._labelCollapseToggle.AddToClassList(StyleConstants.ClickableClass);
            _dataVisualizer._labelCollapseToggle.AddToClassList("collapse-toggle");
            _dataVisualizer._labelCollapseToggle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                TypeLabelFilterConfig currentConfig = _dataVisualizer.CurrentTypeLabelFilterConfig;
                if (currentConfig == null)
                {
                    return;
                }

                ToggleLabelsCollapsed(!currentConfig.isCollapsed);
                evt.StopPropagation();
            });
            _dataVisualizer._labelCollapseRow.Add(_dataVisualizer._labelCollapseToggle);

            _dataVisualizer._labels = new Label("Labels");
            _dataVisualizer._labels.AddToClassList("labels-label");
            _dataVisualizer._labelCollapseRow.Add(_dataVisualizer._labels);
            objectColumn.Add(_dataVisualizer._labelCollapseRow);

            _dataVisualizer._labelFilterSelectionRoot = new VisualElement
            {
                name = "label-filter-section-root",
            };
            _dataVisualizer._labelFilterSelectionRoot.AddToClassList("label-filter-section-root");

            VisualElement labelContainerContainer = new VisualElement
            {
                name = "label-container-container",
            };
            labelContainerContainer.AddToClassList("label-container-container");
            objectColumn.Add(labelContainerContainer);
            labelContainerContainer.Add(_dataVisualizer._labelFilterSelectionRoot);

            BuildLabelRows(config);
            UpdateLabelsCollapsedState();
            UpdateAdvancedCollapsedState();
        }

        public void ToggleLabelsCollapsed(bool isCollapsed)
        {
            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
            if (
                config != null
                && config.isCollapsed != isCollapsed
                && (isCollapsed || CanCollapseLabels())
            )
            {
                config.isCollapsed = isCollapsed;
                _dataVisualizer.SaveLabelFilterConfig(config);
            }

            UpdateLabelsCollapsedState();
        }

        public void ToggleLabelsAdvancedCollapsed(bool isCollapsed)
        {
            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
            if (
                config != null
                && config.isAdvancedCollapsed != isCollapsed
                && (isCollapsed || CanCollapseAdvancedConfiguration())
            )
            {
                config.isAdvancedCollapsed = isCollapsed;
                _dataVisualizer.SaveLabelFilterConfig(config);
            }

            UpdateAdvancedCollapsedState();
        }

        public bool CanCollapseLabels()
        {
            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
            if (config == null)
            {
                return true;
            }

            return config.andLabels.Count == 0 && config.orLabels.Count == 0;
        }

        public bool CanCollapseAdvancedConfiguration()
        {
            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
            if (config == null)
            {
                return true;
            }

            return _dataVisualizer._andOrToggle.IsLeftSelected && config.orLabels.Count == 0;
        }

        public void UpdateLabelsCollapsedState()
        {
            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;

            UpdateLabelsCollapsedClickableState();

            if (_dataVisualizer._labels != null)
            {
                _dataVisualizer._labels.text =
                    config != null && config.isCollapsed
                        ? "Labels ("
                            + "<b><color=yellow>"
                            + _dataVisualizer._currentUniqueLabelsForType.Count
                            + "</color></b>)"
                        : "Labels";
            }

            if (_dataVisualizer._labelFilterSelectionRoot != null)
            {
                _dataVisualizer._labelFilterSelectionRoot.style.display =
                    config == null || config.isCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public void UpdateAdvancedCollapsedState()
        {
            UpdateAdvancedClickableState();
            if (_dataVisualizer._logicalGrouping != null)
            {
                TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
                _dataVisualizer._logicalGrouping.style.display =
                    config != null && !config.isAdvancedCollapsed
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
            }
        }

        public void UpdateLabelsCollapsedClickableState()
        {
            if (_dataVisualizer._labelCollapseToggle == null)
            {
                return;
            }

            _dataVisualizer._labelCollapseToggle.EnableInClassList(
                StyleConstants.ClickableClass,
                CanCollapseLabels()
            );

            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
            bool isCollapsed = config == null || config.isCollapsed;
            _dataVisualizer._labelCollapseToggle.text = isCollapsed
                ? StyleConstants.ArrowCollapsed
                : StyleConstants.ArrowExpanded;
            _dataVisualizer._labelCollapseToggle.tooltip =
                isCollapsed ? "Explore label filtering logic"
                : CanCollapseAdvancedConfiguration() ? "Hide label filtering logic"
                : "Can not un-collapse due to populated label configuration";
        }

        public void UpdateAdvancedClickableState()
        {
            if (_dataVisualizer._labelAdvancedCollapseToggle == null)
            {
                return;
            }

            _dataVisualizer._labelAdvancedCollapseToggle.EnableInClassList(
                StyleConstants.ClickableClass,
                CanCollapseAdvancedConfiguration()
            );

            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
            bool isCollapsed = config == null || config.isAdvancedCollapsed;
            _dataVisualizer._labelAdvancedCollapseToggle.text = isCollapsed
                ? StyleConstants.ArrowCollapsed
                : StyleConstants.ArrowExpanded;
            _dataVisualizer._labelAdvancedCollapseToggle.tooltip =
                isCollapsed ? "Explore advanced boolean label logic"
                : CanCollapseAdvancedConfiguration() ? "Hide advanced boolean logic"
                : "Can not un-collapse due to either OR toggle or OR labels";
        }

        public List<string> GetCurrentlyAvailableLabels()
        {
            List<string> available = new List<string>();
            List<string> unique = _dataVisualizer._currentUniqueLabelsForType;
            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;

            if (config == null)
            {
                available.AddRange(unique);
                return available;
            }

            foreach (string label in unique)
            {
                bool inAnd = config.andLabels.Contains(label);
                bool inOr = config.orLabels.Contains(label);
                if (!(inAnd && inOr))
                {
                    available.Add(label);
                }
            }

            return available;
        }

        public void PopulateLabelPillContainers()
        {
            List<string> availableLabels = GetCurrentlyAvailableLabels();

            PopulateSingleLabelContainer(
                _dataVisualizer._availableLabelsContainer,
                availableLabels,
                DataVisualizer.LabelFilterSection.Available
            );

            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
            if (config != null)
            {
                PopulateSingleLabelContainer(
                    _dataVisualizer._andLabelsContainer,
                    config.andLabels,
                    DataVisualizer.LabelFilterSection.AND
                );
                PopulateSingleLabelContainer(
                    _dataVisualizer._orLabelsContainer,
                    config.orLabels,
                    DataVisualizer.LabelFilterSection.OR
                );
            }

            if (
                _dataVisualizer._labelFilterSelectionRoot != null
                && _dataVisualizer._labelFilterSelectionRoot.parent != null
            )
            {
                _dataVisualizer._labelFilterSelectionRoot.parent.style.display =
                    availableLabels.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_dataVisualizer._labelCollapseRow != null)
            {
                _dataVisualizer._labelCollapseRow.style.display =
                    availableLabels.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void PopulateSingleLabelContainer(
            VisualElement container,
            IList<string> labels,
            DataVisualizer.LabelFilterSection section
        )
        {
            if (container == null)
            {
                return;
            }

            container.Clear();
            if (labels == null)
            {
                return;
            }

            List<string> ordered = new List<string>(labels);
            ordered.Sort(StringComparer.Ordinal);

            foreach (string label in ordered)
            {
                container.Add(CreateLabelPill(label, section));
            }
        }

        public VisualElement CreateLabelPill(
            string labelText,
            DataVisualizer.LabelFilterSection currentSection
        )
        {
            Color labelColor = _dataVisualizer.GetColorForLabel(labelText);
            VisualElement pillContainer = new VisualElement
            {
                name = "label-pill-container-" + labelText.Replace(" ", "-").ToLowerInvariant(),
                style = { backgroundColor = labelColor },
                userData = labelText,
            };
            pillContainer.AddToClassList("label-pill");

            Label labelElement = new Label(labelText)
            {
                style =
                {
                    color = _dataVisualizer.IsColorDark(labelColor) ? Color.white : Color.black,
                    marginRight = currentSection == DataVisualizer.LabelFilterSection.AND
                        || currentSection == DataVisualizer.LabelFilterSection.OR
                        ? 2
                        : 0,
                },
            };
            labelElement.AddToClassList("label-pill-text");
            labelElement.pickingMode = PickingMode.Ignore;
            pillContainer.Add(labelElement);

            pillContainer.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                _dataVisualizer._draggedLabelText = labelText;
                _dataVisualizer._dragSourceSection = currentSection;

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData("DraggedLabelText", labelText);
                DragAndDrop.SetGenericData("SourceSection", currentSection.ToString());
                DragAndDrop.StartDrag(labelText);
                evt.StopPropagation();
            });

            switch (currentSection)
            {
                case DataVisualizer.LabelFilterSection.Available:
                    pillContainer.tooltip =
                        "Drag '" + labelText + "' to an AND/OR filter section.";
                    break;
                case DataVisualizer.LabelFilterSection.AND:
                    pillContainer.tooltip =
                        "Drag '" + labelText + "' back to Available or OR.";
                    break;
                case DataVisualizer.LabelFilterSection.OR:
                    pillContainer.tooltip =
                        "Drag '" + labelText + "' back to Available or AND.";
                    break;
            }

            return pillContainer;
        }

        private void BuildLabelRows(TypeLabelFilterConfig config)
        {
            _dataVisualizer._availableLabelsContainer = new VisualElement
            {
                name = "available-labels-container",
            };
            _dataVisualizer._availableLabelsContainer.AddToClassList("label-pill-container");
            VisualElement availableRow = new VisualElement { name = "available-row" };
            availableRow.AddToClassList("label-row-container");
            availableRow.AddToClassList("label-row-container--available");
            availableRow.Add(_dataVisualizer._availableLabelsContainer);
            _dataVisualizer._labelFilterSelectionRoot.Add(availableRow);

            VisualElement andRow = new VisualElement { name = "and-filter-row" };
            andRow.AddToClassList("label-row-container");
            Label andLabel = new Label("AND:")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginRight = 5,
                    minWidth = 60,
                },
            };
            andLabel.AddToClassList("label-header");
            andRow.Add(andLabel);
            _dataVisualizer._andLabelsContainer = new VisualElement
            {
                name = "and-labels-container",
            };
            _dataVisualizer._andLabelsContainer.AddToClassList("label-pill-container");
            andRow.Add(_dataVisualizer._andLabelsContainer);

            VisualElement advancedRow = new VisualElement();
            advancedRow.AddToClassList("advanced-row");

            _dataVisualizer._labelAdvancedCollapseToggle = new Label();
            _dataVisualizer._labelAdvancedCollapseToggle.AddToClassList(
                StyleConstants.ClickableClass
            );
            _dataVisualizer._labelAdvancedCollapseToggle.AddToClassList("collapse-toggle");
            _dataVisualizer._labelAdvancedCollapseToggle.AddToClassList("advanced");
            _dataVisualizer._labelAdvancedCollapseToggle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                TypeLabelFilterConfig currentConfig = _dataVisualizer.CurrentTypeLabelFilterConfig;
                if (currentConfig == null)
                {
                    return;
                }

                ToggleLabelsAdvancedCollapsed(!currentConfig.isAdvancedCollapsed);
                evt.StopPropagation();
            });
            advancedRow.Add(_dataVisualizer._labelAdvancedCollapseToggle);

            Label advanced = new Label("Advanced");
            advanced.AddToClassList("advanced-label");
            advancedRow.Add(advanced);
            _dataVisualizer._labelFilterSelectionRoot.Add(advancedRow);
            _dataVisualizer._labelFilterSelectionRoot.Add(andRow);

            _dataVisualizer._andOrToggle = new HorizontalToggle()
            {
                name = "and-or-toggle",
                LeftText = "AND &&",
                RightText = "OR ||",
            };
            _dataVisualizer._andOrToggle.AddToClassList("label");
            _dataVisualizer._andOrToggle.OnLeftSelected += HandleAndSelected;
            _dataVisualizer._andOrToggle.OnRightSelected += HandleOrSelected;
            switch (config != null ? config.combinationType : LabelCombinationType.And)
            {
                case LabelCombinationType.And:
                    _dataVisualizer._andOrToggle.SelectLeft(true);
                    break;
                case LabelCombinationType.Or:
                    _dataVisualizer._andOrToggle.SelectRight(true);
                    break;
            }

            _dataVisualizer._logicalGrouping = new VisualElement
            {
                name = "label-logical-grouping",
            };
            _dataVisualizer._logicalGrouping.AddToClassList("label-logical-grouping");
            _dataVisualizer._labelFilterSelectionRoot.Add(_dataVisualizer._logicalGrouping);
            _dataVisualizer._logicalGrouping.Add(_dataVisualizer._andOrToggle);

            VisualElement orRow = new VisualElement { name = "or-filter-row" };
            orRow.AddToClassList("label-row-container");
            Label orLabel = new Label("OR:")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginRight = 5,
                    minWidth = 60,
                },
            };
            orLabel.AddToClassList("label-header");
            orRow.Add(orLabel);
            _dataVisualizer._orLabelsContainer = new VisualElement { name = "or-labels-container" };
            _dataVisualizer._orLabelsContainer.AddToClassList("label-pill-container");
            orRow.Add(_dataVisualizer._orLabelsContainer);
            _dataVisualizer._logicalGrouping.Add(orRow);

            _dataVisualizer._filterStatusLabel = new Label(string.Empty)
            {
                name = "filter-status-label",
                style =
                {
                    color = Color.gray,
                    alignSelf = Align.Center,
                    marginTop = 3,
                    minHeight = 12,
                },
            };
            _dataVisualizer._labelFilterSelectionRoot.Add(_dataVisualizer._filterStatusLabel);

            _dataVisualizer.SetupDropTarget(
                _dataVisualizer._availableLabelsContainer,
                DataVisualizer.LabelFilterSection.Available
            );
            _dataVisualizer.SetupDropTarget(
                _dataVisualizer._andLabelsContainer,
                DataVisualizer.LabelFilterSection.AND
            );
            _dataVisualizer.SetupDropTarget(
                _dataVisualizer._orLabelsContainer,
                DataVisualizer.LabelFilterSection.OR
            );
        }

        private void HandleAndSelected()
        {
            _dataVisualizer._andOrToggle.Indicator.style.backgroundColor = new Color(
                0f,
                0.392f,
                0f
            );
            _dataVisualizer._andOrToggle.LeftLabel.EnableInClassList(
                StyleConstants.ClickableClass,
                false
            );
            _dataVisualizer._andOrToggle.RightLabel.EnableInClassList(
                StyleConstants.ClickableClass,
                true
            );
            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
            if (config != null && config.combinationType != LabelCombinationType.And)
            {
                config.combinationType = LabelCombinationType.And;
                _dataVisualizer.SaveLabelFilterConfig(config);
                _labelService.UpdateLabelAreaAndFilter();
            }
        }

        private void HandleOrSelected()
        {
            _dataVisualizer._andOrToggle.Indicator.style.backgroundColor = new Color(
                1f,
                0.5f,
                0.3137254902f
            );
            _dataVisualizer._andOrToggle.LeftLabel.EnableInClassList(
                StyleConstants.ClickableClass,
                true
            );
            _dataVisualizer._andOrToggle.RightLabel.EnableInClassList(
                StyleConstants.ClickableClass,
                false
            );
            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
            if (config != null && config.combinationType != LabelCombinationType.Or)
            {
                config.combinationType = LabelCombinationType.Or;
                _dataVisualizer.SaveLabelFilterConfig(config);
                _labelService.UpdateLabelAreaAndFilter();
            }
        }
    }
}
