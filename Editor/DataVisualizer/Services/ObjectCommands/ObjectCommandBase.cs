namespace WallstopStudios.DataVisualizer.Editor.Services.ObjectCommands
{
    using System;
    using Events;

    internal abstract class ObjectCommandBase<TEvent> : IObjectCommand
        where TEvent : class
    {
        protected ObjectCommandBase(DataVisualizer dataVisualizer)
        {
            DataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
        }

        protected DataVisualizer DataVisualizer { get; }

        public IDisposable Subscribe(DataVisualizerEventHub eventHub)
        {
            if (eventHub == null)
            {
                throw new ArgumentNullException(nameof(eventHub));
            }

            return eventHub.Subscribe<TEvent>(Execute);
        }

        protected abstract void Execute(TEvent evt);
    }
}
