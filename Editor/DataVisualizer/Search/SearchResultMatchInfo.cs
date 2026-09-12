namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class SearchResultMatchInfo
    {
        public bool MatchInPrimaryField
        {
            get
            {
                return matchedFields.Exists(f =>
                    string.Equals(
                        f.fieldName,
                        MatchSource.ObjectName,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        f.fieldName,
                        MatchSource.TypeName,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        f.fieldName,
                        MatchSource.Guid,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            }
        }

        public IEnumerable<string> AllMatchedTerms
        {
            get
            {
                using ReusableDisposalLease<HashSet<string>> cleanup =
                    MatchedTermSetPool.Shared.Acquire(out HashSet<string> seenTerms);
                foreach (MatchDetail matchDetail in matchedFields)
                {
                    foreach (string matchedTerm in matchDetail.matchedTerms)
                    {
                        if (seenTerms.Add(matchedTerm))
                        {
                            yield return matchedTerm;
                        }
                    }
                }
            }
        }

        public bool isMatch;
        public readonly List<MatchDetail> matchedFields = new();
    }
}
