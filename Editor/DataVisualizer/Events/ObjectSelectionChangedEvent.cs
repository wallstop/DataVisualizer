namespace WallstopStudios.DataVisualizer.Editor.Events
{
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class ObjectSelectionChangedEvent
    {
        public ObjectSelectionChangedEvent(
            ScriptableObject primarySelection,
            IReadOnlyList<ScriptableObject> selections
        )
        {
            PrimarySelection = primarySelection;
            Selections = selections;
        }

        public ScriptableObject PrimarySelection { get; }

        public IReadOnlyList<ScriptableObject> Selections { get; }
    }
}
