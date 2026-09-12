namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;

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
                int fieldIndex = 0;
                foreach (MatchDetail matchDetail in matchedFields)
                {
                    int termIndex = 0;
                    foreach (string matchedTerm in matchDetail.matchedTerms)
                    {
                        if (IsFirstOccurrence(matchedTerm, fieldIndex, termIndex))
                        {
                            yield return matchedTerm;
                        }

                        termIndex++;
                    }

                    fieldIndex++;
                }
            }
        }

        public bool isMatch;
        public readonly List<MatchDetail> matchedFields = new();

        private bool IsFirstOccurrence(string term, int fieldIndex, int termIndex)
        {
            for (int previousFieldIndex = 0; previousFieldIndex <= fieldIndex; previousFieldIndex++)
            {
                List<string> terms = matchedFields[previousFieldIndex].matchedTerms;
                int termsToCheck = previousFieldIndex == fieldIndex ? termIndex : terms.Count;
                for (
                    int previousTermIndex = 0;
                    previousTermIndex < termsToCheck;
                    previousTermIndex++
                )
                {
                    if (
                        string.Equals(
                            terms[previousTermIndex],
                            term,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
