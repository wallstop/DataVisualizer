namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;

    public enum ProcessorLogic
    {
        [Obsolete("Please use a valid value")]
        None = 0,
        All = 1,
        Filtered = 2,
    }

    [Serializable]
    public sealed class ProcessorState
    {
        public string typeFullName = string.Empty;
        public bool isCollapsed = true;
        public ProcessorLogic logic = ProcessorLogic.All;

        public ProcessorState Clone()
        {
            return new ProcessorState
            {
                typeFullName = typeFullName,
                isCollapsed = isCollapsed,
                logic = logic,
            };
        }
    }
}
