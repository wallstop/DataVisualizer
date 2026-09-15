namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class ConsoleCapture : IDisposable
    {
        public IReadOnlyList<(string message, LogType type)> Entries => _entries;

        private readonly List<(string message, LogType type)> _entries = new();

        public ConsoleCapture()
        {
            Application.logMessageReceived += OnLogMessageReceived;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
        }

        public bool Contains(string fragment)
        {
            foreach ((string message, _) in _entries)
            {
                if (message.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            _entries.Add((condition, type));
        }
    }
}
