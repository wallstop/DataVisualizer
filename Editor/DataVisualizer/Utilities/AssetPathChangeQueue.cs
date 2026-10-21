namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System.Collections.Generic;

    // Copy callback inputs now; consume and coalesce paths on the owner's budgeted pump.
    public sealed class AssetPathChangeQueue
    {
        public int Count { get; private set; }

        private readonly Queue<string[]> _batches = new();
        private int _offset;

        public int Enqueue(string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return 0;
            }
            _batches.Enqueue((string[])paths.Clone());
            Count += paths.Length;
            return paths.Length;
        }

        public bool TryDequeue(out string path)
        {
            if (_batches.Count == 0)
            {
                path = null;
                return false;
            }
            string[] batch = _batches.Peek();
            string next = batch[_offset++];
            Count--;
            if (_offset == batch.Length)
            {
                _batches.Dequeue();
                _offset = 0;
            }
            path = next;
            return true;
        }

        public void Clear()
        {
            _batches.Clear();
            _offset = 0;
            Count = 0;
        }
    }
}
