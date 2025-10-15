namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using State;
    using UnityEngine.UIElements;

    internal sealed class NamespacePanelController
    {
        private readonly DataVisualizer _dataVisualizer;
        private readonly NamespaceController _namespaceController;
        private readonly VisualizerSessionState _sessionState;

        public NamespacePanelController(
            DataVisualizer dataVisualizer,
            NamespaceController namespaceController,
            VisualizerSessionState sessionState
        )
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _namespaceController =
                namespaceController ?? throw new ArgumentNullException(nameof(namespaceController));
            _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        }

        public void BuildNamespaceView()
        {
            VisualElement container = _dataVisualizer._namespaceListContainer;
            _namespaceController.Build(_dataVisualizer, ref container);
            _dataVisualizer._namespaceListContainer = container;
        }
    }
}
