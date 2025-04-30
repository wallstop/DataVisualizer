namespace WallstopStudios.Editor.DataVisualizer.Search
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class SearchResultMatchInfo
    {
        public bool IsMatch { get; set; } = false; // Did object match ALL search terms?
        public List<MatchDetail> MatchedFields { get; private set; } = new List<MatchDetail>(); // List of all fields where any term matched

        // Helper property to quickly check if a match occurred in primary identifiers
        public bool MatchInPrimaryField
        {
            get
            {
                return MatchedFields.Exists(f =>
                    string.Equals(
                        f.FieldName,
                        MatchSource.ObjectName,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        f.FieldName,
                        MatchSource.TypeName,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        f.FieldName,
                        MatchSource.GUID,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            }
        }

        // Helper to get all unique terms that were found across all matched fields for this object
        public IEnumerable<string> AllMatchedTerms
        {
            get
            {
                // Use SelectMany to flatten the lists of terms from each detail, then Distinct
                return MatchedFields
                    .SelectMany(mf => mf.MatchedTerms)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
