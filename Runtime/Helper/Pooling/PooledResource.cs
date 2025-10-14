namespace WallstopStudios.DataVisualizer.Helper.Pooling
{
    using System;

    internal struct PooledResource<T> : IDisposable
    {
        public T Resource { get; }

        private readonly Action<T> _onDispose;
        private bool _isInitialized;

        public PooledResource(T resource, Action<T> onDispose)
        {
            Resource = resource;
            _onDispose = onDispose;
            _isInitialized = true;
        }

        public void Dispose()
        {
            if (!_isInitialized)
            {
                return;
            }

            _isInitialized = false;
            _onDispose?.Invoke(Resource);
        }

        public static implicit operator T(PooledResource<T> pooled)
        {
            return pooled.Resource;
        }
    }
}
