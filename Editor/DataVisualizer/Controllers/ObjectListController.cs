namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
    using Data;
    using Events;
    using Services;
    using State;
    using Styles;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class ObjectListController
    {
        private readonly DataVisualizer _dataVisualizer;
        private readonly ObjectSelectionService _selectionService;
        private readonly VisualizerSessionState _sessionState;
        private readonly DataVisualizerEventHub _eventHub;
        private readonly ObjectListState _objectListState;
        private Action<Type> _createPopoverBuilder;
        private Action<VisualElement, VisualElement> _popoverOpener;
        private VisualElement _popoverReference;
        private Button _createObjectButton;
        private VisualElement _objectPageController;
        private Button _previousPageButton;
        private Button _nextPageButton;
        private IntegerField _currentPageField;
        private IntegerField _maxPageField;
        private ScrollView _listScrollView;
        private VisualElement _listContainer;
        private Label _emptyObjectLabel;

        public ObjectListController(
            DataVisualizer dataVisualizer,
            ObjectSelectionService selectionService,
            VisualizerSessionState sessionState,
            DataVisualizerEventHub eventHub
        )
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _selectionService =
                selectionService ?? throw new ArgumentNullException(nameof(selectionService));
            _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _objectListState = _sessionState.Objects;
        }

        public void ConfigureCreateButton(
            Action<Type> createPopoverBuilder,
            Action<VisualElement, VisualElement> popoverOpener,
            VisualElement createPopover
        )
        {
            _createPopoverBuilder = createPopoverBuilder;
            _popoverOpener = popoverOpener;
            _popoverReference = createPopover;
            UpdateCreateButtonState();
        }

        public void BuildObjectListSection(VisualElement objectColumn)
        {
            if (objectColumn == null)
            {
                throw new ArgumentNullException(nameof(objectColumn));
            }

            EnsureHeader(objectColumn);
            EnsureLabelsSection(objectColumn);
            EnsurePaginationControls();
            EnsureListContainer();
            EnsureEmptyLabel();

            AttachElement(_objectPageController, objectColumn);
            AttachElement(_listScrollView, objectColumn);
            AttachElement(_emptyObjectLabel, objectColumn);
        }

        public void BuildObjectsView()
        {
            if (_listContainer == null)
            {
                PublishSelectionChanged(null);
                return;
            }

            List<ScriptableObject> selectedObjects = _dataVisualizer._selectedObjects;
            selectedObjects.RemoveAll(obj => obj == null);

            Type selectedType = _dataVisualizer._namespaceController.SelectedType;
            UpdateCreateButtonState();
            if (selectedType == null)
            {
                HandleNoSelectedType();
                PublishSelectionChanged(null);
                return;
            }

            _dataVisualizer.ApplyLabelFilter(false);

            if (_emptyObjectLabel != null)
            {
                _emptyObjectLabel.style.display = DisplayStyle.None;
            }

            if (selectedObjects.Count == 0)
            {
                HandleNoObjectsForSelectedType(selectedType);
                PublishPageChanged(selectedType);
                PublishSelectionChanged(null);
                return;
            }

            if (_dataVisualizer.FilteredMetadata.Count == 0)
            {
                HandleNoFilteredObjects(selectedType);
                PublishPageChanged(selectedType);
                PublishSelectionChanged(null);
                return;
            }

            PrepareListForDisplay();
            UpdateSelectionState();
            PublishPageChanged(selectedType);
            PublishSelectionChanged(_dataVisualizer._selectedObject);
        }

        public void HandlePreviousPageRequested()
        {
            Type selectedType = _dataVisualizer._namespaceController.SelectedType;
            if (selectedType == null)
            {
                return;
            }

            int currentPage = _dataVisualizer.GetCurrentPage(selectedType);
            if (currentPage <= 0)
            {
                return;
            }

            _dataVisualizer.SetCurrentPage(selectedType, currentPage - 1);
            BuildObjectsView();
        }

        public void HandleNextPageRequested()
        {
            Type selectedType = _dataVisualizer._namespaceController.SelectedType;
            if (selectedType == null)
            {
                return;
            }

            int currentPage = _dataVisualizer.GetCurrentPage(selectedType);
            int maxPage = _dataVisualizer.FilteredMetadata.Count / DataVisualizer.MaxObjectsPerPage;
            if (currentPage >= maxPage)
            {
                return;
            }

            _dataVisualizer.SetCurrentPage(selectedType, currentPage + 1);
            BuildObjectsView();
        }

        public void HandleCurrentPageChanged(int requestedPage)
        {
            Type selectedType = _dataVisualizer._namespaceController.SelectedType;
            if (selectedType == null)
            {
                return;
            }

            int maxPage = _dataVisualizer.FilteredMetadata.Count / DataVisualizer.MaxObjectsPerPage;
            int clampedPage = Mathf.Clamp(requestedPage, 0, maxPage);
            if (clampedPage != requestedPage && _currentPageField != null)
            {
                _currentPageField.SetValueWithoutNotify(clampedPage);
            }

            _dataVisualizer.SetCurrentPage(selectedType, clampedPage);
            BuildObjectsView();
        }

        private void HandleNoSelectedType()
        {
            _objectListState.ClearDisplayed();
            ClearRowViewModels();
            _listContainer?.Clear();

            if (_objectPageController != null)
            {
                _objectPageController.style.display = DisplayStyle.None;
            }

            if (_emptyObjectLabel != null)
            {
                _emptyObjectLabel.text = "Select a type to see objects.";
                _emptyObjectLabel.style.display = DisplayStyle.Flex;
            }

            if (_listScrollView != null)
            {
                _listScrollView.style.display = DisplayStyle.None;
            }

            UpdateCreateButtonState();
        }

        private void HandleNoObjectsForSelectedType(Type selectedType)
        {
            _objectListState.ClearDisplayed();
            ClearRowViewModels();
            _listContainer?.Clear();
            if (_listScrollView != null)
            {
                _listScrollView.style.display = DisplayStyle.None;
            }
            if (_emptyObjectLabel != null)
            {
                _emptyObjectLabel.text =
                    $"No objects of type '{selectedType.Name}' found.\nUse the '+' button above to create one.";
                _emptyObjectLabel.style.display = DisplayStyle.Flex;
            }

            if (_objectPageController != null)
            {
                _objectPageController.style.display = DisplayStyle.None;
            }

            UpdateCreateButtonState();
        }

        private void HandleNoFilteredObjects(Type selectedType)
        {
            _objectListState.ClearDisplayed();
            ClearRowViewModels();
            _listContainer?.Clear();
            if (_listScrollView != null)
            {
                _listScrollView.style.display = DisplayStyle.None;
            }
            if (_emptyObjectLabel != null)
            {
                string typeDisplay = NamespaceController.GetTypeDisplayName(selectedType);
                string message =
                    _dataVisualizer._selectedObjects.Count > 0
                        ? $"No objects of type '{typeDisplay}' match the current label filter."
                        : $"No objects of type '{typeDisplay}' found.";
                _emptyObjectLabel.text = message;
                _emptyObjectLabel.style.display = DisplayStyle.Flex;
            }

            if (_objectPageController != null)
            {
                _objectPageController.style.display = DisplayStyle.None;
            }

            UpdateCreateButtonState();
        }

        private void PrepareListForDisplay()
        {
            if (_listScrollView != null)
            {
                _listScrollView.style.display = DisplayStyle.Flex;
            }
            if (_emptyObjectLabel != null)
            {
                _emptyObjectLabel.style.display = DisplayStyle.None;
            }

            _dataVisualizer._objectVisualElementMap.Clear();

            IReadOnlyList<ScriptableObject> filtered = _objectListState.FilteredObjects;
            IReadOnlyList<DataAssetMetadata> filteredMetadata = _objectListState.FilteredMetadata;
            List<ScriptableObject> displayedObjects = _objectListState.DisplayedObjectsBuffer;
            List<DataAssetMetadata> displayedMetadata = _objectListState.DisplayedMetadataBuffer;
            if (filtered.Count <= DataVisualizer.MaxObjectsPerPage)
            {
                if (_objectPageController != null)
                {
                    _objectPageController.style.display = DisplayStyle.None;
                }

                _objectListState.ClearDisplayed();
                displayedObjects.AddRange(filtered);
                displayedMetadata.AddRange(filteredMetadata);
                _objectListState.SetDisplayStartIndex(0);
                SynchronizeRowViewModels(_objectListState.DisplayStartIndex);
            }
            else
            {
                if (_objectPageController != null)
                {
                    _objectPageController.style.display = DisplayStyle.Flex;
                }

                if (_maxPageField != null)
                {
                    _maxPageField.value = filtered.Count / DataVisualizer.MaxObjectsPerPage;
                }

                int currentPage = _dataVisualizer.GetCurrentPage(
                    _dataVisualizer._namespaceController.SelectedType
                );
                currentPage = Mathf.Clamp(
                    currentPage,
                    0,
                    filtered.Count / DataVisualizer.MaxObjectsPerPage
                );
                _currentPageField?.SetValueWithoutNotify(currentPage);

                _previousPageButton?.EnableInClassList("go-button-disabled", currentPage <= 0);
                _previousPageButton?.EnableInClassList(
                    StyleConstants.ActionButtonClass,
                    currentPage > 0
                );
                _previousPageButton?.EnableInClassList("go-button", currentPage > 0);

                bool disableNext = _maxPageField != null && _maxPageField.value <= currentPage;
                _nextPageButton?.EnableInClassList("go-button-disabled", disableNext);
                _nextPageButton?.EnableInClassList(StyleConstants.ActionButtonClass, !disableNext);
                _nextPageButton?.EnableInClassList("go-button", !disableNext);

                int startIndex = currentPage * DataVisualizer.MaxObjectsPerPage;
                int endIndex = Mathf.Min(
                    startIndex + DataVisualizer.MaxObjectsPerPage,
                    filtered.Count
                );

                _objectListState.ClearDisplayed();
                for (int index = startIndex; index < endIndex; index++)
                {
                    displayedObjects.Add(filtered[index]);
                    if (index < filteredMetadata.Count)
                    {
                        displayedMetadata.Add(filteredMetadata[index]);
                    }
                }

                _objectListState.SetDisplayStartIndex(startIndex);
                SynchronizeRowViewModels(_objectListState.DisplayStartIndex);
            }

            RebuildListFromViewModels();
            UpdateCreateButtonState();
        }

        private void UpdateSelectionState()
        {
            ScriptableObject selectedObject = _dataVisualizer._selectedObject;
            _selectionService.SynchronizeSelection(
                _dataVisualizer._selectedObjects,
                selectedObject
            );

            if (selectedObject != null)
            {
                int displayedIndex = _objectListState.DisplayedObjectsBuffer.IndexOf(
                    selectedObject
                );
                if (displayedIndex >= 0 && _listContainer != null)
                {
                    if (displayedIndex < _listContainer.childCount)
                    {
                        VisualElement row = _listContainer[displayedIndex];
                        _listScrollView?.ScrollTo(row);
                    }
                }
            }
        }

        private void PublishPageChanged(Type selectedType)
        {
            if (selectedType == null)
            {
                return;
            }

            int pageIndex = _dataVisualizer.GetCurrentPage(selectedType);
            _eventHub.Publish(new ObjectPageChangedEvent(selectedType, pageIndex));
        }

        private void PublishSelectionChanged(ScriptableObject primarySelection)
        {
            IReadOnlyList<ScriptableObject> snapshot = new List<ScriptableObject>(
                _dataVisualizer._selectedObjects
            );
            _eventHub.Publish(new ObjectSelectionChangedEvent(primarySelection, snapshot));
        }

        internal void PublishSelectionChangedForTesting()
        {
            PublishSelectionChanged(_dataVisualizer._selectedObject);
        }

        private void SynchronizeRowViewModels(int startIndex)
        {
            List<ObjectRowViewModel> viewModels = _dataVisualizer._objectRowViewModels;
            Stack<ObjectRowViewModel> pool = _dataVisualizer._objectRowViewModelPool;
            List<ScriptableObject> displayedObjects = _objectListState.DisplayedObjectsBuffer;
            List<DataAssetMetadata> displayedMetadata = _objectListState.DisplayedMetadataBuffer;

            while (viewModels.Count < displayedObjects.Count)
            {
                ObjectRowViewModel viewModel =
                    pool.Count > 0 ? pool.Pop() : new ObjectRowViewModel();
                viewModels.Add(viewModel);
            }

            while (viewModels.Count > displayedObjects.Count)
            {
                int lastIndex = viewModels.Count - 1;
                ObjectRowViewModel recycled = viewModels[lastIndex];
                viewModels.RemoveAt(lastIndex);
                pool.Push(recycled);
            }

            for (int index = 0; index < viewModels.Count; index++)
            {
                ScriptableObject dataObject = displayedObjects[index];
                DataAssetMetadata metadata =
                    index < displayedMetadata.Count ? displayedMetadata[index] : null;
                viewModels[index].Update(dataObject, metadata, startIndex + index);
            }
        }

        private void RebuildListFromViewModels()
        {
            if (_listContainer == null)
            {
                return;
            }

            _listContainer.Clear();
            IReadOnlyList<ObjectRowViewModel> viewModels = _dataVisualizer._objectRowViewModels;
            int count = viewModels.Count;
            for (int index = 0; index < count; index++)
            {
                VisualElement row = _dataVisualizer.MakeObjectRow();
                row.style.marginTop = index == 0 ? 6f : 0f;
                row.style.marginBottom = 6f;
                _listContainer.Add(row);
                _dataVisualizer.BindObjectRow(row, index);
            }

            if (_listScrollView != null)
            {
                _listScrollView.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        internal ScrollView GetListScrollView()
        {
            return _listScrollView;
        }

        internal VisualElement GetListContentContainer()
        {
            return _listContainer;
        }

        internal void ShowEmptyLabel()
        {
            if (_emptyObjectLabel == null)
            {
                return;
            }

            _emptyObjectLabel.style.display = DisplayStyle.Flex;
        }

        private void EnsurePaginationControls()
        {
            if (_objectPageController != null)
            {
                return;
            }

            _objectPageController = new VisualElement
            {
                name = "object-page-controller",
                style = { display = DisplayStyle.None },
            };
            _objectPageController.AddToClassList("object-page-controller");

            _previousPageButton = new Button(() => HandlePreviousPageRequested()) { text = "←" };
            _previousPageButton.AddToClassList("go-button-disabled");

            _currentPageField = new IntegerField();
            _currentPageField.AddToClassList("current-page-field");
            _currentPageField.RegisterValueChangedCallback(evt =>
            {
                HandleCurrentPageChanged(evt.newValue);
            });

            _maxPageField = new IntegerField { isReadOnly = true };
            _maxPageField.AddToClassList("max-page-field");

            _nextPageButton = new Button(() => HandleNextPageRequested()) { text = "→" };
            _nextPageButton.AddToClassList("go-button-disabled");

            _objectPageController.Add(_previousPageButton);
            _objectPageController.Add(_currentPageField);
            _objectPageController.Add(_maxPageField);
            _objectPageController.Add(_nextPageButton);
        }

        private void EnsureListContainer()
        {
            if (_listScrollView != null)
            {
                return;
            }

            _listScrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "object-list",
                style = { flexGrow = 1 },
            };
            _listScrollView.AddToClassList("object-list");
            _listScrollView.contentContainer.style.flexDirection = FlexDirection.Column;

            _listContainer = new VisualElement
            {
                name = "object-list-container",
                style =
                {
                    flexDirection = FlexDirection.Column,
                    paddingTop = 0f,
                    paddingBottom = 6f,
                },
            };
            _listContainer.AddToClassList("object-list-container");
            _listScrollView.contentContainer.Add(_listContainer);
        }

        private void EnsureEmptyLabel()
        {
            if (_emptyObjectLabel != null)
            {
                return;
            }

            _emptyObjectLabel = new Label(string.Empty)
            {
                name = "empty-object-list-label",
                style =
                {
                    alignSelf = Align.Center,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    display = DisplayStyle.None,
                    whiteSpace = WhiteSpace.Normal,
                    paddingTop = 8,
                    paddingBottom = 8,
                },
            };
            _emptyObjectLabel.AddToClassList("empty-object-list-label");
        }

        private static void AttachElement(VisualElement element, VisualElement parent)
        {
            if (element == null)
            {
                return;
            }

            element.RemoveFromHierarchy();
            parent.Add(element);
        }

        private void EnsureHeader(VisualElement objectColumn)
        {
            VisualElement header = objectColumn.Q<VisualElement>(name: "object-header");
            if (header == null)
            {
                header = new VisualElement { name = "object-header" };
                header.AddToClassList("object-header");
                header.Add(new Label("Objects"));
            }
            else
            {
                header.Clear();
                header.Add(new Label("Objects"));
            }

            _createObjectButton = new Button(() => HandleCreateRequested())
            {
                text = "+",
                tooltip = "Create New Object",
                name = "create-object-button",
            };
            _createObjectButton.AddToClassList("create-button");
            _createObjectButton.AddToClassList("icon-button");
            _createObjectButton.AddToClassList(StyleConstants.ClickableClass);
            header.Add(_createObjectButton);

            header.RemoveFromHierarchy();
            objectColumn.Insert(0, header);
            UpdateCreateButtonState();
        }

        private void EnsureLabelHeader(VisualElement objectColumn)
        {
            VisualElement header = new VisualElement();
            header.AddToClassList("collapse-row");

            Label toggle = new Label();
            toggle.AddToClassList(StyleConstants.ClickableClass);
            toggle.AddToClassList("collapse-toggle");
            toggle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
                if (config == null)
                {
                    return;
                }

                _dataVisualizer.ToggleLabelsCollapsed(!config.isCollapsed);
                evt.StopPropagation();
            });
            header.Add(toggle);

            header.Add(new Label("Labels") { name = "labels-header" });

            objectColumn.Add(header);
        }

        private void HandleCreateRequested()
        {
            Type selectedType = _dataVisualizer._namespaceController.SelectedType;
            if (selectedType == null)
            {
                return;
            }

            if (
                _createPopoverBuilder == null
                || _popoverOpener == null
                || _popoverReference == null
                || _createObjectButton == null
            )
            {
                return;
            }

            _createPopoverBuilder(selectedType);
            _popoverOpener(_popoverReference, _createObjectButton);
        }

        private void EnsureLabelsSection(VisualElement objectColumn)
        {
            _dataVisualizer._labelPanelController.BuildLabelPanel(objectColumn);
        }

        private void UpdateCreateButtonState()
        {
            if (_createObjectButton == null)
            {
                return;
            }

            _createObjectButton.style.display =
                _dataVisualizer._namespaceController.SelectedType != null
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }

        private void ClearRowViewModels()
        {
            List<ObjectRowViewModel> viewModels = _dataVisualizer._objectRowViewModels;
            Stack<ObjectRowViewModel> pool = _dataVisualizer._objectRowViewModelPool;
            for (int index = 0; index < viewModels.Count; index++)
            {
                pool.Push(viewModels[index]);
            }

            viewModels.Clear();
        }
    }
}
