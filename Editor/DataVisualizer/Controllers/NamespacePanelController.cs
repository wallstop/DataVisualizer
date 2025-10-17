namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
    using Data;
    using Events;
    using Extensions;
    using State;
    using Styles;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class NamespacePanelController : IDisposable
    {
        private const string RemoveHandlerProperty = "NamespacePanelController.RemoveHandler";

        private readonly DataVisualizer _dataVisualizer;
        private readonly NamespaceController _namespaceController;
        private readonly VisualizerSessionState _sessionState;
        private readonly DataVisualizerEventHub _eventHub;
        private readonly IDisposable _typeNavigationSubscription;
        private readonly List<string> _lastNamespaceOrder = new List<string>();
        private readonly Dictionary<string, List<string>> _lastNamespaceTypeOrders = new Dictionary<
            string,
            List<string>
        >(StringComparer.Ordinal);

        public NamespacePanelController(
            DataVisualizer dataVisualizer,
            NamespaceController namespaceController,
            VisualizerSessionState sessionState,
            DataVisualizerEventHub eventHub
        )
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _namespaceController =
                namespaceController ?? throw new ArgumentNullException(nameof(namespaceController));
            _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _namespaceController.TypeSelected += HandleTypeSelected;
            _typeNavigationSubscription = _eventHub.Subscribe<TypeNavigationRequestedEvent>(
                HandleTypeNavigationRequested
            );
        }

        public void Dispose()
        {
            _namespaceController.TypeSelected -= HandleTypeSelected;
            _typeNavigationSubscription?.Dispose();
        }

        public void BuildNamespaceView()
        {
            bool structureChanged = NamespaceStructureChanged();
            if (structureChanged)
            {
                VisualElement container = _dataVisualizer._namespaceListContainer;
                _namespaceController.Build(_dataVisualizer, ref container);
                _dataVisualizer._namespaceListContainer = container;
                CacheNamespaceStructure();
            }

            WireNamespaceInteractions();
            SynchronizeNamespaceState();
            RenderActiveFilters();
        }

        internal void RenderActiveFilters()
        {
            VisualElement filterContainer = _dataVisualizer._namespaceFilterContainer;
            if (filterContainer == null)
            {
                return;
            }

            filterContainer.Clear();

            VisualizerSessionState.LabelFilterState labelsState = _sessionState.Labels;
            string selectedNamespaceKey = _sessionState.Selection.SelectedNamespaceKey;
            bool hasNamespaceFilter = !string.IsNullOrWhiteSpace(selectedNamespaceKey);
            bool hasLabelFilters =
                labelsState.AndLabels.Count > 0 || labelsState.OrLabels.Count > 0;

            if (!hasNamespaceFilter && !hasLabelFilters)
            {
                filterContainer.style.display = DisplayStyle.None;
                return;
            }

            filterContainer.style.display = DisplayStyle.Flex;

            Label title = new Label("Active Filters:")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 4 },
            };
            filterContainer.Add(title);

            if (hasNamespaceFilter)
            {
                Button namespaceChip = CreateNamespaceChip(selectedNamespaceKey);
                filterContainer.Add(namespaceChip);
            }

            if (hasLabelFilters)
            {
                AddFilterChips(
                    filterContainer,
                    "AND",
                    labelsState.AndLabels,
                    "and",
                    DataVisualizer.LabelFilterSection.AND
                );
                AddFilterChips(
                    filterContainer,
                    "OR",
                    labelsState.OrLabels,
                    "or",
                    DataVisualizer.LabelFilterSection.OR
                );

                Button combinationButton = CreateCombinationToggle(labelsState.CombinationType);
                filterContainer.Add(combinationButton);
            }

            Button clearButton = new Button(() => ClearAllFilters())
            {
                text = "Clear",
                tooltip = "Remove namespace and label filters",
            };
            clearButton.AddToClassList("namespace-filter-clear-button");
            clearButton.AddToClassList(StyleConstants.ClickableClass);
            filterContainer.Add(clearButton);
        }

        private void AddFilterChips(
            VisualElement container,
            string prefix,
            IReadOnlyList<string> labels,
            string styleSuffix,
            DataVisualizer.LabelFilterSection section
        )
        {
            if (labels == null || labels.Count == 0)
            {
                return;
            }

            for (int index = 0; index < labels.Count; index++)
            {
                string labelText = labels[index];
                if (string.IsNullOrWhiteSpace(labelText))
                {
                    continue;
                }

                string buttonText = $"{prefix}: {labelText}";
                Button chip = new Button(() => HandleLabelChipClicked(labelText, section))
                {
                    text = buttonText,
                    tooltip = $"Remove '{labelText}' from {prefix} filters",
                };
                chip.AddToClassList("namespace-filter-chip");
                chip.AddToClassList($"namespace-filter-chip-{styleSuffix}");
                chip.AddToClassList(StyleConstants.ClickableClass);
                container.Add(chip);
            }
        }

        private Button CreateNamespaceChip(string namespaceKey)
        {
            string displayText = namespaceKey ?? string.Empty;
            Button chip = new Button(() => ClearNamespaceSelection())
            {
                text = $"Namespace: {displayText}",
                tooltip = $"Clear namespace filter '{displayText}'",
            };
            chip.AddToClassList("namespace-filter-chip");
            chip.AddToClassList("namespace-filter-chip-namespace");
            chip.AddToClassList(StyleConstants.ClickableClass);
            return chip;
        }

        private Button CreateCombinationToggle(LabelCombinationType combinationType)
        {
            bool isOr = combinationType == LabelCombinationType.Or;
            string buttonText = isOr ? "Logic: OR" : "Logic: AND";
            Button toggle = new Button(() => ToggleCombinationType())
            {
                text = buttonText,
                tooltip = "Toggle between AND (intersection) and OR (union) label matching",
            };
            toggle.AddToClassList("namespace-filter-chip");
            toggle.AddToClassList("namespace-filter-chip-logic");
            toggle.AddToClassList(StyleConstants.ClickableClass);
            return toggle;
        }

        private void HandleLabelChipClicked(
            string labelText,
            DataVisualizer.LabelFilterSection section
        )
        {
            if (string.IsNullOrWhiteSpace(labelText))
            {
                return;
            }

            _dataVisualizer.RemoveLabelFromFilter(labelText, section);
            RenderActiveFilters();
        }

        private void ToggleCombinationType()
        {
            TypeLabelFilterConfig config = _dataVisualizer.CurrentTypeLabelFilterConfig;
            if (config == null)
            {
                return;
            }

            LabelCombinationType next =
                config.combinationType == LabelCombinationType.And
                    ? LabelCombinationType.Or
                    : LabelCombinationType.And;

            if (config.combinationType == next)
            {
                return;
            }

            config.combinationType = next;
            _dataVisualizer.SaveLabelFilterConfig(config);
            _dataVisualizer.ApplyLabelFilter();
            RenderActiveFilters();
        }

        private void ClearNamespaceSelection()
        {
            ClearNamespaceSelectionInternal();
        }

        private bool ClearNamespaceSelectionInternal()
        {
            string selectedNamespaceKey = _sessionState.Selection.SelectedNamespaceKey;
            if (string.IsNullOrWhiteSpace(selectedNamespaceKey))
            {
                return false;
            }

            _namespaceController.SelectType(_dataVisualizer, null);
            _sessionState.Selection.SetSelectedNamespace(null);
            _sessionState.Selection.SetSelectedType(null);
            _sessionState.Selection.SetPrimarySelectedObject(null);

            _dataVisualizer.PersistSettings(
                settings =>
                {
                    bool updated = false;
                    if (!string.IsNullOrWhiteSpace(settings.lastSelectedNamespaceKey))
                    {
                        settings.lastSelectedNamespaceKey = null;
                        updated = true;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.lastSelectedTypeName))
                    {
                        settings.lastSelectedTypeName = null;
                        updated = true;
                    }

                    return updated;
                },
                userState =>
                {
                    bool updated = false;
                    if (!string.IsNullOrWhiteSpace(userState.lastSelectedNamespaceKey))
                    {
                        userState.lastSelectedNamespaceKey = null;
                        updated = true;
                    }

                    if (!string.IsNullOrWhiteSpace(userState.lastSelectedTypeName))
                    {
                        userState.lastSelectedTypeName = null;
                        updated = true;
                    }

                    return updated;
                }
            );

            _eventHub.Publish(new TypeSelectedEvent(null, null));
            _dataVisualizer.BuildObjectsView();
            _dataVisualizer.UpdateLabelAreaAndFilter();
            return true;
        }

        private void ClearAllFilters()
        {
            _dataVisualizer.ClearAllLabelFilters();
            ClearNamespaceSelectionInternal();
        }

        public void ToggleNamespaceCollapse(string namespaceKey)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }

            int order = _dataVisualizer.GetNamespaceOrderIndex(namespaceKey);
            if (order < 0)
            {
                return;
            }

            VisualElement container = _dataVisualizer._namespaceListContainer;
            if (container == null || order < 0 || order >= container.childCount)
            {
                return;
            }

            VisualElement namespaceGroup = container.ElementAt(order);
            if (namespaceGroup == null)
            {
                return;
            }

            Label indicator = namespaceGroup.Q<Label>($"namespace-indicator-{namespaceKey}");
            VisualElement typesContainer = namespaceGroup.Q<VisualElement>(
                $"types-container-{namespaceKey}"
            );
            if (indicator == null || typesContainer == null)
            {
                return;
            }

            bool collapse = typesContainer.style.display != DisplayStyle.None;
            NamespaceController.ApplyNamespaceCollapsedState(
                _dataVisualizer,
                indicator,
                typesContainer,
                collapse,
                true
            );
            _eventHub.Publish(new NamespaceCollapseChangedEvent(namespaceKey, collapse));
        }

        public void SelectType(Type type)
        {
            _namespaceController.SelectType(_dataVisualizer, type);
        }

        private void HandleTypeSelected(Type selectedType)
        {
            string namespaceKey =
                selectedType != null
                    ? ResolveNamespaceKey(selectedType)
                    : _sessionState.Selection.SelectedNamespaceKey;

            if (!string.IsNullOrWhiteSpace(namespaceKey))
            {
                _sessionState.Selection.SetSelectedNamespace(namespaceKey);
            }

            string fullName = selectedType != null ? selectedType.FullName : null;
            _sessionState.Selection.SetSelectedType(fullName);

            if (!string.IsNullOrWhiteSpace(namespaceKey) && !string.IsNullOrWhiteSpace(fullName))
            {
                PersistSelection(namespaceKey, fullName);
            }

            _eventHub.Publish(new TypeSelectedEvent(namespaceKey, selectedType));
        }

        private void HandleTypeNavigationRequested(TypeNavigationRequestedEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (evt.Direction == TypeNavigationDirection.Next)
            {
                _namespaceController.IncrementTypeSelection(_dataVisualizer);
                return;
            }

            _namespaceController.DecrementTypeSelection(_dataVisualizer);
        }

        private string ResolveNamespaceKey(Type type)
        {
            if (type == null)
            {
                return null;
            }

            foreach (string namespaceKey in _dataVisualizer.GetNamespaceKeys())
            {
                IReadOnlyList<Type> types = _dataVisualizer.GetTypesForNamespace(namespaceKey);
                for (int index = 0; index < types.Count; index++)
                {
                    if (types[index] == type)
                    {
                        return namespaceKey;
                    }
                }
            }

            return NamespaceController.GetNamespaceKey(type);
        }

        private void PersistSelection(string namespaceKey, string typeFullName)
        {
            _dataVisualizer.PersistSettings(
                settings =>
                {
                    bool changed = false;
                    if (
                        !string.Equals(
                            settings.lastSelectedNamespaceKey,
                            namespaceKey,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        settings.lastSelectedNamespaceKey = namespaceKey;
                        changed = true;
                    }

                    if (
                        !string.Equals(
                            settings.lastSelectedTypeName,
                            typeFullName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        settings.lastSelectedTypeName = typeFullName;
                        changed = true;
                    }

                    return changed;
                },
                userState =>
                {
                    bool changed = false;
                    if (
                        !string.Equals(
                            userState.lastSelectedNamespaceKey,
                            namespaceKey,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        userState.lastSelectedNamespaceKey = namespaceKey;
                        changed = true;
                    }

                    if (
                        !string.Equals(
                            userState.lastSelectedTypeName,
                            typeFullName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        userState.lastSelectedTypeName = typeFullName;
                        changed = true;
                    }

                    return changed;
                }
            );
        }

        private void WireNamespaceInteractions()
        {
            VisualElement container = _dataVisualizer._namespaceListContainer;
            if (container == null)
            {
                return;
            }

            foreach (VisualElement namespaceGroup in container.Children())
            {
                namespaceGroup.UnregisterCallback<PointerDownEvent>(HandleNamespacePointerDown);
                namespaceGroup.RegisterCallback<PointerDownEvent>(HandleNamespacePointerDown);

                string namespaceKey = namespaceGroup.userData as string;
                if (string.IsNullOrWhiteSpace(namespaceKey))
                {
                    continue;
                }

                List<Type> removableTypes = new List<Type>();
                IReadOnlyList<Type> allTypes = _dataVisualizer.GetTypesForNamespace(namespaceKey);
                for (int index = 0; index < allTypes.Count; index++)
                {
                    Type candidate = allTypes[index];
                    if (NamespaceController.IsTypeRemovable(candidate))
                    {
                        removableTypes.Add(candidate);
                    }
                }

                Label indicator = namespaceGroup.Q<Label>($"namespace-indicator-{namespaceKey}");
                if (indicator != null)
                {
                    indicator.UnregisterCallback<PointerDownEvent>(HandleIndicatorPointerDown);
                    indicator.RegisterCallback<PointerDownEvent>(HandleIndicatorPointerDown);
                }

                Button removeButton = namespaceGroup.Q<Button>(
                    className: StyleConstants.NamespaceDeleteButton
                );
                if (removeButton != null)
                {
                    if (removeButton.GetProperty(RemoveHandlerProperty) is Action existing)
                    {
                        removeButton.clicked -= existing;
                    }

                    Action handler = () =>
                        HandleNamespaceRemoveRequested(namespaceKey, removableTypes, removeButton);
                    removeButton.clicked += handler;
                    removeButton.SetProperty(RemoveHandlerProperty, handler);
                }

                VisualElement typesContainer = namespaceGroup.Q<VisualElement>(
                    $"types-container-{namespaceKey}"
                );
                if (typesContainer == null)
                {
                    continue;
                }

                foreach (VisualElement typeElement in typesContainer.Children())
                {
                    typeElement.UnregisterCallback<PointerDownEvent>(HandleTypePointerDown);
                    typeElement.RegisterCallback<PointerDownEvent>(HandleTypePointerDown);
                    typeElement.UnregisterCallback<PointerUpEvent>(HandleTypePointerUp);
                    typeElement.RegisterCallback<PointerUpEvent>(HandleTypePointerUp);

                    Button typeRemoveButton = typeElement.Q<Button>(
                        className: StyleConstants.DeleteButtonClass
                    );
                    Type removableType = typeRemoveButton?.userData as Type;
                    if (typeRemoveButton != null && removableType != null)
                    {
                        if (
                            typeRemoveButton.GetProperty(RemoveHandlerProperty)
                            is Action existingHandler
                        )
                        {
                            typeRemoveButton.clicked -= existingHandler;
                        }

                        Action handler = () =>
                            HandleTypeRemoveRequested(removableType, typeRemoveButton);
                        typeRemoveButton.clicked += handler;
                        typeRemoveButton.SetProperty(RemoveHandlerProperty, handler);
                    }
                }
            }
        }

        private void HandleNamespacePointerDown(PointerDownEvent evt)
        {
            _dataVisualizer.OnNamespacePointerDown(evt);
        }

        private void HandleIndicatorPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            if (evt.currentTarget is not VisualElement indicator)
            {
                return;
            }

            VisualElement header = indicator.parent;
            VisualElement namespaceGroup = header?.parent;
            string namespaceKey = namespaceGroup?.userData as string;
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }

            ToggleNamespaceCollapse(namespaceKey);
            evt.StopPropagation();
        }

        private void HandleTypePointerDown(PointerDownEvent evt)
        {
            if (evt.currentTarget is not VisualElement typeElement)
            {
                return;
            }

            VisualElement typesContainer = typeElement.parent;
            VisualElement namespaceGroup = typesContainer?.parent;
            if (namespaceGroup == null)
            {
                return;
            }

            _dataVisualizer.OnTypePointerDown(namespaceGroup, evt);
        }

        private void HandleTypePointerUp(PointerUpEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            if (_dataVisualizer._isDragging)
            {
                return;
            }

            if (evt.currentTarget is not VisualElement typeElement)
            {
                return;
            }

            Type selectedType = typeElement.userData as Type;
            if (selectedType == null)
            {
                return;
            }

            _namespaceController.SelectType(_dataVisualizer, selectedType);
            evt.StopPropagation();
        }

        private void HandleNamespaceRemoveRequested(
            string namespaceKey,
            IReadOnlyList<Type> typesToRemove,
            VisualElement trigger
        )
        {
            if (string.IsNullOrWhiteSpace(namespaceKey) || typesToRemove == null)
            {
                return;
            }

            int removableCount = typesToRemove.Count;
            if (removableCount == 0)
            {
                return;
            }

            List<Type> snapshot = new List<Type>(typesToRemove);
            _eventHub.Publish(new NamespaceRemovalRequestedEvent(namespaceKey, snapshot));

            _dataVisualizer.BuildAndOpenConfirmationPopover(
                $"Remove {removableCount} non-core type{(removableCount > 1 ? "s" : string.Empty)} from namespace '<color=yellow>{namespaceKey}</color>'?",
                "Remove",
                () => OnNamespaceRemovalConfirmed(namespaceKey, snapshot),
                trigger
            );
        }

        private void OnNamespaceRemovalConfirmed(
            string namespaceKey,
            IReadOnlyList<Type> typesToRemove
        )
        {
            List<Type> snapshot = new List<Type>(typesToRemove);
            _eventHub.Publish(new NamespaceRemovalConfirmedEvent(namespaceKey, snapshot));
        }

        private void HandleTypeRemoveRequested(Type type, VisualElement trigger)
        {
            if (type == null)
            {
                return;
            }

            _eventHub.Publish(new TypeRemovalRequestedEvent(type));

            _dataVisualizer.BuildAndOpenConfirmationPopover(
                $"Remove type '<color=yellow><i>{type.Name}</i></color>' from Data Visualizer?",
                "Remove",
                () => OnTypeRemovalConfirmed(type),
                trigger
            );
        }

        private void OnTypeRemovalConfirmed(Type type)
        {
            if (type == null)
            {
                return;
            }

            _eventHub.Publish(new TypeRemovalConfirmedEvent(type));
        }

        private bool NamespaceStructureChanged()
        {
            List<string> desiredNamespaces = BuildDesiredNamespaceOrder();
            if (desiredNamespaces.Count != _lastNamespaceOrder.Count)
            {
                return true;
            }

            for (int index = 0; index < desiredNamespaces.Count; index++)
            {
                if (
                    !string.Equals(
                        desiredNamespaces[index],
                        _lastNamespaceOrder[index],
                        StringComparison.Ordinal
                    )
                )
                {
                    return true;
                }
            }

            foreach (string namespaceKey in desiredNamespaces)
            {
                List<string> desiredTypes = BuildDesiredTypeOrder(namespaceKey);
                if (
                    !_lastNamespaceTypeOrders.TryGetValue(
                        namespaceKey,
                        out List<string> previousTypes
                    )
                )
                {
                    return true;
                }

                if (desiredTypes.Count != previousTypes.Count)
                {
                    return true;
                }

                for (int index = 0; index < desiredTypes.Count; index++)
                {
                    if (
                        !string.Equals(
                            desiredTypes[index],
                            previousTypes[index],
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CacheNamespaceStructure()
        {
            List<string> desiredNamespaces = BuildDesiredNamespaceOrder();
            _lastNamespaceOrder.Clear();
            _lastNamespaceOrder.AddRange(desiredNamespaces);

            _lastNamespaceTypeOrders.Clear();
            foreach (string namespaceKey in desiredNamespaces)
            {
                List<string> desiredTypes = BuildDesiredTypeOrder(namespaceKey);
                _lastNamespaceTypeOrders[namespaceKey] = desiredTypes;
            }
        }

        private List<string> BuildDesiredNamespaceOrder()
        {
            Dictionary<string, int> resolvedOrder = new Dictionary<string, int>(
                _dataVisualizer.GetNamespaceOrderSnapshot(),
                StringComparer.Ordinal
            );

            foreach (string namespaceKey in _dataVisualizer.GetNamespaceKeys())
            {
                if (!resolvedOrder.ContainsKey(namespaceKey))
                {
                    resolvedOrder[namespaceKey] = int.MaxValue;
                }
            }

            List<KeyValuePair<string, int>> orderedPairs = new List<KeyValuePair<string, int>>(
                resolvedOrder
            );
            orderedPairs.Sort(
                (left, right) =>
                {
                    int comparison = left.Value.CompareTo(right.Value);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    return string.Compare(left.Key, right.Key, StringComparison.Ordinal);
                }
            );

            List<string> result = new List<string>(orderedPairs.Count);
            for (int index = 0; index < orderedPairs.Count; index++)
            {
                result.Add(orderedPairs[index].Key);
            }

            return result;
        }

        private List<string> BuildDesiredTypeOrder(string namespaceKey)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return new List<string>();
            }

            IReadOnlyList<Type> types = _dataVisualizer.GetTypesForNamespace(namespaceKey);
            List<string> ordered = new List<string>(types.Count);
            for (int index = 0; index < types.Count; index++)
            {
                Type type = types[index];
                if (type == null)
                {
                    continue;
                }

                ordered.Add(type.FullName);
            }

            return ordered;
        }

        private void SynchronizeNamespaceState()
        {
            IReadOnlyCollection<string> collapsed = _sessionState.Selection.CollapsedNamespaces;
            if (collapsed == null || collapsed.Count == 0)
            {
                return;
            }

            foreach (string namespaceKey in collapsed)
            {
                if (string.IsNullOrWhiteSpace(namespaceKey))
                {
                    continue;
                }

                VisualElement container = _dataVisualizer._namespaceListContainer;
                VisualElement namespaceGroup = container?.Q<VisualElement>(
                    $"namespace-group-{namespaceKey}"
                );
                if (namespaceGroup == null)
                {
                    continue;
                }

                Label indicator = namespaceGroup.Q<Label>($"namespace-indicator-{namespaceKey}");
                VisualElement typesContainer = namespaceGroup.Q<VisualElement>(
                    $"types-container-{namespaceKey}"
                );
                if (indicator == null || typesContainer == null)
                {
                    continue;
                }

                NamespaceController.ApplyNamespaceCollapsedState(
                    _dataVisualizer,
                    indicator,
                    typesContainer,
                    true,
                    false
                );
            }
        }
    }
}
