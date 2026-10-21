namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;

    /// <summary>A configuration rejection that occurs before preference mutation.</summary>
    public sealed class EditorConfigurationException : InvalidOperationException
    {
        public string Code { get; }

        public EditorConfigurationException(string code, string message)
            : base(message)
        {
            Code = code;
        }
    }
}
