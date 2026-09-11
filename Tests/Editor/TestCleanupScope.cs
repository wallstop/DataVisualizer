namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class TestCleanupScope : IDisposable
    {
        private readonly Stack<Action> _cleanupActions = new();

        private bool _isDisposed;

        public void Defer(Action cleanup)
        {
            if (cleanup == null)
            {
                throw new ArgumentNullException(nameof(cleanup));
            }

            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(TestCleanupScope));
            }

            _cleanupActions.Push(cleanup);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            while (0 < _cleanupActions.Count)
            {
                Action cleanup = _cleanupActions.Pop();
                try
                {
                    cleanup();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
