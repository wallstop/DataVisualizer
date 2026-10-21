namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    // A metadata snapshot keeps arriving records from moving an in-flight cursor.
    public sealed class AssetSearchQuery
    {
        private const int SortStepsPerBudgetCheck = 64;

        public bool IsComplete { get; private set; }
        public bool IsPreparing => 0 < _sortWidth;
        public bool ReachedLimit => _maximumResults <= _results.Count && ScannedCount < _documents.Length;
        public int ScannedCount { get; private set; }
        public int RecordCount => _documents.Length;
        public double LongestSliceMilliseconds { get; private set; }
        public int SliceOverrunCount { get; private set; }
        public IReadOnlyList<(AssetSearchDocument Document, SearchResultMatchInfo Match)> Results => _results;

        private AssetSearchDocument[] _documents;
        private AssetSearchDocument[] _sortBuffer;
        private int _sortWidth;
        private int _mergeStart;
        private int _mergeLeft;
        private int _mergeRight;
        private int _mergeLeftEnd;
        private int _mergeEnd;
        private int _mergeOutput;
        private readonly string[] _terms;
        private readonly string[] _normalizedTerms;
        private readonly int _maximumResults;
        private readonly List<(AssetSearchDocument Document, SearchResultMatchInfo Match)> _results = new();

        public AssetSearchQuery(IReadOnlyList<AssetSearchDocument> documents, string[] terms, int maximumResults, bool sortByIdentity = false)
        {
            if (documents == null)
            {
                throw new ArgumentNullException(nameof(documents));
            }
            if (maximumResults <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResults));
            }
            if (documents is List<AssetSearchDocument> list)
            {
                _documents = list.ToArray();
            }
            else
            {
                _documents = new AssetSearchDocument[documents.Count];
                for (int index = 0; index < documents.Count; index++)
                {
                    _documents[index] = documents[index];
                }
            }
            if (sortByIdentity && 1 < _documents.Length)
            {
                _sortBuffer = new AssetSearchDocument[_documents.Length];
                _sortWidth = 1;
            }
            _terms = terms == null ? Array.Empty<string>() : (string[])terms.Clone();
            _normalizedTerms = new string[_terms.Length];
            for (int index = 0; index < _terms.Length; index++)
            {
                _normalizedTerms[index] = AssetSearchDocument.NormalizeAsciiOrNull(_terms[index]);
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
            int sortStepsPerCheck = budgetMilliseconds == 0 ? 1 : SortStepsPerBudgetCheck;
            while (IsPreparing)
            {
                for (int step = 0; step < sortStepsPerCheck && IsPreparing; step++)
                {
                    SortNextDocument();
                }
                if (budgetMilliseconds <= clock.Elapsed.TotalMilliseconds)
                {
                    RecordSlice(clock.Elapsed.TotalMilliseconds, budgetMilliseconds);
                    return;
                }
            }
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

        // Merge bounded groups of documents so even one large merge yields before it finishes.
        private void SortNextDocument()
        {
            if (_mergeOutput == _mergeEnd)
            {
                if (_mergeStart == _documents.Length)
                {
                    (_documents, _sortBuffer) = (_sortBuffer, _documents);
                    if (_documents.Length - _sortWidth <= _sortWidth)
                    {
                        _sortWidth = 0;
                        _sortBuffer = null;
                        return;
                    }
                    _sortWidth *= 2;
                    _mergeStart = 0;
                }
                _mergeLeft = _mergeStart;
                _mergeRight = Math.Min(_mergeStart + _sortWidth, _documents.Length);
                _mergeLeftEnd = _mergeRight;
                _mergeEnd = Math.Min(_mergeRight + _sortWidth, _documents.Length);
                _mergeOutput = _mergeStart;
                _mergeStart = _mergeEnd;
            }
            bool takeLeft = _mergeLeft < _mergeLeftEnd && (_mergeEnd <= _mergeRight || AssetSearchDocument.CompareIdentity(_documents[_mergeLeft], _documents[_mergeRight]) <= 0);
            _sortBuffer[_mergeOutput++] = _documents[takeLeft ? _mergeLeft++ : _mergeRight++];
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
