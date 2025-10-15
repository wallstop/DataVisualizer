namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
    using Events;
    using State;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class NamespacePanelController : IDisposable
    {
        private readonly DataVisualizer _dataVisualizer;
        private readonly NamespaceController _namespaceController;
        private readonly VisualizerSessionState _sessionState;
        private readonly DataVisualizerEventHub _eventHub;
        private readonly List<string> _lastNamespaceOrder = new List<string>();
        private readonly Dictionary<string, List<string>> _lastNamespaceTypeOrders = new Dictionary<string, List<string>>(StringComparer.Ordinal);

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
        }

        public void Dispose()
        {
            _namespaceController.TypeSelected -= HandleTypeSelected;
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
        }

        public void ToggleNamespaceCollapse(string namespaceKey)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }

            if (!_dataVisualizer._namespaceOrder.TryGetValue(namespaceKey, out int order))
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
            string namespaceKey = selectedType != null
                ? NamespaceController.GetNamespaceKey(selectedType)
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

        private void PersistSelection(string namespaceKey, string typeFullName)
        {
            _dataVisualizer.PersistSettings(
                settings =>
                {
                    bool changed = false;
                    if (!string.Equals(settings.lastSelectedNamespaceKey, namespaceKey, StringComparison.Ordinal))
                    {
                        settings.lastSelectedNamespaceKey = namespaceKey;
                        changed = true;
                    }

                    if (!string.Equals(settings.lastSelectedTypeName, typeFullName, StringComparison.Ordinal))
                    {
                        settings.lastSelectedTypeName = typeFullName;
                        changed = true;
                    }

                    return changed;
                },
                userState =>
                {
                    bool changed = false;
                    if (!string.Equals(userState.lastSelectedNamespaceKey, namespaceKey, StringComparison.Ordinal))
                    {
                        userState.lastSelectedNamespaceKey = namespaceKey;
                        changed = true;
                    }

                    if (!string.Equals(userState.lastSelectedTypeName, typeFullName, StringComparison.Ordinal))
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

                Label indicator = namespaceGroup.Q<Label>($"namespace-indicator-{namespaceKey}");
                if (indicator != null)
                {
                    indicator.UnregisterCallback<PointerDownEvent>(HandleIndicatorPointerDown);
                    indicator.RegisterCallback<PointerDownEvent>(HandleIndicatorPointerDown);
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

        private bool NamespaceStructureChanged()
        {
            List<string> desiredNamespaces = BuildDesiredNamespaceOrder();
            if (desiredNamespaces.Count != _lastNamespaceOrder.Count)
            {
                return true;
            }

            for (int index = 0; index < desiredNamespaces.Count; index++)
            {
                if (!string.Equals(desiredNamespaces[index], _lastNamespaceOrder[index], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (string namespaceKey in desiredNamespaces)
            {
                List<string> desiredTypes = BuildDesiredTypeOrder(namespaceKey);
                if (!_lastNamespaceTypeOrders.TryGetValue(namespaceKey, out List<string> previousTypes))
                {
                    return true;
                }

                if (desiredTypes.Count != previousTypes.Count)
                {
                    return true;
                }

                for (int index = 0; index < desiredTypes.Count; index++)
                {
                    if (!string.Equals(desiredTypes[index], previousTypes[index], StringComparison.Ordinal))
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
            Dictionary<string, int> resolvedOrder = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> entry in _dataVisualizer._namespaceOrder)
            {
                resolvedOrder[entry.Key] = entry.Value;
            }

            foreach (KeyValuePair<string, List<Type>> entry in _dataVisualizer._scriptableObjectTypes)
            {
                if (!resolvedOrder.ContainsKey(entry.Key))
                {
                    resolvedOrder[entry.Key] = int.MaxValue;
                }
            }

            List<KeyValuePair<string, int>> orderedPairs = new List<KeyValuePair<string, int>>(resolvedOrder);
            orderedPairs.Sort((left, right) =>
            {
                int comparison = left.Value.CompareTo(right.Value);
                if (comparison != 0)
                {
                    return comparison;
                }

                return string.Compare(left.Key, right.Key, StringComparison.Ordinal);
            });

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

            if (!_dataVisualizer._scriptableObjectTypes.TryGetValue(namespaceKey, out List<Type> types))
            {
                return new List<string>();
            }

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
