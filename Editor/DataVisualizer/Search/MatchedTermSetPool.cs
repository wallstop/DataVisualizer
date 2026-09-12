namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    internal sealed class MatchedTermSetPool
    {
        private const int MaxRetainedSetCount = 4;
        private const int MaxRetainedTermCount = 64;

        internal static MatchedTermSetPool Shared { get; } = new();

        private readonly Stack<HashSet<string>> _availableSets = new();
        private readonly ReusableDisposalScope<HashSet<string>> _cleanupScopes;

        private MatchedTermSetPool()
        {
            _cleanupScopes = new ReusableDisposalScope<HashSet<string>>(Release);
        }

        internal ReusableDisposalLease<HashSet<string>> Acquire(out HashSet<string> matchedTerms)
        {
            HashSet<string> acquiredTerms;
            lock (_availableSets)
            {
                acquiredTerms =
                    0 < _availableSets.Count
                        ? _availableSets.Pop()
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            ReusableDisposalLease<HashSet<string>> cleanup = _cleanupScopes.Acquire(acquiredTerms);
            matchedTerms = acquiredTerms;
            return cleanup;
        }

        private void Release(HashSet<string> matchedTerms)
        {
            bool retain = matchedTerms.Count <= MaxRetainedTermCount;
            matchedTerms.Clear();
            if (!retain)
            {
                return;
            }

            lock (_availableSets)
            {
                if (_availableSets.Count < MaxRetainedSetCount)
                {
                    _availableSets.Push(matchedTerms);
                }
            }
        }
    }
}
