namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    internal readonly struct EditorSurfaceCaptureResult
    {
        internal string OutputPath { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal int ByteCount { get; }

        internal int DistinctColorCount { get; }

        internal EditorSurfaceCaptureResult(
            string outputPath,
            int width,
            int height,
            int byteCount,
            int distinctColorCount
        )
        {
            OutputPath = outputPath;
            Width = width;
            Height = height;
            ByteCount = byteCount;
            DistinctColorCount = distinctColorCount;
        }

        public override string ToString()
        {
            return $"{OutputPath} {Width}x{Height} bytes={ByteCount} "
                + $"distinctColors={DistinctColorCount}";
        }
    }
}
