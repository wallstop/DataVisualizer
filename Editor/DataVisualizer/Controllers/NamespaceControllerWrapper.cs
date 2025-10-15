namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
    using UnityEngine.UIElements;

    internal sealed class NamespaceControllerWrapper
    {
        private readonly NamespaceController _namespaceController;

        public NamespaceControllerWrapper(NamespaceController namespaceController)
        {
            _namespaceController = namespaceController
                ?? throw new ArgumentNullException(nameof(namespaceController));
        }

        public event Action<Type> TypeSelected
        {
            add => _namespaceController.TypeSelected += value;
            remove => _namespaceController.TypeSelected -= value;
        }

        public Type SelectedType => _namespaceController.SelectedType;

        public bool TryGet(Type type, out VisualElement element)
        {
            return _namespaceController.TryGet(type, out element);
        }

        public void Build(DataVisualizer dataVisualizer, ref VisualElement namespaceListContainer)
        {
            _namespaceController.Build(dataVisualizer, ref namespaceListContainer);
        }

        public void ApplyNamespaceCollapsedState(
            DataVisualizer dataVisualizer,
            Label indicator,
            VisualElement typesContainer,
            bool collapsed,
            bool saveState
        )
        {
            _namespaceController.ApplyNamespaceCollapsedState(
                dataVisualizer,
                indicator,
                typesContainer,
                collapsed,
                saveState
            );
        }

        public void SetIsNamespaceCollapsed(
            DataVisualizer dataVisualizer,
            string namespaceKey,
            bool isCollapsed
        )
        {
            NamespaceController.SetIsNamespaceCollapsed(dataVisualizer, namespaceKey, isCollapsed);
        }

        public bool GetIsNamespaceCollapsed(DataVisualizer dataVisualizer, string namespaceKey)
        {
            return NamespaceController.GetIsNamespaceCollapsed(dataVisualizer, namespaceKey);
        }

        public void SelectType(DataVisualizer dataVisualizer, Type type)
        {
            _namespaceController.SelectType(dataVisualizer, type);
        }

        public IEnumerable<KeyValuePair<string, List<Type>>> GetManagedNamespaces()
        {
            foreach (
                KeyValuePair<string, List<Type>> entry in _namespaceController._managedTypes
            )
            {
                yield return entry;
            }
        }
    }
}
