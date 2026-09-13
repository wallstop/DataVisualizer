namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System.Collections.Generic;

    public sealed class MatchDetail
    {
        public IReadOnlyList<string> MatchedTerms => matchedTerms;

        public string fieldName = string.Empty;
        public string matchedValue = string.Empty;

        private readonly List<string> matchedTerms = new();

        public MatchDetail(params string[] terms)
        {
            matchedTerms.AddRange(terms);
        }

        public void AddMatchedTerm(string term)
        {
            matchedTerms.Add(term);
        }
    }
}
