namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    // A metadata snapshot keeps arriving records from moving an in-flight cursor.
    public sealed class AssetSearchQuery
    {
        public bool IsComplete { get; private set; }
        public bool ReachedLimit => _maximumResults <= _results.Count && ScannedCount < _documents.Length;
        public int ScannedCount { get; private set; }
        public int RecordCount => _documents.Length;
        public double LongestSliceMilliseconds { get; private set; }
        public int SliceOverrunCount { get; private set; }
        public IReadOnlyList<(AssetSearchDocument Document, SearchResultMatchInfo Match)> Results => _results;

        private readonly AssetSearchDocument[] _documents;
        private readonly string[] _terms;
        private readonly string[] _normalizedTerms;
        private readonly int _maximumResults;
        private readonly List<(AssetSearchDocument Document, SearchResultMatchInfo Match)> _results = new();

        public AssetSearchQuery(IReadOnlyList<AssetSearchDocument> documents, string[] terms, int maximumResults)
        {
            if (documents == null)
            {
                throw new ArgumentNullException(nameof(documents));
            }
            _documents = new AssetSearchDocument[documents.Count];
            for (int index = 0; index < documents.Count; index++)
            {
                _documents[index] = documents[index];
            }
            _terms = terms == null ? Array.Empty<string>() : (string[])terms.Clone();
            _normalizedTerms = new string[_terms.Length];
            for (int index = 0; index < _terms.Length; index++)
            {
                _normalizedTerms[index] = AssetSearchDocument.NormalizeAsciiOrNull(_terms[index]);
            }
            _maximumResults = 0 < maximumResults ? maximumResults : throw new ArgumentOutOfRangeException(nameof(maximumResults));
        }

        public void Advance(double budgetMilliseconds = 4)
        {
            if (double.IsNaN(budgetMilliseconds) || budgetMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            }
            if (IsComplete)
            {
                return;
            }
            Stopwatch clock = Stopwatch.StartNew();
            while (ScannedCount < _documents.Length)
            {
                AssetSearchDocument document = _documents[ScannedCount++];
                SearchResultMatchInfo match = MatchOrNull(document);
                if (match != null)
                {
                    _results.Add((document, match));
                }
                if (_maximumResults <= _results.Count || budgetMilliseconds <= clock.Elapsed.TotalMilliseconds)
                {
                    break;
                }
            }
            IsComplete = _documents.Length <= ScannedCount || _maximumResults <= _results.Count;
            double elapsed = clock.Elapsed.TotalMilliseconds;
            LongestSliceMilliseconds = Math.Max(LongestSliceMilliseconds, elapsed);
            if (budgetMilliseconds < elapsed)
            {
                SliceOverrunCount++;
            }
        }

        private SearchResultMatchInfo MatchOrNull(AssetSearchDocument document)
        {
            if (document.NormalizedAsciiText != null)
            {
                bool possibleMatch = false;
                foreach (string term in _normalizedTerms)
                {
                    if (term == null || document.NormalizedAsciiText.Contains(term, StringComparison.Ordinal))
                    {
                        possibleMatch = true;
                        break;
                    }
                }
                if (!possibleMatch)
                {
                    return null;
                }
            }
            // Non-ASCII input keeps the runtime's full OrdinalIgnoreCase semantics.
            return document.MatchOrNull(_terms);
        }
    }
}
