namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
    using Data;
    using Events;
    using Services;
    using State;
    using Styles;
    using UI;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class ProcessorPanelController : IDisposable
    {
        private readonly DataVisualizer _dataVisualizer;
        private readonly VisualizerSessionState _sessionState;
        private readonly IDataProcessorRegistry _registry;
        private readonly List<IDataProcessor> _compatibleProcessors = new();
        private readonly VisualElement _root;
        private readonly VisualElement _header;
        private readonly Label _headerLabel;
        private readonly Label _collapseToggle;
        private readonly VisualElement _content;
        private readonly HorizontalToggle _logicToggle;
        private readonly Label _statusLabel;
        private readonly ScrollView _scrollView;
        private readonly VisualElement _listContainer;
        private DataVisualizerEventHub _eventHub;
        private IDisposable _typeSelectedSubscription;
        private bool _registrySubscribed;
        public ProcessorPanelController(
            DataVisualizer dataVisualizer,
            VisualizerSessionState sessionState,
            IDataProcessorRegistry registry
        )
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            _root = new VisualElement { name = "processor-column" };
            _root.AddToClassList("processor-column");

            _header = new VisualElement { name = "processor-column-header" };
            _header.AddToClassList("processor-column-header");

            _collapseToggle = new Label();
            _collapseToggle.AddToClassList("collapse-toggle");
            _collapseToggle.AddToClassList(StyleConstants.ClickableClass);
            _collapseToggle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                ToggleCollapse();
            });

            _headerLabel = new Label("Processors")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold },
            };

            _header.Add(_collapseToggle);
            _header.Add(_headerLabel);
            _root.Add(_header);

            _content = new VisualElement { style = { flexDirection = FlexDirection.Column } };
            _content.AddToClassList("processor-area");

            _logicToggle = new HorizontalToggle
            {
                name = "processor-logic-toggle",
                LeftText = "ALL",
                RightText = "FILTERED",
            };
            _logicToggle.AddToClassList("processor");
            _logicToggle.OnLeftSelected += () => SetProcessorLogic(ProcessorLogic.All);
            _logicToggle.OnRightSelected += () => SetProcessorLogic(ProcessorLogic.Filtered);
            _content.Add(_logicToggle);

            _statusLabel = new Label
            {
                name = "processor-status-label",
                style =
                {
                    display = DisplayStyle.None,
                    unityFontStyleAndWeight = FontStyle.Italic,
                    marginBottom = 4,
                    marginLeft = 4,
                },
            };
            _statusLabel.AddToClassList("processor-status-label");
            _content.Add(_statusLabel);

            _scrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "processor-list-scrollview",
            };
            _scrollView.AddToClassList("processor-list-scrollview");

            _listContainer = new VisualElement { name = "processor-list-container" };
            _listContainer.AddToClassList("processor-list-container");
            _scrollView.Add(_listContainer);

            _content.Add(_scrollView);
            _root.Add(_content);
        }

        public VisualElement RootElement => _root;

        public void Bind(DataVisualizerEventHub eventHub)
        {
            if (eventHub == null)
            {
                return;
            }

            Unbind();
            _eventHub = eventHub;
            _typeSelectedSubscription = _eventHub.Subscribe<TypeSelectedEvent>(HandleTypeSelected);
            if (!_registrySubscribed)
            {
                _registry.ProcessorsChanged += HandleProcessorsChanged;
                _registrySubscribed = true;
            }
        }

        public void Unbind()
        {
            _typeSelectedSubscription?.Dispose();
            _typeSelectedSubscription = null;
            _eventHub = null;
            if (_registrySubscribed)
            {
                _registry.ProcessorsChanged -= HandleProcessorsChanged;
                _registrySubscribed = false;
            }
        }

        public void RebuildProcessorCache()
        {
            _registry.Refresh();
        }

        public void Refresh()
        {
            _listContainer.Clear();
            _compatibleProcessors.Clear();

            ProcessorPanelState panelState = _sessionState.Processors;
            Type selectedType = _dataVisualizer.GetSelectedType();
            if (selectedType != null)
            {
                IReadOnlyList<IDataProcessor> compatible = _registry.GetCompatibleProcessors(
                    selectedType
                );
                for (int index = 0; index < compatible.Count; index++)
                {
                    _compatibleProcessors.Add(compatible[index]);
                }
            }

            if (_compatibleProcessors.Count == 0)
            {
                _root.style.display = DisplayStyle.None;
                return;
            }

            _root.style.display = DisplayStyle.Flex;
            _collapseToggle.SetEnabled(true);

            ProcessorState state =
                _sessionState.Processors.ActiveState ?? _dataVisualizer.CurrentProcessorState;
            ApplyLogicToggle(state?.logic ?? ProcessorLogic.Filtered, force: true);
            ApplyCollapseState(state?.isCollapsed == true, panelState);

            if (state?.isCollapsed == true)
            {
                UpdateStatusLabel(panelState);
                return;
            }

            UpdateStatusLabel(panelState);

            bool disableButtons = panelState.IsExecuting;
            for (int index = 0; index < _compatibleProcessors.Count; index++)
            {
                IDataProcessor processor = _compatibleProcessors[index];
                string processorName = processor.Name;
                if (!_dataVisualizer._textColorCache.TryGetValue(processorName, out Color color))
                {
                    color = DataVisualizer.GenerateColorForText(processorName);
                    _dataVisualizer._textColorCache[processorName] = color;
                }

                Label processorButton = new Label(processorName)
                {
                    tooltip = processor.Description,
                    style =
                    {
                        backgroundColor = color,
                        color = DataVisualizer.IsColorDark(color) ? Color.white : Color.black,
                    },
                };
                processorButton.AddToClassList("processor-button");
                processorButton.AddToClassList(StyleConstants.ClickableClass);

                Color highlightColor = color;
                IDataProcessor localProcessor = processor;
                processorButton.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }

                    Type targetType = _dataVisualizer.GetSelectedType();
                    if (targetType == null)
                    {
                        return;
                    }

                    _eventHub?.Publish(
                        new ProcessorExecutionRequestedEvent(
                            localProcessor,
                            targetType,
                            highlightColor,
                            processorButton
                        )
                    );
                });

                processorButton.SetEnabled(!disableButtons);
                _listContainer.Add(processorButton);
            }
        }

        public void ToggleCollapse()
        {
            ProcessorState state =
                _sessionState.Processors.ActiveState ?? _dataVisualizer.CurrentProcessorState;
            if (state != null)
            {
                state.isCollapsed = !state.isCollapsed;
                _dataVisualizer.SaveProcessorState(state);
            }

            Refresh();
        }

        private void SetProcessorLogic(ProcessorLogic logic)
        {
            ProcessorState state =
                _sessionState.Processors.ActiveState ?? _dataVisualizer.CurrentProcessorState;
            if (state != null && state.logic != logic)
            {
                state.logic = logic;
                _dataVisualizer.SaveProcessorState(state);
            }

            ApplyLogicToggle(logic, force: true);
            Refresh();
        }

        private void ApplyLogicToggle(ProcessorLogic logic, bool force)
        {
            switch (logic)
            {
                case ProcessorLogic.All:
                {
                    _logicToggle.SelectLeft(force);
                    _logicToggle.Indicator.style.backgroundColor = new Color(0f, 0.392f, 0f);
                    _logicToggle.LeftLabel.EnableInClassList(StyleConstants.ClickableClass, false);
                    _logicToggle.RightLabel.EnableInClassList(StyleConstants.ClickableClass, true);
                    break;
                }
                case ProcessorLogic.Filtered:
                {
                    _logicToggle.SelectRight(force);
                    _logicToggle.Indicator.style.backgroundColor = new Color(
                        1f,
                        0.5f,
                        0.3137254902f
                    );
                    _logicToggle.LeftLabel.EnableInClassList(StyleConstants.ClickableClass, true);
                    _logicToggle.RightLabel.EnableInClassList(StyleConstants.ClickableClass, false);
                    break;
                }
            }
        }

        private void ApplyCollapseState(bool isCollapsed, ProcessorPanelState panelState)
        {
            if (isCollapsed)
            {
                _collapseToggle.text = StyleConstants.ArrowCollapsed;
                if (panelState.IsExecuting)
                {
                    _headerLabel.text =
                        $"Processors (<b><color=yellow>running…</color></b>)";
                }
                else
                {
                    _headerLabel.text =
                        $"Processors (<b><color=yellow>{_compatibleProcessors.Count}</color></b>)";
                }
                _content.style.display = DisplayStyle.None;
                _header.style.borderBottomWidth = 1;
                return;
            }

            _collapseToggle.text = StyleConstants.ArrowExpanded;
            _headerLabel.text = "Processors";
            _content.style.display = DisplayStyle.Flex;
            _header.style.borderBottomWidth = 0;
        }

        private void HandleTypeSelected(TypeSelectedEvent evt)
        {
            Refresh();
        }

        public void Dispose()
        {
            Unbind();
        }

        private void HandleProcessorsChanged()
        {
            Refresh();
        }

        private void UpdateStatusLabel(ProcessorPanelState panelState)
        {
            if (panelState == null)
            {
                _statusLabel.style.display = DisplayStyle.None;
                _statusLabel.text = string.Empty;
                return;
            }

            if (panelState.IsExecuting)
            {
                string pendingText = panelState.PendingExecutionCount > 0
                    ? $" · Pending: {panelState.PendingExecutionCount}"
                    : string.Empty;
                _statusLabel.text =
                    $"Running {panelState.ActiveProcessorName} on {panelState.ActiveObjectCount} objects{pendingText}";
                _statusLabel.style.display = DisplayStyle.Flex;
                return;
            }

            if (!string.IsNullOrWhiteSpace(panelState.LastExecutionError))
            {
                _statusLabel.text =
                    $"Last processor failed: {panelState.LastExecutionError}";
                _statusLabel.style.display = DisplayStyle.Flex;
                return;
            }

            if (panelState.LastExecutionDurationSeconds.HasValue)
            {
                double duration = panelState.LastExecutionDurationSeconds.Value;
                string pendingText = panelState.PendingExecutionCount > 0
                    ? $" · Pending: {panelState.PendingExecutionCount}"
                    : string.Empty;
                _statusLabel.text =
                    $"Last processor finished in {duration:0.00}s{pendingText}";
                _statusLabel.style.display = DisplayStyle.Flex;
                return;
            }

            _statusLabel.style.display = DisplayStyle.None;
            _statusLabel.text = string.Empty;
        }
    }
}
