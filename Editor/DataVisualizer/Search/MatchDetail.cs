namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System.Collections.Generic;

    public sealed class MatchDetail
    {
        public string fieldName = string.Empty;
        public string matchedValue = string.Empty;
        public readonly List<string> matchedTerms = new();

        public MatchDetail(params string[] terms)
        {
            matchedTerms.AddRange(terms);
        }
    }
}
