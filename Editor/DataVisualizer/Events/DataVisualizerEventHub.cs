namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;
    using System.Collections.Generic;

    internal sealed class DataVisualizerEventHub
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Type eventType = typeof(TEvent);
            List<Delegate> listeners;
            if (!_handlers.TryGetValue(eventType, out listeners))
            {
                listeners = new List<Delegate>();
                _handlers[eventType] = listeners;
            }

            listeners.Add(handler);
            return new Subscription<TEvent>(this, handler);
        }

        public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class
        {
            if (handler == null)
            {
                return;
            }

            Type eventType = typeof(TEvent);
            if (!_handlers.TryGetValue(eventType, out List<Delegate> listeners))
            {
                return;
            }

            listeners.Remove(handler);
            if (listeners.Count == 0)
            {
                _handlers.Remove(eventType);
            }
        }

        public void Publish<TEvent>(TEvent eventData) where TEvent : class
        {
            Type eventType = typeof(TEvent);
            if (!_handlers.TryGetValue(eventType, out List<Delegate> listeners))
            {
                return;
            }

            List<Delegate> snapshot = new List<Delegate>(listeners);
            for (int index = 0; index < snapshot.Count; index++)
            {
                Delegate listener = snapshot[index];
                Action<TEvent> typedHandler = listener as Action<TEvent>;
                if (typedHandler != null)
                {
                    typedHandler(eventData);
                }
            }
        }

        public void Clear()
        {
            _handlers.Clear();
        }

        private sealed class Subscription<TEvent> : IDisposable where TEvent : class
        {
            private readonly DataVisualizerEventHub _hub;
            private Action<TEvent> _handler;

            public Subscription(DataVisualizerEventHub hub, Action<TEvent> handler)
            {
                _hub = hub ?? throw new ArgumentNullException(nameof(hub));
                _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            }

            public void Dispose()
            {
                Action<TEvent> handler = _handler;
                if (handler == null)
                {
                    return;
                }

                _hub.Unsubscribe(handler);
                _handler = null;
            }
        }
    }
}
