namespace WallstopStudios.DataVisualizer.Editor.Services.ObjectCommands
{
    using System;
    using System.Collections.Generic;
    using Events;

    internal sealed class ObjectCommandDispatcher : IDisposable
    {
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        public ObjectCommandDispatcher(
            DataVisualizer dataVisualizer,
            DataVisualizerEventHub eventHub
        )
        {
            if (dataVisualizer == null)
            {
                throw new ArgumentNullException(nameof(dataVisualizer));
            }

            if (eventHub == null)
            {
                throw new ArgumentNullException(nameof(eventHub));
            }

            IObjectCommand[] commands =
            {
                new MoveObjectToTopCommand(dataVisualizer),
                new MoveObjectToBottomCommand(dataVisualizer),
                new CloneObjectCommand(dataVisualizer),
                new RenameObjectCommand(dataVisualizer),
                new MoveObjectCommand(dataVisualizer),
                new ReorderObjectCommand(dataVisualizer),
                new DeleteObjectCommand(dataVisualizer),
            };

            for (int index = 0; index < commands.Length; index++)
            {
                IObjectCommand command = commands[index];
                IDisposable subscription = command.Subscribe(eventHub);
                _subscriptions.Add(subscription);
            }
        }

        public void Dispose()
        {
            for (int index = 0; index < _subscriptions.Count; index++)
            {
                IDisposable subscription = _subscriptions[index];
                subscription?.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}
