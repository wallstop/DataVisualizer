namespace WallstopStudios.DataVisualizer.Helper.Pooling
{
    using System;
    using System.Collections.Concurrent;

    internal sealed class WallstopGenericPool<T> : IDisposable
    {
        public int Count => _pool.Count;

        private readonly Func<T> _producer;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDispose;
        private readonly ConcurrentStack<T> _pool = new();

        public WallstopGenericPool(
            Func<T> producer,
            int preWarmCount = 0,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            Action<T> onDispose = null
        )
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
            _onGet = onGet;
            _onRelease = onRelease;
            _onDispose = onDispose;

            for (int i = 0; i < preWarmCount; ++i)
            {
                T value = _producer();
                _onGet?.Invoke(value);
                Return(value);
            }
        }

        public PooledResource<T> Get()
        {
            return Get(out _);
        }

        public PooledResource<T> Get(out T value)
        {
            if (!_pool.TryPop(out value))
            {
                value = _producer();
            }

            _onGet?.Invoke(value);
            return new PooledResource<T>(value, Return);
        }

        public void Dispose()
        {
            if (_onDispose == null)
            {
                _pool.Clear();
                return;
            }

            while (_pool.TryPop(out T value))
            {
                _onDispose(value);
            }
        }

        private void Return(T instance)
        {
            _onRelease?.Invoke(instance);
            _pool.Push(instance);
        }
    }
}
