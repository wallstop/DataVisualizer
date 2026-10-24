namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    // A metadata snapshot keeps arriving records from moving an in-flight cursor.
    public sealed class AssetSearchQuery
    {
        public bool IsComplete { get; private set; }
        public bool IsPreparing => !_ordering.IsComplete;
        public bool ReachedLimit => _maximumResults <= _results.Count && ScannedCount < _documents.Count;
        public int ScannedCount { get; private set; }
        public int RecordCount => _documents.Count;
        public double LongestSliceMilliseconds { get; private set; }
        public int SliceOverrunCount { get; private set; }
        public IReadOnlyList<(AssetSearchDocument Document, SearchResultMatchInfo Match)> Results => _results;

        private readonly AssetSearchOrdering _ordering;
        private IReadOnlyList<AssetSearchDocument> _documents;
        private readonly string[] _terms;
        private readonly string[] _normalizedTerms;
        private readonly string[] _normalizedOrdinalTerms;
        private readonly int _maximumResults;
        private readonly List<(AssetSearchDocument Document, SearchResultMatchInfo Match)> _results = new();

        public AssetSearchQuery(IReadOnlyList<AssetSearchDocument> documents, string[] terms, int maximumResults)
            : this(documents, terms, maximumResults, sortByIdentity: false) { }

        public AssetSearchQuery(IReadOnlyList<AssetSearchDocument> documents, string[] terms, int maximumResults, bool sortByIdentity)
        {
            if (maximumResults <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResults));
            }
            _ordering = documents is AssetSearchSnapshot snapshot ? snapshot.GetOrdering(sortByIdentity) : new AssetSearchOrdering(documents, sortByIdentity);
            _documents = _ordering.Documents;
            _terms = terms == null ? Array.Empty<string>() : (string[])terms.Clone();
            _normalizedTerms = new string[_terms.Length];
            _normalizedOrdinalTerms = new string[_terms.Length];
            for (int index = 0; index < _terms.Length; index++)
            {
                _normalizedTerms[index] = AssetSearchDocument.NormalizeAsciiOrNull(_terms[index]);
                _normalizedOrdinalTerms[index] = AssetSearchDocument.NormalizeOrdinalOrNull(_terms[index]);
            }
            _maximumResults = maximumResults;
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
            if (IsPreparing)
            {
                _ordering.Advance(budgetMilliseconds);
                _documents = _ordering.Documents;
                if (IsPreparing || budgetMilliseconds <= clock.Elapsed.TotalMilliseconds)
                {
                    RecordSlice(clock.Elapsed.TotalMilliseconds, budgetMilliseconds);
                    return;
                }
            }
            // Another query may have completed the shared preparation since this query last ran.
            _documents = _ordering.Documents;
            while (ScannedCount < _documents.Count)
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
            IsComplete = _documents.Count <= ScannedCount || _maximumResults <= _results.Count;
            RecordSlice(clock.Elapsed.TotalMilliseconds, budgetMilliseconds);
        }

        private void RecordSlice(double elapsed, double budgetMilliseconds)
        {
            LongestSliceMilliseconds = Math.Max(LongestSliceMilliseconds, elapsed);
            if (budgetMilliseconds < elapsed)
            {
                SliceOverrunCount++;
            }
        }

        private SearchResultMatchInfo MatchOrNull(AssetSearchDocument document)
        {
            string normalizedText = document.NormalizedAsciiText ?? document.NormalizedOrdinalText;
            if (normalizedText != null)
            {
                string[] normalizedTerms = document.NormalizedAsciiText != null ? _normalizedTerms : _normalizedOrdinalTerms;
                bool possibleMatch = false;
                foreach (string term in normalizedTerms)
                {
                    if (term == null || normalizedText.Contains(term, StringComparison.Ordinal))
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
            // Original strings preserve match sources, GUID equality and runtime casing.
            return document.MatchOrNull(_terms);
        }
    }
}
