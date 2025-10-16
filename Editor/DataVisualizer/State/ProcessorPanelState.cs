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

        public ProcessorState ActiveState
        {
            get
            {
                return _activeState;
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

        public void Clear()
        {
            _states.Clear();
            _activeState = null;
        }
    }
}
