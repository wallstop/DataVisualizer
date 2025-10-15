namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using Events;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class ObjectCommandService
    {
        private readonly DataVisualizer _dataVisualizer;
        private readonly DataVisualizerEventHub _eventHub;

        public ObjectCommandService(DataVisualizer dataVisualizer, DataVisualizerEventHub eventHub)
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
        }

        public void MoveToTop(Button button)
        {
            ScriptableObject dataObject = ResolveDataObject(button);
            if (dataObject == null)
            {
                return;
            }

            _eventHub.Publish(new ObjectMoveToTopRequestedEvent(dataObject));
        }

        public void MoveToBottom(Button button)
        {
            ScriptableObject dataObject = ResolveDataObject(button);
            if (dataObject == null)
            {
                return;
            }

            _eventHub.Publish(new ObjectMoveToBottomRequestedEvent(dataObject));
        }

        public void Clone(Button button)
        {
            ScriptableObject dataObject = ResolveDataObject(button);
            if (dataObject == null)
            {
                return;
            }

            _eventHub.Publish(new ObjectCloneRequestedEvent(dataObject));
        }

        public void Rename(Button button)
        {
            ScriptableObject dataObject = ResolveDataObject(button);
            if (dataObject == null)
            {
                return;
            }

            _eventHub.Publish(new ObjectRenameRequestedEvent(button, dataObject));
        }

        public void Move(Button button)
        {
            ScriptableObject dataObject = ResolveDataObject(button);
            if (dataObject == null)
            {
                return;
            }

            _eventHub.Publish(new ObjectMoveRequestedEvent(button, dataObject));
        }

        public void Delete(Button button)
        {
            ScriptableObject dataObject = ResolveDataObject(button);
            if (dataObject == null)
            {
                return;
            }

            _eventHub.Publish(new ObjectDeleteRequestedEvent(button, dataObject));
        }

        private static ScriptableObject ResolveDataObject(Button button)
        {
            if (button == null)
            {
                return null;
            }

            return button.userData as ScriptableObject;
        }
    }
}
