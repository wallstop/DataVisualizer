namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    // Own the snapshot so arrivals cannot move a cursor during a merge.
    public sealed class AssetSearchOrdering
    {
        private const int StepsPerBudgetCheck = 64;

        public bool IsComplete => _width == 0;
        public IReadOnlyList<AssetSearchDocument> Documents => _documents;

        private IReadOnlyList<AssetSearchDocument> _documents;
        private AssetSearchDocument[] _buffer;
        private int _width;
        private int _start;
        private int _left;
        private int _right;
        private int _leftEnd;
        private int _end;
        private int _output;

        public AssetSearchOrdering(IReadOnlyList<AssetSearchDocument> documents, bool sortByIdentity = true)
        {
            if (documents == null)
            {
                throw new ArgumentNullException(nameof(documents));
            }
            if (documents is AssetSearchSnapshot)
            {
                _documents = documents;
            }
            else if (documents is List<AssetSearchDocument> list)
            {
                _documents = list.ToArray();
            }
            else
            {
                AssetSearchDocument[] copy = new AssetSearchDocument[documents.Count];
                for (int index = 0; index < documents.Count; index++)
                {
                    copy[index] = documents[index];
                }
                _documents = copy;
            }
            if (sortByIdentity && 1 < _documents.Count)
            {
                _width = 1;
            }
        }

        public void Advance(double budgetMilliseconds = 4)
        {
            if (double.IsNaN(budgetMilliseconds) || budgetMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            }
            Stopwatch clock = Stopwatch.StartNew();
            if (!IsComplete && _buffer == null)
            {
                _buffer = new AssetSearchDocument[_documents.Count];
            }
            int steps = budgetMilliseconds == 0 ? 1 : StepsPerBudgetCheck;
            while (!IsComplete)
            {
                for (int step = 0; step < steps && !IsComplete; step++)
                {
                    MoveNext();
                }
                if (budgetMilliseconds <= clock.Elapsed.TotalMilliseconds)
                {
                    return;
                }
            }
        }

        // Merge one document at a time, including within the final large merge.
        private void MoveNext()
        {
            if (_output == _end)
            {
                if (_start == _documents.Count)
                {
                    AssetSearchDocument[] previous = _documents as AssetSearchDocument[];
                    _documents = _buffer;
                    _buffer = previous;
                    if (_documents.Count - _width <= _width)
                    {
                        _width = 0;
                        _buffer = null;
                        return;
                    }
                    _width *= 2;
                    _start = 0;
                    // The first merge can read an immutable cache view without copying it.
                    _buffer ??= new AssetSearchDocument[_documents.Count];
                }
                _left = _start;
                _right = Math.Min(_start + _width, _documents.Count);
                _leftEnd = _right;
                _end = Math.Min(_right + _width, _documents.Count);
                _output = _start;
                _start = _end;
            }
            bool takeLeft = _left < _leftEnd && (_end <= _right || AssetSearchDocument.CompareIdentity(_documents[_left], _documents[_right]) <= 0);
            _buffer[_output++] = _documents[takeLeft ? _left++ : _right++];
        }
    }
}
