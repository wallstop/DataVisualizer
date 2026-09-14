using System.Diagnostics.CodeAnalysis;

/*
    Sanctioned SonarWay suppressions for the editor assembly, recorded by the owner on
    issue #85 (criteria 2-3). Every entry targets one specific site; no assembly-wide rule
    weakening. Enforcement contract: with the pinned SonarAnalyzer.CSharp set installed on
    the Unity host, compiling this assembly must produce zero S-diagnostics.
*/

[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Reserved invalid enum sentinel: value 0 stays obsolete by convention so valid values are nonzero.",
    Scope = "member",
    Target = "F:WallstopStudios.DataVisualizer.Editor.DragType.Unknown"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Reserved invalid enum sentinel: value 0 stays obsolete by convention so valid values are nonzero.",
    Scope = "member",
    Target = "F:WallstopStudios.DataVisualizer.Editor.FocusArea.Unknown"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Reserved invalid enum sentinel: value 0 stays obsolete by convention so valid values are nonzero.",
    Scope = "member",
    Target = "F:WallstopStudios.DataVisualizer.Editor.LabelFilterSection.None"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Reserved invalid enum sentinel: value 0 stays obsolete by convention so valid values are nonzero.",
    Scope = "member",
    Target = "F:WallstopStudios.DataVisualizer.Editor.Data.LabelCombinationType.None"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Reserved invalid enum sentinel: value 0 stays obsolete by convention so valid values are nonzero.",
    Scope = "member",
    Target = "F:WallstopStudios.DataVisualizer.Editor.Utilities.AssetGuidTypeIndexState.Unknown"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Reserved invalid enum sentinel: value 0 stays obsolete by convention so valid values are nonzero.",
    Scope = "member",
    Target = "F:WallstopStudios.DataVisualizer.Editor.Data.ProcessorLogic.None"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Serialized field kept for saved user-state compatibility; the superseded properties own reads and writes.",
    Scope = "member",
    Target = "F:WallstopStudios.DataVisualizer.Editor.DataVisualizer._hiddenNamespaces"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Serialized field kept for saved user-state compatibility; the superseded properties own reads and writes.",
    Scope = "member",
    Target = "F:WallstopStudios.DataVisualizer.Editor.DataVisualizer._userState"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Serialized field kept for saved settings compatibility; the superseded properties own reads and writes.",
    Scope = "member",
    Target = "F:WallstopStudios.DataVisualizer.Editor.DataVisualizer._settings"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Kept only for persisted user-state migration; internal callers use UserState.",
    Scope = "member",
    Target = "M:WallstopStudios.DataVisualizer.Editor.DataVisualizer.LoadUserStateFromFile"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1133",
    Justification = "Legacy placement helper kept for external callers; TryGetEditorPlacementRect replaced it.",
    Scope = "member",
    Target = "M:WallstopStudios.DataVisualizer.Editor.Utilities.MonitorUtility.TryGetPrimaryMonitorRect(UnityEngine.Rect@)"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2589",
    Justification = "Defensive null guard so late selection mutations cannot reach the non-null path; recorded false positive on #85.",
    Scope = "member",
    Target = "M:WallstopStudios.DataVisualizer.Editor.DataVisualizer.BuildInspectorView"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2589",
    Justification = "Condition is constant only under the ODIN_INSPECTOR define; the no-Odin path needs the branch when the define is absent.",
    Scope = "member",
    Target = "M:WallstopStudios.DataVisualizer.Editor.DataVisualizer.BuildInspectorView"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1751",
    Justification = "Deliberate first-element read of the selection stream; recorded on #85.",
    Scope = "member",
    Target = "M:WallstopStudios.DataVisualizer.Editor.DataVisualizer.OnObjectListSelectionChanged(System.Collections.Generic.IEnumerable{System.Object})"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S1751",
    Justification = "Structural continue before the local functions of this loop body; recorded on #85.",
    Scope = "member",
    Target = "M:WallstopStudios.DataVisualizer.Editor.NamespaceController.Build(WallstopStudios.DataVisualizer.Editor.DataVisualizer,UnityEngine.UIElements.VisualElement@)"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S4275",
    Justification = "The setter routes through SetValueWithoutNotify by design to pool and send one ChangeEvent; recorded false positive on #85.",
    Scope = "member",
    Target = "P:WallstopStudios.DataVisualizer.Editor.UI.ActionButtonToggle.value"
)]
