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
        public ProcessorLogic logic = ProcessorLogic.Filtered;

        public ProcessorState Clone()
        {
            return new ProcessorState
            {
                typeFullName = typeFullName ?? string.Empty,
                isCollapsed = isCollapsed,
#pragma warning disable CS0618 // Type or member is obsolete
                logic = logic == ProcessorLogic.None ? ProcessorLogic.Filtered : logic,
#pragma warning restore CS0618 // Type or member is obsolete
            };
        }
    }
}
