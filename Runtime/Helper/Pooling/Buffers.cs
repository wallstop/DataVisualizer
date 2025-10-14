namespace WallstopStudios.DataVisualizer.Helper.Pooling
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;

    internal static class Buffers<T>
    {
        public static readonly WallstopGenericPool<List<T>> List = new(
            () => new List<T>(),
            onRelease: list => list.Clear()
        );

        public static readonly WallstopGenericPool<HashSet<T>> HashSet = new(
            () => new HashSet<T>(),
            onRelease: set => set.Clear()
        );

        public static PooledResource<List<T>> GetList(int capacity, out List<T> list)
        {
            PooledResource<List<T>> resource = List.Get(out list);
            if (list.Capacity < capacity)
            {
                list.Capacity = capacity;
            }

            return resource;
        }
    }

    internal static class WallstopArrayPool<T>
    {
        private static readonly ConcurrentDictionary<int, ConcurrentStack<T[]>> Pool = new();

        public static PooledResource<T[]> Get(int size)
        {
            return Get(size, out _);
        }

        public static PooledResource<T[]> Get(int size, out T[] array)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if (size == 0)
            {
                array = Array.Empty<T>();
                return new PooledResource<T[]>(array, _ => { });
            }

            ConcurrentStack<T[]> stack = Pool.GetOrAdd(size, _ => new ConcurrentStack<T[]>());
            if (!stack.TryPop(out array))
            {
                array = new T[size];
            }

            return new PooledResource<T[]>(
                array,
                value =>
                {
                    Array.Clear(value, 0, value.Length);
                    stack.Push(value);
                }
            );
        }
    }

    internal static class StopwatchBuffers
    {
        public static readonly WallstopGenericPool<Stopwatch> Stopwatch = new(
            () => System.Diagnostics.Stopwatch.StartNew(),
            onGet: sw => sw.Restart(),
            onRelease: sw => sw.Stop()
        );
    }
}
