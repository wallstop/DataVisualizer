namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
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
        }

        public void BuildObjectsView()
        {
            if (_dataVisualizer._objectListView == null)
            {
                PublishSelectionChanged(null);
                return;
            }

            List<ScriptableObject> selectedObjects = _dataVisualizer._selectedObjects;
            selectedObjects.RemoveAll(obj => obj == null);

            Type selectedType = _dataVisualizer._namespaceController.SelectedType;
            if (selectedType == null)
            {
                HandleNoSelectedType();
                PublishSelectionChanged(null);
                return;
            }

            _dataVisualizer.ApplyLabelFilter(false);

            if (_dataVisualizer._emptyObjectLabel != null)
            {
                _dataVisualizer._emptyObjectLabel.style.display = DisplayStyle.None;
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
            if (clampedPage != requestedPage)
            {
                IntegerField currentPageField = _dataVisualizer._currentPageField;
                if (currentPageField != null)
                {
                    currentPageField.SetValueWithoutNotify(clampedPage);
                }
            }

            _dataVisualizer.SetCurrentPage(selectedType, clampedPage);
            BuildObjectsView();
        }

        public void HandleSelectionChanged(IEnumerable<object> selectedItems)
        {
            if (_dataVisualizer._isUpdatingListSelection)
            {
                return;
            }

            ScriptableObject firstSelection = null;
            if (selectedItems != null)
            {
                foreach (object item in selectedItems)
                {
                    ScriptableObject candidate = item as ScriptableObject;
                    if (candidate != null)
                    {
                        firstSelection = candidate;
                        break;
                    }
                }
            }

            if (!ReferenceEquals(firstSelection, _dataVisualizer._selectedObject))
            {
                _dataVisualizer.SelectObject(firstSelection);
            }
            PublishSelectionChanged(_dataVisualizer._selectedObject);
        }

        private void HandleNoSelectedType()
        {
            _dataVisualizer._displayedObjects.Clear();
            _dataVisualizer._currentDisplayStartIndex = 0;
            _dataVisualizer._objectListView.RefreshItems();
            if (_dataVisualizer._objectPageController != null)
            {
                _dataVisualizer._objectPageController.style.display = DisplayStyle.None;
            }

            if (_dataVisualizer._emptyObjectLabel != null)
            {
                _dataVisualizer._emptyObjectLabel.text = "Select a type to see objects.";
                _dataVisualizer._emptyObjectLabel.style.display = DisplayStyle.Flex;
            }

            _dataVisualizer._objectListView.style.display = DisplayStyle.None;
        }

        private void HandleNoObjectsForSelectedType(Type selectedType)
        {
            _dataVisualizer._displayedObjects.Clear();
            _dataVisualizer._currentDisplayStartIndex = 0;
            _dataVisualizer._objectListView.RefreshItems();
            _dataVisualizer._objectListView.style.display = DisplayStyle.None;
            if (_dataVisualizer._emptyObjectLabel != null)
            {
                _dataVisualizer._emptyObjectLabel.text =
                    $"No objects of type '{selectedType.Name}' found.\nUse the '+' button above to create one.";
                _dataVisualizer._emptyObjectLabel.style.display = DisplayStyle.Flex;
            }

            if (_dataVisualizer._objectPageController != null)
            {
                _dataVisualizer._objectPageController.style.display = DisplayStyle.None;
            }
        }

        private void HandleNoFilteredObjects(Type selectedType)
        {
            _dataVisualizer._displayedObjects.Clear();
            _dataVisualizer._currentDisplayStartIndex = 0;
            _dataVisualizer._objectListView.RefreshItems();
            _dataVisualizer._objectListView.style.display = DisplayStyle.None;
            if (_dataVisualizer._emptyObjectLabel != null)
            {
                string typeDisplay = NamespaceController.GetTypeDisplayName(selectedType);
                string message =
                    _dataVisualizer._selectedObjects.Count > 0
                        ? $"No objects of type '{typeDisplay}' match the current label filter."
                        : $"No objects of type '{typeDisplay}' found.";
                _dataVisualizer._emptyObjectLabel.text = message;
                _dataVisualizer._emptyObjectLabel.style.display = DisplayStyle.Flex;
            }

            if (_dataVisualizer._objectPageController != null)
            {
                _dataVisualizer._objectPageController.style.display = DisplayStyle.None;
            }
        }

        private void PrepareListForDisplay()
        {
            _dataVisualizer._objectListView.style.display = DisplayStyle.Flex;
            if (_dataVisualizer._emptyObjectLabel != null)
            {
                _dataVisualizer._emptyObjectLabel.style.display = DisplayStyle.None;
            }

            _dataVisualizer._objectVisualElementMap.Clear();

            IReadOnlyList<ScriptableObject> filtered = _dataVisualizer.FilteredObjects;
            IReadOnlyList<DataAssetMetadata> filteredMetadata = _dataVisualizer.FilteredMetadata;
            if (filtered.Count <= DataVisualizer.MaxObjectsPerPage)
            {
                if (_dataVisualizer._objectPageController != null)
                {
                    _dataVisualizer._objectPageController.style.display = DisplayStyle.None;
                }

                _dataVisualizer._displayedObjects.Clear();
                _dataVisualizer._displayedObjects.AddRange(filtered);
                _dataVisualizer._displayedMetadata.Clear();
                _dataVisualizer._displayedMetadata.AddRange(filteredMetadata);
                _dataVisualizer._currentDisplayStartIndex = 0;
            }
            else
            {
                if (_dataVisualizer._objectPageController != null)
                {
                    _dataVisualizer._objectPageController.style.display = DisplayStyle.Flex;
                }

                _dataVisualizer._maxPageField.value =
                    filtered.Count / DataVisualizer.MaxObjectsPerPage;
                int currentPage = _dataVisualizer.GetCurrentPage(
                    _dataVisualizer._namespaceController.SelectedType
                );
                currentPage = Mathf.Clamp(
                    currentPage,
                    0,
                    filtered.Count / DataVisualizer.MaxObjectsPerPage
                );
                _dataVisualizer._currentPageField.SetValueWithoutNotify(currentPage);

                _dataVisualizer._previousPageButton.EnableInClassList(
                    "go-button-disabled",
                    currentPage <= 0
                );
                _dataVisualizer._previousPageButton.EnableInClassList(
                    StyleConstants.ActionButtonClass,
                    currentPage > 0
                );
                _dataVisualizer._previousPageButton.EnableInClassList("go-button", currentPage > 0);

                _dataVisualizer._nextPageButton.EnableInClassList(
                    "go-button-disabled",
                    _dataVisualizer._maxPageField.value <= currentPage
                );
                _dataVisualizer._nextPageButton.EnableInClassList(
                    StyleConstants.ActionButtonClass,
                    currentPage < _dataVisualizer._maxPageField.value
                );
                _dataVisualizer._nextPageButton.EnableInClassList(
                    "go-button",
                    currentPage < _dataVisualizer._maxPageField.value
                );

                int startIndex = currentPage * DataVisualizer.MaxObjectsPerPage;
                int endIndex = Mathf.Min(
                    startIndex + DataVisualizer.MaxObjectsPerPage,
                    filtered.Count
                );

                _dataVisualizer._displayedObjects.Clear();
                _dataVisualizer._displayedMetadata.Clear();
                for (int index = startIndex; index < endIndex; index++)
                {
                    _dataVisualizer._displayedObjects.Add(filtered[index]);
                    if (index < filteredMetadata.Count)
                    {
                        _dataVisualizer._displayedMetadata.Add(filteredMetadata[index]);
                    }
                }

                _dataVisualizer._currentDisplayStartIndex = startIndex;
            }

            _dataVisualizer._objectListView.itemsSource = null;
            _dataVisualizer._objectListView.itemsSource = _dataVisualizer._displayedObjects;
            _dataVisualizer._objectListView.RefreshItems();
            _dataVisualizer._objectListView.Rebuild();
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
                int displayedIndex = _dataVisualizer._displayedObjects.IndexOf(selectedObject);
                _dataVisualizer._isUpdatingListSelection = true;
                if (displayedIndex >= 0)
                {
                    _dataVisualizer._objectListView.SetSelectionWithoutNotify(
                        new int[] { displayedIndex }
                    );
                }
                else
                {
                    _dataVisualizer._objectListView.ClearSelection();
                }

                _dataVisualizer._isUpdatingListSelection = false;
                return;
            }

            _dataVisualizer._isUpdatingListSelection = true;
            _dataVisualizer._objectListView.ClearSelection();
            _dataVisualizer._isUpdatingListSelection = false;
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
    }
}
