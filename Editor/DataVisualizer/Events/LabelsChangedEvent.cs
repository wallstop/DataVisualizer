namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;
    using System.Collections.Generic;

    internal sealed class LabelsChangedEvent
    {
        public LabelsChangedEvent(
            IReadOnlyCollection<string> andLabels,
            IReadOnlyCollection<string> orLabels
        )
        {
            AndLabels = andLabels ?? Array.Empty<string>();
            OrLabels = orLabels ?? Array.Empty<string>();
        }

        public IReadOnlyCollection<string> AndLabels { get; }

        public IReadOnlyCollection<string> OrLabels { get; }
    }
}
