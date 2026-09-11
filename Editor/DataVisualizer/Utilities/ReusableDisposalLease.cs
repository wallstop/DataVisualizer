namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// A copy-safe cleanup claim. All copies refer to the same owner slot and generation.
    /// </summary>
    public readonly struct ReusableDisposalLease<TState> : IDisposable
    {
        private readonly ReusableDisposalScope<TState> _owner;
        private readonly long _generation;
        private readonly int _slot;

        internal ReusableDisposalLease(
            ReusableDisposalScope<TState> owner,
            int slot,
            long generation
        )
        {
            _owner = owner;
            _generation = generation;
            _slot = slot;
        }

        /// <summary>Runs the cleanup once; default, stale, and duplicate leases are no-ops.</summary>
        public void Dispose()
        {
            _owner?.Release(_slot, _generation);
        }
    }
#endif
}
