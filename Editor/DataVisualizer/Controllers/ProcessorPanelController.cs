namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Data;
    using Services;
    using State;
    using Styles;
    using UI;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class ProcessorPanelController
    {
        private readonly DataVisualizer _dataVisualizer;
        private readonly VisualizerSessionState _sessionState;
        private readonly List<IDataProcessor> _allProcessors = new List<IDataProcessor>();
        private readonly List<IDataProcessor> _compatibleProcessors = new List<IDataProcessor>();
        private readonly VisualElement _root;
        private readonly VisualElement _header;
        private readonly Label _headerLabel;
        private readonly Label _collapseToggle;
        private readonly VisualElement _content;
        private readonly HorizontalToggle _logicToggle;
        private readonly ScrollView _scrollView;
        private readonly VisualElement _listContainer;

        public ProcessorPanelController(
            DataVisualizer dataVisualizer,
            VisualizerSessionState sessionState
        )
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));

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

        public void RebuildProcessorCache()
        {
            _allProcessors.Clear();

            IEnumerable<Type> processorTypes = TypeCache
                .GetTypesDerivedFrom<IDataProcessor>()
                .Where(t => !t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition);

            foreach (Type type in processorTypes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is IDataProcessor processor)
                    {
                        _allProcessors.Add(processor);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"Failed to create instance of IDataProcessor '{type.FullName}': {ex.Message}"
                    );
                }
            }

            _allProcessors.Sort((lhs, rhs) => string.CompareOrdinal(lhs.Name, rhs.Name));
        }

        public void Refresh()
        {
            _listContainer.Clear();
            _compatibleProcessors.Clear();

            Type selectedType = _dataVisualizer._namespaceController.SelectedType;
            if (selectedType != null)
            {
                for (int index = 0; index < _allProcessors.Count; index++)
                {
                    IDataProcessor processor = _allProcessors[index];
                    if (processor.Accepts != null && processor.Accepts.Contains(selectedType))
                    {
                        _compatibleProcessors.Add(processor);
                    }
                }
            }

            if (_compatibleProcessors.Count == 0)
            {
                _root.style.display = DisplayStyle.None;
                return;
            }

            _root.style.display = DisplayStyle.Flex;
            _collapseToggle.SetEnabled(true);

            ProcessorState state = _dataVisualizer.CurrentProcessorState;
            ApplyLogicToggle(state?.logic ?? ProcessorLogic.Filtered, force: true);
            ApplyCollapseState(state?.isCollapsed == true);

            if (state?.isCollapsed == true)
            {
                return;
            }

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

                IDataProcessor localProcessor = processor;
                processorButton.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }

                    _dataVisualizer.RunDataProcessor(processorButton, localProcessor);
                });

                _listContainer.Add(processorButton);
            }
        }

        public void ToggleCollapse()
        {
            ProcessorState state = _dataVisualizer.CurrentProcessorState;
            if (state != null)
            {
                state.isCollapsed = !state.isCollapsed;
                _dataVisualizer.SaveProcessorState(state);
            }

            Refresh();
        }

        private void SetProcessorLogic(ProcessorLogic logic)
        {
            ProcessorState state = _dataVisualizer.CurrentProcessorState;
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

        private void ApplyCollapseState(bool isCollapsed)
        {
            if (isCollapsed)
            {
                _collapseToggle.text = StyleConstants.ArrowCollapsed;
                _headerLabel.text =
                    $"Processors (<b><color=yellow>{_compatibleProcessors.Count}</color></b>)";
                _content.style.display = DisplayStyle.None;
                _header.style.borderBottomWidth = 1;
                return;
            }

            _collapseToggle.text = StyleConstants.ArrowExpanded;
            _headerLabel.text = "Processors";
            _content.style.display = DisplayStyle.Flex;
            _header.style.borderBottomWidth = 0;
        }
    }
}
