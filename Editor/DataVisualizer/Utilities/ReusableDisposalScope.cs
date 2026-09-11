namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using UnityEngine;

    /// <summary>
    /// Owns reusable cleanup slots and returns copy-safe value-type leases for <c>using</c> scopes.
    /// </summary>
    /// <typeparam name="TState">State passed to the cleanup callback.</typeparam>
    public sealed class ReusableDisposalScope<TState>
    {
        private const int InitialCapacity = 4;
        private const int NoSlot = -1;

        private readonly Action<TState> _cleanup;
        private readonly object _gate = new();

        private TState[] _states = new TState[InitialCapacity];
        private long[] _generations = new long[InitialCapacity];
        private int[] _nextFreeSlots = new int[InitialCapacity];
        private bool[] _activeSlots = new bool[InitialCapacity];
        private long _nextGeneration;
        private int _nextUnusedSlot;
        private int _freeSlot = NoSlot;

        /// <summary>
        /// Creates a reusable scope owner. Supply a non-capturing callback for allocation-free
        /// leases after the slot array reaches its maximum concurrent depth.
        /// </summary>
        public ReusableDisposalScope(Action<TState> cleanup)
        {
            _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
        }

        /// <summary>Acquires a lease that invokes the configured cleanup exactly once.</summary>
        public ReusableDisposalLease<TState> Acquire(TState state)
        {
            lock (_gate)
            {
                int slot;
                if (0 <= _freeSlot)
                {
                    slot = _freeSlot;
                    _freeSlot = _nextFreeSlots[slot];
                }
                else
                {
                    slot = _nextUnusedSlot;
                    _nextUnusedSlot++;
                    if (_states.Length <= slot)
                    {
                        int newCapacity = _states.Length * 2;
                        Array.Resize(ref _states, newCapacity);
                        Array.Resize(ref _generations, newCapacity);
                        Array.Resize(ref _nextFreeSlots, newCapacity);
                        Array.Resize(ref _activeSlots, newCapacity);
                    }
                }

                long generation = ++_nextGeneration;
                _states[slot] = state;
                _generations[slot] = generation;
                _nextFreeSlots[slot] = NoSlot;
                _activeSlots[slot] = true;
                return new ReusableDisposalLease<TState>(this, slot, generation);
            }
        }

        internal void Release(int slot, long generation)
        {
            TState state;
            lock (_gate)
            {
                if (slot < 0 || _nextUnusedSlot <= slot)
                {
                    return;
                }

                if (!_activeSlots[slot] || _generations[slot] != generation)
                {
                    return;
                }

                state = _states[slot];
                _states[slot] = default;
                _generations[slot] = default;
                _activeSlots[slot] = false;
                _nextFreeSlots[slot] = _freeSlot;
                _freeSlot = slot;
            }

            try
            {
                _cleanup(state);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
#endif
}
