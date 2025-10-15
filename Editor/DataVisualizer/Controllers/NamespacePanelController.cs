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
            VisualElement container = _dataVisualizer._namespaceListContainer;
            _namespaceController.Build(_dataVisualizer, ref container);
            _dataVisualizer._namespaceListContainer = container;
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
            _sessionState.Selection.SetNamespaceCollapsed(namespaceKey, collapse);
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
