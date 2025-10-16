namespace WallstopStudios.DataVisualizer.Editor.State
{
    using System;
    using System.Collections.Generic;
    using Data;

    public sealed class ProcessorPanelState
    {
        private readonly Dictionary<string, ProcessorState> _states =
            new Dictionary<string, ProcessorState>(StringComparer.Ordinal);
        private ProcessorState _activeState;
        private bool _isExecuting;
        private string _activeProcessorName = string.Empty;
        private int _activeObjectCount;
        private int _pendingExecutionCount;
        private double? _lastExecutionDurationSeconds;
        private string _lastExecutionError;

        public ProcessorState ActiveState
        {
            get
            {
                return _activeState;
            }
        }

        public bool IsExecuting
        {
            get
            {
                return _isExecuting;
            }
        }

        public string ActiveProcessorName
        {
            get
            {
                return _activeProcessorName;
            }
        }

        public int ActiveObjectCount
        {
            get
            {
                return _activeObjectCount;
            }
        }

        public int PendingExecutionCount
        {
            get
            {
                return _pendingExecutionCount;
            }
        }

        public double? LastExecutionDurationSeconds
        {
            get
            {
                return _lastExecutionDurationSeconds;
            }
        }

        public string LastExecutionError
        {
            get
            {
                return _lastExecutionError;
            }
        }

        public void SetActiveState(ProcessorState state)
        {
            _activeState = state;
            if (state == null || string.IsNullOrWhiteSpace(state.typeFullName))
            {
                return;
            }

            _states[state.typeFullName] = state;
        }

        public ProcessorState GetState(string typeFullName)
        {
            if (string.IsNullOrWhiteSpace(typeFullName))
            {
                return null;
            }

            _states.TryGetValue(typeFullName, out ProcessorState state);
            return state;
        }

        public void MarkExecutionStarted(string processorName, int objectCount, int pendingCount)
        {
            _isExecuting = true;
            _activeProcessorName = processorName ?? string.Empty;
            _activeObjectCount = objectCount < 0 ? 0 : objectCount;
            _pendingExecutionCount = pendingCount < 0 ? 0 : pendingCount;
            _lastExecutionError = null;
        }

        public void MarkExecutionCompleted(double durationSeconds, int pendingCount)
        {
            _isExecuting = false;
            _pendingExecutionCount = pendingCount < 0 ? 0 : pendingCount;
            _lastExecutionDurationSeconds = durationSeconds >= 0 ? durationSeconds : (double?)null;
            _lastExecutionError = null;
            _activeProcessorName = string.Empty;
            _activeObjectCount = 0;
        }

        public void MarkExecutionFailed(string errorMessage, int pendingCount)
        {
            _isExecuting = false;
            _pendingExecutionCount = pendingCount < 0 ? 0 : pendingCount;
            _lastExecutionError = errorMessage;
            _lastExecutionDurationSeconds = null;
            _activeProcessorName = string.Empty;
            _activeObjectCount = 0;
        }

        public void Clear()
        {
            _states.Clear();
            _activeState = null;
            _isExecuting = false;
            _activeProcessorName = string.Empty;
            _activeObjectCount = 0;
            _pendingExecutionCount = 0;
            _lastExecutionDurationSeconds = null;
            _lastExecutionError = null;
        }
    }
}
