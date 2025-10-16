namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer;

    internal sealed class ProcessorExecutionRequestedEvent
    {
        public ProcessorExecutionRequestedEvent(
            IDataProcessor processor,
            Type targetType,
            Color highlightColor,
            VisualElement anchor
        )
        {
            Processor = processor;
            TargetType = targetType;
            HighlightColor = highlightColor;
            Anchor = anchor;
        }

        public IDataProcessor Processor { get; }

        public Type TargetType { get; }

        public Color HighlightColor { get; }

        public VisualElement Anchor { get; }
    }
}
